using System.Text.RegularExpressions;
using System.Text.Json;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using ICSharpCode.Decompiler;

namespace DecompilerServer.Services;

public sealed class DecompilerWorkspace : IDisposable
{
    private const string DefaultContextAlias = "default";
    public const int DefaultMaxLoadedContexts = 4;
    public const string MaxLoadedContextsEnvironmentVariable = "DECOMPILER_MAX_LOADED_CONTEXTS";
    private static readonly JsonSerializerOptions RegistrySerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly ReaderWriterLockSlim _lock = new();
    private readonly object _activationLock = new();
    private readonly Dictionary<string, LoadedSessionState> _sessionsByAlias = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _aliasByMvid = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _mvidByAlias = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, WorkspaceLoadRequest> _loadRequestsByAlias = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DecompilerSettings> _settingsByAlias = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _evictedAliases = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<string, WorkspaceLoadRequest, DecompilerSession> _sessionFactory;
    private readonly string _registryPath;
    private WorkspaceRegistryState _registryState;
    private long _accessSequence;
    private long _evictionCount;
    private long _reloadCount;
    private bool _disposed;

    public string? CurrentContextAlias { get; private set; }
    public int MaxLoadedContexts { get; }

    public DecompilerWorkspace(string? registryPath = null, int? maxLoadedContexts = null)
        : this(registryPath, maxLoadedContexts, DecompilerSession.Create)
    {
    }

    internal DecompilerWorkspace(
        string? registryPath,
        int? maxLoadedContexts,
        Func<string, WorkspaceLoadRequest, DecompilerSession> sessionFactory)
    {
        _sessionFactory = sessionFactory ?? throw new ArgumentNullException(nameof(sessionFactory));
        MaxLoadedContexts = maxLoadedContexts ?? ReadMaxLoadedContextsFromEnvironment();
        if (MaxLoadedContexts <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxLoadedContexts), "The loaded context limit must be greater than zero.");

        _registryPath = registryPath ?? GetDefaultRegistryPath();
        _registryState = LoadRegistryState(_registryPath);
        foreach (var entry in _registryState.Contexts)
        {
            _loadRequestsByAlias[entry.ContextAlias] = CreateLoadRequest(entry);
        }

