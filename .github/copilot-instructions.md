# Copilot Instructions for DecompilerServer

## Project Overview

DecompilerServer is a Model Context Protocol (MCP) server for exploring and analyzing Unity Assembly-CSharp.dll files using ICSharpCode.Decompiler. It provides a comprehensive API for decompiling, searching, and analyzing game assemblies with focus on modding support (Harmony patches, detours, extension methods).

## Architecture

### Core Technologies
- **Framework**: .NET 8.0 C# console application
- **MCP Integration**: ModelContextProtocol NuGet package with STDIO transport
- **Decompilation**: ICSharpCode.Decompiler 9.1.0+
- **Hosting**: Microsoft.Extensions.Hosting for DI and lifecycle management

### Key Components

1. **Helper Services** (`/Services/`):
   - `AssemblyContextManager` - Manages loaded assembly context, PEFile, TypeSystem, CSharpDecompiler
   - `MemberResolver` - Central service for resolving member IDs to IEntity objects
   - `DecompilerService` - Handles C# decompilation with caching and source document management
   - `SearchServiceBase` - Base class for search and pagination functionality
   - `UsageAnalyzer` - Analyzes IL code for usage patterns, callers, and callees
   - `InheritanceAnalyzer` - Analyzes type inheritance relationships and interface implementations
   - `ResponseFormatter` - Provides standardized JSON response formatting

2. **MCP Tools** (`/Tools/`): 38+ endpoint implementations for assembly exploration
   - Assembly management: `LoadAssembly`, `Unload`, `Status`
   - Search: `SearchTypes`, `SearchMembers`, `SearchStringLiterals`
   - Decompilation: `GetDecompiledSource`, `BatchGetDecompiledSource`, `GetSourceSlice`
   - Analysis: `FindUsages`, `FindCallers`, `FindCallees`, `GetMemberDetails`
   - Code generation: `GenerateHarmonyPatchSkeleton`, `GenerateDetourStub`

## Core Constraints & Patterns

### Assembly Context
- **Single in-memory context**: PEFile, UniversalAssemblyResolver, DecompilerTypeSystem, CSharpDecompiler
- **Thread-safety**: ReaderWriterLockSlim for fast reads, guarded writes
- **Search paths**: Game directory + Game*_Data/Managed + MonoBleedingEdge/lib/mono

### Member ID Format
Stable IDs: `<mvid-32hex>:<token-8hex>:<kind-code>`
- Kind codes: T=Type, M=Method/Ctor, P=Property, F=Field, E=Event, N=Namespace
- Built from IEntity.MetadataToken and PEFile.Metadata with module MVID

### Pagination & Serialization
- **Always paginate**: limit (default 50, max 500), cursor string
- **Output format**: `{ items, nextCursor }`
- **JSON**: System.Text.Json with camelCase, ignore nulls, deterministic ordering

### Performance
- **Lazy indexes**: Build minimal maps on first access, deep indexes behind WarmIndex
- **Source cache**: Per-member decompiled C# with line index for ranged retrieval
- **Error handling**: Return `{ error: { code, message, detail? } }`, never throw across MCP boundary

## Development Guidelines

### Code Style & Formatting
- **During development**: Whitespace and formatting are not super important - focus on functionality
- **Before committing**: Always run `dotnet format DecompilerServer.sln` to reformat everything correctly
- **Consistency**: Follow existing patterns in helper services and endpoint implementations

### Implementation Patterns

#### Basic Endpoint
```csharp
public static string MyEndpoint()
{
    return ResponseFormatter.TryExecute(() => {
        if (!contextManager.IsLoaded)
            throw new InvalidOperationException("No assembly loaded");
        
        var data = GetMyData();
        return data;
    });
}
```

#### Member Resolution Endpoint
```csharp
public static string MyEndpoint(string memberId)
{
    return ResponseFormatter.TryExecute(() => {
        var member = memberResolver.ResolveMember(memberId);
        if (member == null)
            throw new ArgumentException($"Cannot resolve member: {memberId}");
        
        var result = ProcessMember(member);
        return result;
    });
}
```

#### Search Endpoint
```csharp
public static string MySearchEndpoint(string query, bool regex, int limit, string cursor)
{
    return ResponseFormatter.TryExecute(() => {
        var results = searchService.SearchSomething(query, regex, limit, cursor);
        return ResponseFormatter.SearchResult(results);
    });
}
```

### Recommended Data Models
```csharp
record MemberHandle(string Id, string Kind, string Name, string DeclaringType, string Namespace);
enum MemberKind { Namespace, Type, Method, Property, Field, Event, Constructor }
record MemberSummary(MemberHandle Handle, string Signature, string Accessibility, bool Static, bool Virtual, bool Abstract, int GenericArity, string? DocSummary);
record SearchResult<T>(IReadOnlyList<T> Items, string? NextCursor, int TotalEstimate);
record SourceDocument(string MemberId, string Language, int TotalLines, string Hash, string? Header, string? Footer);
record SourceSlice(string MemberId, int StartLine, int EndLine, bool LineNumbers, string Text);
```

### Search Implementation
- **Default**: Case-insensitive substring matching
- **Optional**: Regex support, qualified search ("Verse.Pawn:Tick")
- **Filters**: Kind, namespace, declaring type, attribute, return type, accessibility, static/virtual/abstract

### Safety & Security
- **Path validation**: Never load untrusted paths, only within configured game root + allow-list
- **Error boundaries**: All MCP endpoints wrapped in ResponseFormatter.TryExecute
- **Resource management**: Proper disposal of PEFile and related resources

## Testing & Quality

### Running Tests
```bash
dotnet run --test          # Run basic helper service tests
dotnet build               # Build entire solution
dotnet format DecompilerServer.sln  # Format before commit
```

### Key Documentation
- `HELPER_METHODS_GUIDE.md` - Comprehensive guide to helper services and implementation patterns
- `CommonImplementorGuide.txt` - Core constraints and implementation tips for MCP endpoints

## MCP Tool Attributes
All endpoint tools should use `[McpServerTool]` attribute for auto-discovery:
```csharp
[McpServerTool("tool_name", "Description of what this tool does")]
public static string ToolMethod(parameters...) => ResponseFormatter.TryExecute(() => { ... });
```

## Common Tasks

### Adding New Endpoint
1. Create new file in `/Tools/` following naming convention
2. Add `[McpServerTool]` attribute with clear description
3. Use appropriate helper services from `/Services/`
4. Follow one of the standard implementation patterns
5. Ensure proper error handling with ResponseFormatter.TryExecute
6. Add pagination if returning collections
7. Run `dotnet format` before committing

### Extending Helper Services
1. Add methods to existing services in `/Services/`
2. Maintain thread-safety with existing locking patterns
3. Use lazy loading and caching where appropriate
4. Follow established naming conventions and patterns
5. Update `HELPER_METHODS_GUIDE.md` if adding significant functionality

### Performance Optimization
1. Use WarmIndex for expensive operations
2. Implement appropriate caching in helper services
3. Consider lazy evaluation for expensive computations
4. Profile with large assemblies to identify bottlenecks