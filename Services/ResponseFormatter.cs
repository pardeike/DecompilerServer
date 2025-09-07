using System.Text.Json;

namespace DecompilerServer.Services;

/// <summary>
/// Provides standardized JSON response formatting for all MCP server endpoints.
/// Ensures consistent response structure and error handling.
/// </summary>
public class ResponseFormatter
{
    private static readonly JsonSerializerOptions DefaultOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    /// <summary>
    /// Format a successful response with data
    /// </summary>
    public static string Success<T>(T data)
    {
        var response = new
        {
            status = "ok",
            data = data
        };

        return JsonSerializer.Serialize(response, DefaultOptions);
    }

    /// <summary>
    /// Format a successful response with no data
    /// </summary>
    public static string Success()
    {
        var response = new
        {
            status = "ok"
        };

        return JsonSerializer.Serialize(response, DefaultOptions);
    }

    /// <summary>
    /// Format an error response
    /// </summary>
    public static string Error(string message, string? details = null)
    {
        var response = new
        {
            status = "error",
            message = message,
            details = details
        };

        return JsonSerializer.Serialize(response, DefaultOptions);
    }

    /// <summary>
    /// Format a search result with pagination
    /// </summary>
    public static string SearchResult<T>(SearchResult<T> result)
    {
        var response = new
        {
            status = "ok",
            items = result.Items,
            hasMore = result.HasMore,
            nextCursor = result.NextCursor,
            totalEstimate = result.TotalEstimate
        };

        return JsonSerializer.Serialize(response, DefaultOptions);
    }

    /// <summary>
    /// Format server status response
    /// </summary>
    public static string Status(ServerStatus status)
    {
        return JsonSerializer.Serialize(status, DefaultOptions);
    }

    /// <summary>
    /// Format assembly load response
    /// </summary>
    public static string AssemblyLoaded(AssemblyInfo info)
    {
        var response = new
        {
            status = "ok",
            mvid = info.Mvid,
            assemblyPath = info.AssemblyPath,
            types = info.TypeCount,
            methods = info.MethodCount,
            namespaces = info.NamespaceCount,
            warmed = info.Warmed
        };

        return JsonSerializer.Serialize(response, DefaultOptions);
    }

    /// <summary>
    /// Format source document response
    /// </summary>
    public static string SourceDocument(SourceDocument document)
    {
        var response = new
        {
            memberId = document.MemberId,
            language = document.Language,
            totalLines = document.TotalLines,
            hash = document.Hash,
            includeHeader = document.IncludeHeader
        };

        return JsonSerializer.Serialize(response, DefaultOptions);
    }

    /// <summary>
    /// Format source slice response
    /// </summary>
    public static string SourceSlice(SourceSlice slice)
    {
        var response = new
        {
            memberId = slice.MemberId,
            language = slice.Language,
            startLine = slice.StartLine,
            endLine = slice.EndLine,
            totalLines = slice.TotalLines,
            hash = slice.Hash,
            code = slice.Code
        };

        return JsonSerializer.Serialize(response, DefaultOptions);
    }

    /// <summary>
    /// Format member details response
    /// </summary>
    public static string MemberDetails(MemberDetails details)
    {
        return JsonSerializer.Serialize(details, DefaultOptions);
    }

    /// <summary>
    /// Format batch response for multiple operations
    /// </summary>
    public static string BatchResponse<T>(IEnumerable<T> items)
    {
        var response = new
        {
            status = "ok",
            items = items,
            count = items.Count()
        };

        return JsonSerializer.Serialize(response, DefaultOptions);
    }

    /// <summary>
    /// Format statistics response
    /// </summary>
    public static string Statistics(object stats)
    {
        var response = new
        {
            status = "ok",
            stats = stats
        };

        return JsonSerializer.Serialize(response, DefaultOptions);
    }