        CurrentContextAlias = _registryState.Contexts.Any(entry =>
            string.Equals(entry.ContextAlias, _registryState.CurrentContextAlias, StringComparison.OrdinalIgnoreCase))
            ? _registryState.CurrentContextAlias
            : null;
    }

    public WorkspaceContextInfo LoadAssembly(WorkspaceLoadRequest request)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ValidateRequest(request);

        lock (_activationLock)
        {
            return LoadAssemblyCore(request, restoreRuntimeSettings: false);
        }
    }

    public WorkspaceContextInfo SelectContext(string contextAlias)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (string.IsNullOrWhiteSpace(contextAlias))
            throw new ArgumentException("Context alias cannot be empty.", nameof(contextAlias));

        using var lease = AcquireSession(contextAlias);
        _lock.EnterWriteLock();
        try
        {
            CurrentContextAlias = lease.Session.ContextAlias;
            SaveRegistryState();
            return lease.Session.ToContextInfo(isCurrent: true);
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
                .Select(state => state.Session.ToContextInfo(isCurrent: string.Equals(CurrentContextAlias, state.Session.ContextAlias, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(info => info.ContextAlias, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    // Snapshot and lease only resident contexts atomically. Graph queries never
    // activate historical registrations or race an eviction between listing and use.
    public IReadOnlyList<DecompilerSessionLease> AcquireLoadedSessions()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _lock.EnterWriteLock();
        try
        {
            var leases = new List<DecompilerSessionLease>();
            foreach (var alias in _sessionsByAlias.Keys.OrderBy(alias => alias, StringComparer.Ordinal).ToArray())
                if (TryAcquireLoadedSessionLocked(alias, out var lease))
                    leases.Add(lease);
            return leases;
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public bool TryAcquireCurrentLoadedSession(out DecompilerSessionLease lease)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _lock.EnterWriteLock();
        try
        {
            if (CurrentContextAlias != null && TryAcquireLoadedSessionLocked(CurrentContextAlias, out lease))
                return true;

            lease = null!;
            return false;
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public DecompilerSessionLease AcquireCurrentSession()
    {
        string? currentContextAlias;
        _lock.EnterReadLock();
        try
        {
            currentContextAlias = CurrentContextAlias;
        }
        finally
        {
            _lock.ExitReadLock();
        }

        if (currentContextAlias == null)
            throw new InvalidOperationException("No context is currently selected.");

        return AcquireSession(currentContextAlias);
    }

    public DecompilerSessionLease AcquireSession(string contextAlias)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (string.IsNullOrWhiteSpace(contextAlias))
            throw new ArgumentException("Context alias cannot be empty.", nameof(contextAlias));

        var normalizedAlias = NormalizeAlias(contextAlias);

        _lock.EnterWriteLock();
        try
        {
            if (TryAcquireLoadedSessionLocked(normalizedAlias, out var loadedLease))
                return loadedLease;
        }
        finally
        {
            _lock.ExitWriteLock();
        }

        lock (_activationLock)
        {
            _lock.EnterWriteLock();
            try
            {
                if (TryAcquireLoadedSessionLocked(normalizedAlias, out var loadedLease))
                    return loadedLease;
            }
            finally
            {
                _lock.ExitWriteLock();
            }

            WorkspaceLoadRequest? request;
            _lock.EnterReadLock();
            try
            {
                _loadRequestsByAlias.TryGetValue(normalizedAlias, out request);
            }
            finally
            {
                _lock.ExitReadLock();
            }

            if (request == null)
                throw new InvalidOperationException($"Context alias '{contextAlias}' is not loaded or registered.");

            LoadAssemblyCore(request, restoreRuntimeSettings: true);

            _lock.EnterWriteLock();
            try
            {
                if (TryAcquireLoadedSessionLocked(normalizedAlias, out var loadedLease))
                    return loadedLease;
            }
            finally
            {
                _lock.ExitWriteLock();
            }

            throw new InvalidOperationException($"Context alias '{contextAlias}' could not be loaded.");
        }
    }

    public IReadOnlyList<string> ListRegisteredAliases()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _lock.EnterReadLock();
        try
        {
            return _registryState.Contexts
                .Select(entry => entry.ContextAlias)
                .OrderBy(alias => alias, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public DecompilerSessionLease AcquireSessionForMemberId(string memberId)
    {
        if (string.IsNullOrWhiteSpace(memberId))
            throw new ArgumentException("Member ID cannot be empty.", nameof(memberId));

        var separatorIndex = memberId.IndexOf(':');
        if (separatorIndex > 0)
        {
            var prefix = memberId[..separatorIndex];
            if (LooksLikeMvid(prefix))
            {
                string? contextAlias;
                _lock.EnterReadLock();
                try
                {
                    _aliasByMvid.TryGetValue(prefix, out contextAlias);
                }
                finally
                {
                    _lock.ExitReadLock();
                }

                if (contextAlias != null)
                    return AcquireSession(contextAlias);
            }
        }

        return AcquireCurrentSession();
    }

    public void UnloadContext(string? contextAlias = null, bool preserveRegistration = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        DecompilerSession? sessionToDispose = null;
        lock (_activationLock)
        {
            try
            {
                _lock.EnterWriteLock();
                try
                {
                    var aliasToUnload = string.IsNullOrWhiteSpace(contextAlias) ? CurrentContextAlias : NormalizeAlias(contextAlias);
                    if (string.IsNullOrWhiteSpace(aliasToUnload))
                        throw new InvalidOperationException("No context is currently selected.");

                    var isAvailable = _loadRequestsByAlias.ContainsKey(aliasToUnload);
                    var isLoaded = _sessionsByAlias.TryGetValue(aliasToUnload, out var state);
                    if (!isLoaded && !isAvailable)
                        throw new InvalidOperationException($"Context alias '{aliasToUnload}' is not loaded or registered.");

                    if (isLoaded)
                    {
                        EnsureNotLeased(state!, aliasToUnload);
                        if (preserveRegistration)
                            _settingsByAlias[aliasToUnload] = state!.Session.ContextManager.GetSettings();

                        _sessionsByAlias.Remove(aliasToUnload);
                        sessionToDispose = state!.Session;
                    }

                    var wasCurrent = string.Equals(CurrentContextAlias, aliasToUnload, StringComparison.OrdinalIgnoreCase);
                    if (!preserveRegistration)
                    {
                        _registryState.Contexts.RemoveAll(entry => string.Equals(entry.ContextAlias, aliasToUnload, StringComparison.OrdinalIgnoreCase));
                        _loadRequestsByAlias.Remove(aliasToUnload);
                        _settingsByAlias.Remove(aliasToUnload);
                        _evictedAliases.Remove(aliasToUnload);
                        RemoveAliasRoutingLocked(aliasToUnload);
                    }

                    if (wasCurrent && !preserveRegistration)
                    {
                        CurrentContextAlias = _sessionsByAlias.Keys
                            .Concat(_loadRequestsByAlias.Keys)
                            .OrderBy(alias => alias, StringComparer.OrdinalIgnoreCase)
                            .FirstOrDefault();
                    }

                    SaveRegistryState();
                }
                finally
                {
                    _lock.ExitWriteLock();
                }
            }
            finally
            {
                sessionToDispose?.Dispose();
            }
        }
    }

    public void UnloadAllContexts(bool preserveRegistration = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var sessionsToDispose = new List<DecompilerSession>();
        lock (_activationLock)
        {
            try
            {
                _lock.EnterWriteLock();
                try
                {
                    var leasedAliases = _sessionsByAlias.Values
                        .Where(state => state.ActiveLeaseCount > 0)
                        .Select(state => state.Session.ContextAlias)
                        .OrderBy(alias => alias, StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                    if (leasedAliases.Length > 0)
                        throw CreateContextBusyError(leasedAliases);

                    var selectedAlias = CurrentContextAlias;
                    if (preserveRegistration)
                    {
                        foreach (var state in _sessionsByAlias.Values)
                        {
                            _settingsByAlias[state.Session.ContextAlias] = state.Session.ContextManager.GetSettings();
                        }
                    }

                    sessionsToDispose = _sessionsByAlias.Values.Select(state => state.Session).ToList();
                    _sessionsByAlias.Clear();
                    if (!preserveRegistration)
                    {
                        _registryState.Contexts.Clear();
                        _loadRequestsByAlias.Clear();
                        _settingsByAlias.Clear();
                        _aliasByMvid.Clear();
                        _mvidByAlias.Clear();
                        _evictedAliases.Clear();
                        CurrentContextAlias = null;
                    }
                    else
                    {
                        CurrentContextAlias = selectedAlias != null && _loadRequestsByAlias.ContainsKey(selectedAlias)
                            ? selectedAlias
                            : null;
                    }

                    SaveRegistryState();
                }
                finally
                {
                    _lock.ExitWriteLock();
                }
            }
            finally
            {
                foreach (var session in sessionsToDispose)
                {
                    session.Dispose();
                }
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        lock (_activationLock)
        {
            _lock.EnterWriteLock();
            try
            {
                if (_disposed)
                    return;

                foreach (var state in _sessionsByAlias.Values)
                {
                    state.Session.Dispose();
                }

                _sessionsByAlias.Clear();
                _aliasByMvid.Clear();
                _mvidByAlias.Clear();
                _loadRequestsByAlias.Clear();
                _settingsByAlias.Clear();
                _evictedAliases.Clear();
                CurrentContextAlias = null;
                _disposed = true;
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }
    }

    public WorkspaceMemoryStats GetMemoryStats()
    {
        return GetMemoryStats(excludedSession: null);
    }

    internal WorkspaceMemoryStats GetMemoryStats(DecompilerSession? excludedSession)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _lock.EnterReadLock();
        try
        {
            var excludedLeaseCount = excludedSession != null
                && _sessionsByAlias.TryGetValue(excludedSession.ContextAlias, out var excludedState)
                && ReferenceEquals(excludedState.Session, excludedSession)
                && excludedState.ActiveLeaseCount > 0
                    ? 1
                    : 0;

            return new WorkspaceMemoryStats
            {
                MaxLoadedContexts = MaxLoadedContexts,
                LoadedContexts = _sessionsByAlias.Count,
                ActiveLeases = _sessionsByAlias.Values.Sum(state => state.ActiveLeaseCount) - excludedLeaseCount,
                Evictions = _evictionCount,
                Reloads = _reloadCount,
                LeasedAliases = _sessionsByAlias.Values
                    .Where(state => state.ActiveLeaseCount - (ReferenceEquals(state.Session, excludedSession) ? excludedLeaseCount : 0) > 0)
                    .Select(state => state.Session.ContextAlias)
                    .OrderBy(alias => alias, StringComparer.OrdinalIgnoreCase)
                    .ToArray()
            };
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    internal void ReleaseSession(DecompilerSession session)
    {
        if (_disposed)
            return;

        _lock.EnterWriteLock();
        try
        {
            if (_sessionsByAlias.TryGetValue(session.ContextAlias, out var state)
                && ReferenceEquals(state.Session, session)
                && state.ActiveLeaseCount > 0)
            {
                state.ActiveLeaseCount--;
                state.LastUsedSequence = ++_accessSequence;
            }
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    private WorkspaceContextInfo LoadAssemblyCore(WorkspaceLoadRequest request, bool restoreRuntimeSettings)
    {
        var contextAlias = NormalizeAlias(request.ContextAlias);
        PreflightLoadRequest(request);

        DisplacedSessionState? displacedSession = null;
        DecompilerSession? sessionToDispose = null;
        DecompilerSettings? settingsToRestore = null;

        _lock.EnterWriteLock();
        try
        {
            if (_sessionsByAlias.TryGetValue(contextAlias, out var existingState))
            {
                EnsureNotLeased(existingState, contextAlias);
                displacedSession = CaptureDisplacedSessionLocked(existingState, wasLruEviction: false);
                sessionToDispose = existingState.Session;
                _sessionsByAlias.Remove(contextAlias);
            }
            else if (_sessionsByAlias.Count >= MaxLoadedContexts)
            {
                var evictionCandidate = _sessionsByAlias.Values
                    .Where(state => state.ActiveLeaseCount == 0)
                    .OrderBy(state => state.LastUsedSequence)
                    .ThenBy(state => state.Session.ContextAlias, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();

                if (evictionCandidate == null)
                    throw CreateCapacityBusyError();

                var evictedAlias = evictionCandidate.Session.ContextAlias;
                displacedSession = CaptureDisplacedSessionLocked(evictionCandidate, wasLruEviction: true);
                sessionToDispose = evictionCandidate.Session;
                _settingsByAlias[evictedAlias] = displacedSession.Settings;
                _sessionsByAlias.Remove(evictedAlias);
                _evictedAliases.Add(evictedAlias);
                _evictionCount++;
            }

            if (restoreRuntimeSettings)
                _settingsByAlias.TryGetValue(contextAlias, out settingsToRestore);
            else
                _settingsByAlias.Remove(contextAlias);
        }
        finally
        {
            _lock.ExitWriteLock();
        }

        sessionToDispose?.Dispose();
        sessionToDispose = null;

        DecompilerSession? createdSession = null;
        try
        {
            createdSession = _sessionFactory(contextAlias, request);
            if (settingsToRestore != null)
                createdSession.ContextManager.UpdateSettings(settingsToRestore);
        }
        catch (Exception loadException)
        {
            RollBackFailedLoad(loadException, createdSession, displacedSession);
            throw;
        }

        var session = createdSession!;
        var activationRequest = CreateActivationRequest(contextAlias, request);
        var sessionOwnedByWorkspace = false;
        try
        {
            _lock.EnterWriteLock();
            try
            {
                var nextCurrentContextAlias = request.MakeCurrent || CurrentContextAlias == null
                    ? contextAlias
                    : CurrentContextAlias;
                var contextInfo = session.ToContextInfo(isCurrent: string.Equals(
                    nextCurrentContextAlias,
                    contextAlias,
                    StringComparison.OrdinalIgnoreCase));
                WorkspaceRegistryState? persistedRegistryState = null;
                if (request.PersistRegistration)
                {
                    persistedRegistryState = CreateRegistryStateWithEntry(
                        contextAlias,
                        request,
                        nextCurrentContextAlias);
                    SaveRegistryState(persistedRegistryState);
                }

                _sessionsByAlias[contextAlias] = new LoadedSessionState(session, ++_accessSequence);
                sessionOwnedByWorkspace = true;
                UpdateAliasRoutingLocked(session);
                _loadRequestsByAlias[contextAlias] = activationRequest;

                if (restoreRuntimeSettings && _evictedAliases.Remove(contextAlias))
                    _reloadCount++;
                else if (!restoreRuntimeSettings)
                    _evictedAliases.Remove(contextAlias);

                CurrentContextAlias = nextCurrentContextAlias;

                if (persistedRegistryState != null)
                    _registryState = persistedRegistryState;

                return contextInfo;
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }
        catch (Exception loadException)
        {
            if (!sessionOwnedByWorkspace)
                RollBackFailedLoad(loadException, session, displacedSession);
            throw;
        }
    }

    private void RollBackFailedLoad(
        Exception loadException,
        DecompilerSession? createdSession,
        DisplacedSessionState? displacedSession)
    {
        createdSession?.Dispose();
        try
        {
            RestoreDisplacedSession(displacedSession);
        }
        catch (Exception restoreException)
        {
            loadException.Data["displacedContextRestoreError"] = restoreException.Message;
        }
    }

    private static void PreflightLoadRequest(WorkspaceLoadRequest request)
    {
        var assemblyPath = request.AssemblyPath
            ?? AssemblyContextManager.ResolveAssemblyPath(request.GameDir!, request.AssemblyFile);

        if (!File.Exists(assemblyPath))
            throw new FileNotFoundException($"Assembly not found: {assemblyPath}");

        using var stream = new FileStream(
            assemblyPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var peReader = new PEReader(stream, PEStreamOptions.LeaveOpen);
        if (!peReader.HasMetadata)
            throw new BadImageFormatException($"File does not contain managed metadata: {assemblyPath}");

        _ = peReader.GetMetadataReader().GetModuleDefinition();
    }

    private DisplacedSessionState CaptureDisplacedSessionLocked(LoadedSessionState state, bool wasLruEviction)
    {
        var contextAlias = state.Session.ContextAlias;
        if (!_loadRequestsByAlias.TryGetValue(contextAlias, out var loadRequest))
            throw new InvalidOperationException($"Loaded context '{contextAlias}' has no activation request for rollback.");

        return new DisplacedSessionState(
            contextAlias,
            loadRequest,
            state.Session.ContextManager.GetSettings(),
            state.LastUsedSequence,
            wasLruEviction);
    }

    private void RestoreDisplacedSession(DisplacedSessionState? displacedSession)
    {
        if (displacedSession == null)
            return;

        var contextAlias = displacedSession.ContextAlias;
        var restoredSession = _sessionFactory(contextAlias, displacedSession.LoadRequest);
        var sessionOwnedByWorkspace = false;
        try
        {
            restoredSession.ContextManager.UpdateSettings(displacedSession.Settings);

            _lock.EnterWriteLock();
            try
            {
                if (_sessionsByAlias.Count >= MaxLoadedContexts)
                    throw new InvalidOperationException("The displaced context cannot be restored without exceeding the loaded context limit.");

                _sessionsByAlias[contextAlias] = new LoadedSessionState(restoredSession, displacedSession.LastUsedSequence);
                sessionOwnedByWorkspace = true;
                UpdateAliasRoutingLocked(restoredSession);
                _loadRequestsByAlias[contextAlias] = displacedSession.LoadRequest;
                _settingsByAlias[contextAlias] = displacedSession.Settings;

                if (displacedSession.WasLruEviction && _evictedAliases.Remove(contextAlias))
                    _reloadCount++;
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }
        finally
        {
            if (!sessionOwnedByWorkspace)
                restoredSession.Dispose();
        }
    }

    private bool TryAcquireLoadedSessionLocked(string contextAlias, out DecompilerSessionLease lease)
    {
        if (_sessionsByAlias.TryGetValue(contextAlias, out var state))
        {
            state.ActiveLeaseCount++;
            state.LastUsedSequence = ++_accessSequence;
            lease = new DecompilerSessionLease(this, state.Session);
            return true;
        }

        lease = null!;
        return false;
    }

    private ToolErrorException CreateCapacityBusyError()
    {
        var leasedAliases = _sessionsByAlias.Values
            .Where(state => state.ActiveLeaseCount > 0)
            .Select(state => state.Session.ContextAlias)
            .OrderBy(alias => alias, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new ToolErrorException(
            "context_capacity_busy",
            "All loaded contexts are currently in use.",
            new
            {
                maxLoadedContexts = MaxLoadedContexts,
                loadedContexts = _sessionsByAlias.Count,
                activeLeases = _sessionsByAlias.Values.Sum(state => state.ActiveLeaseCount),
                leasedAliases
            });
    }

    private static void EnsureNotLeased(LoadedSessionState state, string contextAlias)
    {
        if (state.ActiveLeaseCount > 0)
            throw CreateContextBusyError([contextAlias]);
    }

    private static ToolErrorException CreateContextBusyError(IReadOnlyCollection<string> contextAliases)
    {
        return new ToolErrorException(
            "context_busy",
            "One or more contexts are currently in use.",
            new { contextAliases });
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

    private static WorkspaceLoadRequest CreateLoadRequest(WorkspaceRegistryEntry entry)
    {
        return new WorkspaceLoadRequest
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
    }

    private static WorkspaceLoadRequest CreateActivationRequest(string contextAlias, WorkspaceLoadRequest request)
    {
        return new WorkspaceLoadRequest
        {
            GameDir = request.GameDir,
            AssemblyPath = request.AssemblyPath,
            AssemblyFile = request.AssemblyFile,
            AdditionalSearchDirs = request.AdditionalSearchDirs,
            RebuildIndex = request.RebuildIndex,
            ContextAlias = contextAlias,
            MakeCurrent = false,
            PersistRegistration = false
        };
    }

    private WorkspaceRegistryState CreateRegistryStateWithEntry(
        string contextAlias,
        WorkspaceLoadRequest request,
        string? currentContextAlias)
    {
        var contexts = _registryState.Contexts
            .Where(entry => !string.Equals(entry.ContextAlias, contextAlias, StringComparison.OrdinalIgnoreCase))
            .ToList();
        contexts.Add(new WorkspaceRegistryEntry
        {
            ContextAlias = contextAlias,
            GameDir = request.GameDir,
            AssemblyPath = request.AssemblyPath,
            AssemblyFile = request.AssemblyFile,
            AdditionalSearchDirs = request.AdditionalSearchDirs,
            RebuildIndex = request.RebuildIndex
        });

        return new WorkspaceRegistryState
        {
            CurrentContextAlias = currentContextAlias,
            Contexts = contexts
        };
    }

    private void SaveRegistryState()
    {
        _registryState.CurrentContextAlias = CurrentContextAlias;
        SaveRegistryState(_registryState);
    }

    private void SaveRegistryState(WorkspaceRegistryState registryState)
    {
        var directory = Path.GetDirectoryName(_registryPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(registryState, RegistrySerializerOptions);
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

    private static int ReadMaxLoadedContextsFromEnvironment()
    {
        var configuredValue = Environment.GetEnvironmentVariable(MaxLoadedContextsEnvironmentVariable);
        return int.TryParse(configuredValue, out var parsedValue) && parsedValue > 0
            ? parsedValue
            : DefaultMaxLoadedContexts;
    }

    private void UpdateAliasRoutingLocked(DecompilerSession session)
    {
        var contextAlias = session.ContextAlias;
        var mvid = session.ContextManager.Mvid;
        if (mvid == null)
            return;

        if (_mvidByAlias.TryGetValue(contextAlias, out var previousMvid)
            && !string.Equals(previousMvid, mvid, StringComparison.OrdinalIgnoreCase))
        {
            RemoveAliasRoutingLocked(contextAlias);
        }

        _mvidByAlias[contextAlias] = mvid;
        if (!_aliasByMvid.ContainsKey(mvid))
            _aliasByMvid[mvid] = contextAlias;
    }

    private void RemoveAliasRoutingLocked(string contextAlias)
    {
        if (!_mvidByAlias.Remove(contextAlias, out var mvid))
            return;

        if (!_aliasByMvid.TryGetValue(mvid, out var mappedAlias)
            || !string.Equals(mappedAlias, contextAlias, StringComparison.OrdinalIgnoreCase))
            return;

        _aliasByMvid.Remove(mvid);
        var replacementAlias = _mvidByAlias
            .Where(pair => string.Equals(pair.Value, mvid, StringComparison.OrdinalIgnoreCase))
            .Select(pair => pair.Key)
            .OrderBy(alias => string.Equals(alias, CurrentContextAlias, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(alias => alias, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (replacementAlias != null)
            _aliasByMvid[mvid] = replacementAlias;
    }

    private sealed class LoadedSessionState(DecompilerSession session, long lastUsedSequence)
    {
        public DecompilerSession Session { get; } = session;
        public long LastUsedSequence { get; set; } = lastUsedSequence;
        public int ActiveLeaseCount { get; set; }
    }

    private sealed record DisplacedSessionState(
        string ContextAlias,
        WorkspaceLoadRequest LoadRequest,
        DecompilerSettings Settings,
        long LastUsedSequence,
        bool WasLruEviction);
}

public sealed class DecompilerSessionLease : IDisposable
{
    private DecompilerWorkspace? _workspace;

    internal DecompilerSessionLease(DecompilerWorkspace workspace, DecompilerSession session)
    {
        _workspace = workspace;
        Session = session;
    }

    public DecompilerSession Session { get; }

    public void Dispose()
    {
        Interlocked.Exchange(ref _workspace, null)?.ReleaseSession(Session);
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

        try
        {
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
        catch
        {
            contextManager.Dispose();
            throw;
        }
    }

    public WorkspaceContextInfo ToContextInfo(bool isCurrent)
    {
        return new WorkspaceContextInfo
        {
            ContextAlias = ContextAlias,
            Mvid = ContextManager.Mvid!,
            AssemblyPath = ContextManager.AssemblyPath!,
            LoadedAtUnix = ToUnixTimeSeconds(ContextManager.LoadedAtUtc),
            TypeCount = ContextManager.TypeCount,
            MethodCount = ContextManager.GetAllTypes().Sum(type => type.Methods.Count()),
            NamespaceCount = ContextManager.NamespaceCount,
            IsCurrent = isCurrent
        };
    }

    private static long? ToUnixTimeSeconds(DateTime? utc)
    {
        if (!utc.HasValue)
            return null;

        var value = DateTime.SpecifyKind(utc.Value, DateTimeKind.Utc);
        return new DateTimeOffset(value).ToUnixTimeSeconds();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        try
        {
            DecompilerService.Dispose();
            MemberResolver.ClearCache();
            UsageAnalyzer.ClearCache();
        }
        finally
        {
            ContextManager.Dispose();
            _disposed = true;
        }
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

public sealed record WorkspaceMemoryStats
{
    public int MaxLoadedContexts { get; init; }
    public int LoadedContexts { get; init; }
    public int ActiveLeases { get; init; }
    public long Evictions { get; init; }
    public long Reloads { get; init; }
    public required string[] LeasedAliases { get; init; }
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
