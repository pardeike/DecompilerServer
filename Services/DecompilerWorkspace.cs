using System.Text.RegularExpressions;
using System.Text.Json;

namespace DecompilerServer.Services;

public sealed class DecompilerWorkspace : IDisposable
{
    private const string DefaultContextAlias = "default";
    private static readonly JsonSerializerOptions RegistrySerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly ReaderWriterLockSlim _lock = new();
    private readonly Dictionary<string, DecompilerSession> _sessionsByAlias = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _aliasByMvid = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _registryPath;
    private WorkspaceRegistryState _registryState;
    private bool _disposed;

    public string? CurrentContextAlias { get; private set; }

    public DecompilerWorkspace(string? registryPath = null)
    {
        _registryPath = registryPath ?? GetDefaultRegistryPath();
        _registryState = LoadRegistryState(_registryPath);
    }

    public WorkspaceContextInfo LoadAssembly(WorkspaceLoadRequest request)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        ValidateRequest(request);

        var contextAlias = NormalizeAlias(request.ContextAlias);
        var session = DecompilerSession.Create(contextAlias, request);

        _lock.EnterWriteLock();
        try
        {
            if (_sessionsByAlias.TryGetValue(contextAlias, out var existing))
            {
                RemoveSessionMappings(existing);
                existing.Dispose();
            }

            _sessionsByAlias[contextAlias] = session;
            _aliasByMvid[session.ContextManager.Mvid!] = contextAlias;

            if (request.MakeCurrent || CurrentContextAlias == null)
            {
                CurrentContextAlias = contextAlias;
            }

            if (request.PersistRegistration)
            {
                UpsertRegistryEntry(contextAlias, request);
                SaveRegistryState();
            }

            return session.ToContextInfo(isCurrent: string.Equals(CurrentContextAlias, contextAlias, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            session.Dispose();
            throw;
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public WorkspaceContextInfo SelectContext(string contextAlias)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (string.IsNullOrWhiteSpace(contextAlias))
            throw new ArgumentException("Context alias cannot be empty.", nameof(contextAlias));

        _lock.EnterWriteLock();
        try
        {
            if (!_sessionsByAlias.TryGetValue(contextAlias, out var session))
                throw new InvalidOperationException($"Context alias '{contextAlias}' is not loaded.");

            CurrentContextAlias = session.ContextAlias;
            SaveRegistryState();
            return session.ToContextInfo(isCurrent: true);
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public IReadOnlyList<WorkspaceContextInfo> ListContexts()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _lock.EnterReadLock();
        try
        {
            return _sessionsByAlias.Values
                .Select(session => session.ToContextInfo(isCurrent: string.Equals(CurrentContextAlias, session.ContextAlias, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(info => info.ContextAlias, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public bool TryGetCurrentSession(out DecompilerSession session)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _lock.EnterReadLock();
        try
        {
            if (CurrentContextAlias != null && _sessionsByAlias.TryGetValue(CurrentContextAlias, out session!))
                return true;

            session = null!;
            return false;
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public DecompilerSession GetCurrentSession()
    {
        if (TryGetCurrentSession(out var session))
            return session;

        throw new InvalidOperationException("No context is currently selected.");
    }

    public bool TryGetSession(string contextAlias, out DecompilerSession session)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _lock.EnterReadLock();
        try
        {
            return _sessionsByAlias.TryGetValue(contextAlias, out session!);
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public bool TryGetSessionByMvid(string mvid, out DecompilerSession session)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (string.IsNullOrWhiteSpace(mvid))
        {
            session = null!;
            return false;
        }

        _lock.EnterReadLock();
        try
        {
            if (_aliasByMvid.TryGetValue(mvid, out var alias) && _sessionsByAlias.TryGetValue(alias, out session!))
                return true;

            session = null!;
            return false;
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public DecompilerSession ResolveSessionForMemberId(string memberId)
    {
        if (string.IsNullOrWhiteSpace(memberId))
            throw new ArgumentException("Member ID cannot be empty.", nameof(memberId));

        var separatorIndex = memberId.IndexOf(':');
        if (separatorIndex > 0)
        {
            var prefix = memberId[..separatorIndex];
            if (LooksLikeMvid(prefix) && TryGetSessionByMvid(prefix, out var sessionByMvid))
                return sessionByMvid;
        }

        return GetCurrentSession();
    }

    public IReadOnlyList<WorkspaceRestoreResult> RestoreRegisteredContexts()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var entries = _registryState.Contexts
            .OrderBy(entry => entry.ContextAlias, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var results = new List<WorkspaceRestoreResult>();

        foreach (var entry in entries)
        {
            try
            {
                var loadRequest = new WorkspaceLoadRequest
                {
                    GameDir = entry.GameDir,
                    AssemblyPath = entry.AssemblyPath,
                    AssemblyFile = entry.AssemblyFile,
                    AdditionalSearchDirs = entry.AdditionalSearchDirs,
                    RebuildIndex = entry.RebuildIndex,
                    ContextAlias = entry.ContextAlias,
                    MakeCurrent = false,
                    PersistRegistration = false
                };

                var info = LoadAssembly(loadRequest);
                results.Add(new WorkspaceRestoreResult
                {
                    ContextAlias = info.ContextAlias,
                    AssemblyPath = info.AssemblyPath,
                    Loaded = true
                });
            }
            catch (Exception ex)
            {
                results.Add(new WorkspaceRestoreResult
                {
                    ContextAlias = entry.ContextAlias,
                    AssemblyPath = entry.AssemblyPath ?? entry.GameDir ?? entry.AssemblyFile,
                    Loaded = false,
                    ErrorMessage = ex.Message
                });
            }
        }

        if (!string.IsNullOrWhiteSpace(_registryState.CurrentContextAlias) &&
            TryGetSession(_registryState.CurrentContextAlias, out _))
        {
            SelectContext(_registryState.CurrentContextAlias);
        }

        return results;
    }

    public void UnloadContext(string? contextAlias = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _lock.EnterWriteLock();
        try
        {
            var aliasToUnload = string.IsNullOrWhiteSpace(contextAlias) ? CurrentContextAlias : contextAlias;
            if (string.IsNullOrWhiteSpace(aliasToUnload))
                throw new InvalidOperationException("No context is currently selected.");

            if (!_sessionsByAlias.TryGetValue(aliasToUnload, out var session))
                throw new InvalidOperationException($"Context alias '{aliasToUnload}' is not loaded.");

            RemoveSessionMappings(session);
            session.Dispose();

            if (CurrentContextAlias == null && _sessionsByAlias.Count > 0)
            {
                CurrentContextAlias = _sessionsByAlias.Keys.OrderBy(alias => alias, StringComparer.OrdinalIgnoreCase).First();
            }

            SaveRegistryState();
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public void UnloadAllContexts()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _lock.EnterWriteLock();
        try
        {
            foreach (var session in _sessionsByAlias.Values)
            {
                session.Dispose();
            }

            _sessionsByAlias.Clear();
            _aliasByMvid.Clear();
            CurrentContextAlias = null;
            SaveRegistryState();
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _lock.EnterWriteLock();
        try
        {
            if (_disposed)
                return;

            foreach (var session in _sessionsByAlias.Values)
            {
                session.Dispose();
            }

            _sessionsByAlias.Clear();
            _aliasByMvid.Clear();
            CurrentContextAlias = null;
            _disposed = true;
        }
        finally
        {
            _lock.ExitWriteLock();
            _lock.Dispose();
        }
    }

    private static void ValidateRequest(WorkspaceLoadRequest request)
    {
        if (request.GameDir != null && request.AssemblyPath != null)
            throw new ArgumentException("Cannot specify both gameDir and assemblyPath. Use gameDir for Unity projects or assemblyPath for direct assembly loading.");

        if (request.GameDir == null && request.AssemblyPath == null)
            throw new ArgumentException("Must specify either gameDir (for Unity projects) or assemblyPath (for direct assembly loading).");
    }

    private static string NormalizeAlias(string? contextAlias)
    {
        if (string.IsNullOrWhiteSpace(contextAlias))
            return DefaultContextAlias;

        return contextAlias.Trim();
    }

    private static bool LooksLikeMvid(string prefix)
    {
        return Regex.IsMatch(prefix, "^[0-9A-Fa-f]{32}$");
    }

    private void UpsertRegistryEntry(string contextAlias, WorkspaceLoadRequest request)
    {
        _registryState.Contexts.RemoveAll(entry => string.Equals(entry.ContextAlias, contextAlias, StringComparison.OrdinalIgnoreCase));
        _registryState.Contexts.Add(new WorkspaceRegistryEntry
        {
            ContextAlias = contextAlias,
            GameDir = request.GameDir,
            AssemblyPath = request.AssemblyPath,
            AssemblyFile = request.AssemblyFile,
            AdditionalSearchDirs = request.AdditionalSearchDirs,
            RebuildIndex = request.RebuildIndex
        });
        _registryState.CurrentContextAlias = CurrentContextAlias;
    }

    private void SaveRegistryState()
    {
        _registryState.CurrentContextAlias = CurrentContextAlias;

        var directory = Path.GetDirectoryName(_registryPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(_registryState, RegistrySerializerOptions);
        File.WriteAllText(_registryPath, json);
    }

    private static WorkspaceRegistryState LoadRegistryState(string registryPath)
    {
        if (!File.Exists(registryPath))
            return new WorkspaceRegistryState();

        try
        {
            var json = File.ReadAllText(registryPath);
            return JsonSerializer.Deserialize<WorkspaceRegistryState>(json, RegistrySerializerOptions)
                ?? new WorkspaceRegistryState();
        }
        catch
        {
            return new WorkspaceRegistryState();
        }
    }

    private static string GetDefaultRegistryPath()
    {
        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(baseDir))
        {
            baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".decompilerserver");
        }

        return Path.Combine(baseDir, "DecompilerServer", "contexts.json");
    }

    private void RemoveSessionMappings(DecompilerSession session)
    {
        _sessionsByAlias.Remove(session.ContextAlias);
        if (session.ContextManager.Mvid != null)
            _aliasByMvid.Remove(session.ContextManager.Mvid);

        if (string.Equals(CurrentContextAlias, session.ContextAlias, StringComparison.OrdinalIgnoreCase))
            CurrentContextAlias = null;
    }
}

public sealed class DecompilerSession : IDisposable
{
    private bool _disposed;

    private DecompilerSession(
        string contextAlias,
        AssemblyContextManager contextManager,
        MemberResolver memberResolver,
        DecompilerService decompilerService,
        UsageAnalyzer usageAnalyzer,
        InheritanceAnalyzer inheritanceAnalyzer)
    {
        ContextAlias = contextAlias;
        ContextManager = contextManager;
        MemberResolver = memberResolver;
        DecompilerService = decompilerService;
        UsageAnalyzer = usageAnalyzer;
        InheritanceAnalyzer = inheritanceAnalyzer;
    }

    public string ContextAlias { get; }
    public AssemblyContextManager ContextManager { get; }
    public MemberResolver MemberResolver { get; }
    public DecompilerService DecompilerService { get; }
    public UsageAnalyzer UsageAnalyzer { get; }
    public InheritanceAnalyzer InheritanceAnalyzer { get; }

    public static DecompilerSession Create(string contextAlias, WorkspaceLoadRequest request)
    {
        var contextManager = new AssemblyContextManager();

        if (request.GameDir != null)
            contextManager.LoadAssembly(request.GameDir, request.AssemblyFile, request.AdditionalSearchDirs);
        else
            contextManager.LoadAssemblyDirect(request.AssemblyPath!, request.AdditionalSearchDirs);

        if (request.RebuildIndex)
            contextManager.WarmIndexes();

        var memberResolver = new MemberResolver(contextManager);
        var decompilerService = new DecompilerService(contextManager, memberResolver);
        var usageAnalyzer = new UsageAnalyzer(contextManager, memberResolver);
        var inheritanceAnalyzer = new InheritanceAnalyzer(contextManager, memberResolver);

        return new DecompilerSession(contextAlias, contextManager, memberResolver, decompilerService, usageAnalyzer, inheritanceAnalyzer);
    }

    public WorkspaceContextInfo ToContextInfo(bool isCurrent)
    {
        return new WorkspaceContextInfo
        {
            ContextAlias = ContextAlias,
            Mvid = ContextManager.Mvid!,
            AssemblyPath = ContextManager.AssemblyPath!,
            LoadedAtUnix = ContextManager.LoadedAtUtc?.Ticks,
            TypeCount = ContextManager.TypeCount,
            MethodCount = ContextManager.GetAllTypes().Sum(type => type.Methods.Count()),
            NamespaceCount = ContextManager.NamespaceCount,
            IsCurrent = isCurrent
        };
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        ContextManager.Dispose();
        _disposed = true;
    }
}

public sealed record WorkspaceLoadRequest
{
    public string? GameDir { get; init; }
    public string? AssemblyPath { get; init; }
    public string AssemblyFile { get; init; } = "Assembly-CSharp.dll";
    public string[]? AdditionalSearchDirs { get; init; }
    public bool RebuildIndex { get; init; } = true;
    public string? ContextAlias { get; init; }
    public bool MakeCurrent { get; init; } = true;
    public bool PersistRegistration { get; init; } = true;
}

public sealed record WorkspaceContextInfo
{
    public required string ContextAlias { get; init; }
    public required string Mvid { get; init; }
    public required string AssemblyPath { get; init; }
    public long? LoadedAtUnix { get; init; }
    public int TypeCount { get; init; }
    public int MethodCount { get; init; }
    public int NamespaceCount { get; init; }
    public bool IsCurrent { get; init; }
}

public sealed record WorkspaceRestoreResult
{
    public required string ContextAlias { get; init; }
    public required string AssemblyPath { get; init; }
    public bool Loaded { get; init; }
    public string? ErrorMessage { get; init; }
}

internal sealed record WorkspaceRegistryState
{
    public string? CurrentContextAlias { get; set; }
    public List<WorkspaceRegistryEntry> Contexts { get; init; } = new();
}

internal sealed record WorkspaceRegistryEntry
{
    public required string ContextAlias { get; init; }
    public string? GameDir { get; init; }
    public string? AssemblyPath { get; init; }
    public string AssemblyFile { get; init; } = "Assembly-CSharp.dll";
    public string[]? AdditionalSearchDirs { get; init; }
    public bool RebuildIndex { get; init; } = true;
}