    /// <summary>
    /// Format namespace list response
    /// </summary>
    public static string NamespaceList(IEnumerable<string> namespaces)
    {
        var response = new
        {
            status = "ok",
            namespaces = namespaces.OrderBy(ns => ns),
            count = namespaces.Count()
        };

        return JsonSerializer.Serialize(response, DefaultOptions);
    }

    /// <summary>
    /// Format generated code response
    /// </summary>
    public static string GeneratedCode(GeneratedCodeResult result)
    {
        var response = new
        {
            status = "ok",
            target = result.Target,
            code = result.Code,
            notes = result.Notes
        };

        return JsonSerializer.Serialize(response, DefaultOptions);
    }

    /// <summary>
    /// Try to execute an operation and return formatted response
    /// </summary>
    public static string TryExecute<T>(Func<T> operation)
    {
        try
        {
            var result = operation();
            return Success(result);
        }
        catch (ArgumentException ex)
        {
            return Error("Invalid argument", ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return Error("Invalid operation", ex.Message);
        }
        catch (FileNotFoundException ex)
        {
            return Error("File not found", ex.Message);
        }
        catch (NotSupportedException ex)
        {
            return Error("Not supported", ex.Message);
        }
        catch (Exception ex)
        {
            return Error("Internal error", ex.Message);
        }
    }

    /// <summary>
    /// Try to execute an operation that returns void and return formatted response
    /// </summary>
    public static string TryExecute(Action operation)
    {
        try
        {
            operation();
            return Success();
        }
        catch (ArgumentException ex)
        {
            return Error("Invalid argument", ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return Error("Invalid operation", ex.Message);
        }
        catch (FileNotFoundException ex)
        {
            return Error("File not found", ex.Message);
        }
        catch (NotSupportedException ex)
        {
            return Error("Not supported", ex.Message);
        }
        catch (Exception ex)
        {
            return Error("Internal error", ex.Message);
        }
    }
}

/// <summary>
/// Server status information
/// </summary>
public class ServerStatus
{
    public bool Loaded { get; init; }
    public string? Mvid { get; init; }
    public string? AssemblyPath { get; init; }
    public long? StartedAtUnix { get; init; }
    public object? Settings { get; init; }
    public object? Stats { get; init; }
    public IndexStatus? Indexes { get; init; }
}

/// <summary>
/// Index status information
/// </summary>
public class IndexStatus
{
    public int Namespaces { get; init; }
    public int Types { get; init; }
    public bool NameIndexReady { get; init; }
    public bool StringLiteralIndexReady { get; init; }
}

/// <summary>
/// Assembly information
/// </summary>
public class AssemblyInfo
{
    public required string Mvid { get; init; }
    public required string AssemblyPath { get; init; }
    public int TypeCount { get; init; }
    public int MethodCount { get; init; }
    public int NamespaceCount { get; init; }
    public bool Warmed { get; init; }
}

/// <summary>
/// Member details with full metadata
/// </summary>
public class MemberDetails
{
    public required string MemberId { get; init; }
    public required string Name { get; init; }
    public required string FullName { get; init; }
    public required string Kind { get; init; }
    public required string Signature { get; init; }
    public string? DeclaringType { get; init; }
    public string? Namespace { get; init; }
    public string? Accessibility { get; init; }
    public bool IsStatic { get; init; }
    public bool IsAbstract { get; init; }
    public bool IsVirtual { get; init; }
    public List<AttributeInfo>? Attributes { get; init; }
    public string? XmlDoc { get; init; }
    public string? BaseDefinitionId { get; init; }
    public List<string>? OverrideIds { get; init; }
    public List<string>? ImplementorIds { get; init; }
}

/// <summary>
/// Attribute information
/// </summary>
public class AttributeInfo
{
    public required string FullName { get; init; }
    public List<object>? ConstructorArgs { get; init; }
}

/// <summary>
/// Generated code result
/// </summary>
public class GeneratedCodeResult
{
    public required MemberSummary Target { get; init; }
    public required string Code { get; init; }
    public List<string>? Notes { get; init; }
}