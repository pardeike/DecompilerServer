# DecompilerServer Copilot Instructions

## Project Overview

This is a **DecompilerServer** project that implements an MCP (Model Context Protocol) server for decompiling and analyzing .NET assemblies, specifically focused on Unity's Assembly-CSharp.dll files. The server provides comprehensive decompilation, search, and analysis capabilities through various endpoints.

## Key Technologies & Frameworks

- **.NET 8.0** - Target framework
- **ICSharpCode.Decompiler** - Core decompilation engine 
- **Microsoft.Extensions.Hosting** - Hosting and dependency injection
- **ModelContextProtocol** - MCP server implementation
- **System.Text.Json** - JSON serialization with camelCase naming
- **xUnit** - Testing framework

## Project Structure

```
DecompilerServer/
├── Services/              # Core service implementations
│   ├── AssemblyContextManager.cs   # Assembly loading and context management
│   ├── DecompilerService.cs       # C# decompilation with caching
│   ├── MemberResolver.cs          # Member ID resolution and normalization
│   ├── SearchServiceBase.cs       # Base search and pagination functionality
│   ├── UsageAnalyzer.cs           # Code usage analysis
│   ├── InheritanceAnalyzer.cs     # Inheritance relationship analysis
│   └── ResponseFormatter.cs       # JSON response formatting
├── Tools/                 # MCP tool implementations (static methods)
│   ├── ResolveMemberId.cs         # Member ID validation
│   ├── ListNamespaces.cs          # Namespace enumeration
│   ├── SearchTypes.cs             # Type discovery with search
│   ├── GetDecompiledSource.cs     # Core decompilation to C#
│   ├── GetSourceSlice.cs          # Source code range viewing
│   └── GetMemberDetails.cs        # Rich member metadata
├── DecompilerServer.Tests/        # xUnit test suite
├── TestLibrary/                   # Test assembly for validation
├── ServiceLocator.cs              # Service locator for MCP tools
├── Program.cs                     # Application entry point
└── *.md                          # Documentation files
```

## Documentation

### Developer Guides
- **[HELPER_METHODS_GUIDE.md](../HELPER_METHODS_GUIDE.md)** - Comprehensive guide to all service helper methods and implementation patterns for MCP tools. Essential reading for understanding the service architecture and implementing new endpoints.
- **[TESTING.md](../TESTING.md)** - Complete testing framework documentation covering xUnit setup, test data structure, and testing patterns. Required reading for writing tests and understanding the test infrastructure.

Both guides are actively maintained and provide crucial implementation guidance for developers working on the DecompilerServer.

## Core Architecture Principles

### Member ID System
- **Format**: `<mvid-32hex>:<token-8hex>:<kind-code>`
- **Kind Codes**: T=Type, M=Method/Constructor, P=Property, F=Field, E=Event, N=Namespace
- All member IDs must be stable and consistent across sessions

### Threading & Performance
- Use `ReaderWriterLockSlim` for thread-safe access (fast reads, guarded writes)
- Implement lazy indexing - build minimal maps on first access
- Cache decompiled source with line indexing for efficient slicing
- Always paginate results (default limit: 50, max: 500)

### Error Handling
- Return structured errors: `{ error: { code, message, detail? } }`
- Never throw exceptions across MCP boundaries
- Handle assembly loading failures gracefully

## Code Style & Conventions

### Formatting
- **Important**: Run `dotnet format DecompilerServer.sln` before committing
- During development, focus on functionality over formatting
- The formatter will handle code style consistency

### Naming Conventions
- Use PascalCase for public members and types
- Use camelCase for private fields and local variables
- Prefix private fields with underscore (`_fieldName`)
- Use descriptive names for member IDs and handles

### JSON Serialization
- Use camelCase property naming
- Ignore null values in output
- Ensure deterministic ordering for stable diffs

## Development Workflow

### Building & Testing
```bash
# Build the solution
dotnet build DecompilerServer.sln

# Run tests
dotnet test DecompilerServer.sln

# Format code before committing
dotnet format DecompilerServer.sln
```

### Test Strategy
- All service helpers have comprehensive tests using real test assembly
- Use `ServiceTestBase` for integration tests with loaded test.dll
- Validate both functionality and output format
- Test with various C# constructs (generics, inheritance, attributes, etc.)

## Key Implementation Guidelines

### Assembly Context Management
- Maintain single in-memory context with PEFile, UniversalAssemblyResolver, DecompilerTypeSystem
- Configure resolver with appropriate search directories (Game*_Data/Managed, Unity directories)
- Use proper decompiler settings (UsingDeclarations, ShowXmlDocumentation, NamedArguments)

### Search & Pagination
- Implement cursor-based pagination for all search endpoints
- Support filtering by accessibility, member types, namespaces
- Return structured results with `{ items, nextCursor, totalEstimate }`

### Source Management
- Cache decompiled C# source with line indexing
- Support ranged retrieval without re-decompilation
- Include proper headers/footers and maintain source document metadata

### IL Handling
- Use ICSharpCode.Decompiler.Disassembler for readable IL output
- Leverage MetadataReader, avoid reflection-only loading
- Provide both IL and decompiled C# views

## Common Patterns

**See [HELPER_METHODS_GUIDE.md](../HELPER_METHODS_GUIDE.md) for comprehensive service helper documentation and implementation patterns.**

### Service Dependencies
Services typically depend on:
- `AssemblyContextManager` - for assembly access
- `MemberResolver` - for ID resolution and validation
- Other services as needed for specific functionality

### MCP Tool Dependencies
MCP tools (static methods) access services via:
- `ServiceLocator.ContextManager` - assembly context access
- `ServiceLocator.MemberResolver` - member ID operations
- `ServiceLocator.DecompilerService` - source decompilation
- `ServiceLocator.ResponseFormatter` - consistent JSON responses

### Response Models
Use consistent model types (defined in shared Models.cs):
- `MemberHandle`, `MemberSummary`, `SearchResult<T>`
- `MemberDetails`, `SourceDocument`, `SourceSlice`
- `UsageRef`, `GraphResult`, `Stats`

### Error Responses
Always return structured error objects rather than throwing exceptions when implementing MCP endpoints.

## Testing Considerations

- Use the TestLibrary project for consistent test data
- Test with various C# language features and constructs
- Validate both service functionality and JSON output format
- Ensure thread safety in concurrent scenarios
- Test pagination and cursor handling

**See [TESTING.md](../TESTING.md) for complete testing framework documentation and best practices.**

### MCP Tool Testing Patterns
When implementing MCP tools (static methods), follow these patterns:

- **ServiceLocator Setup**: Create a service provider in test constructor and register all required services:
  ```csharp
  public ToolImplementationTests()
  {
      var services = new ServiceCollection();
      services.AddSingleton(ContextManager);
      services.AddSingleton(MemberResolver);
      services.AddSingleton<DecompilerService>();
      services.AddSingleton<UsageAnalyzer>();
      services.AddSingleton<InheritanceAnalyzer>();
      services.AddSingleton<ResponseFormatter>();
      
      _serviceProvider = services.BuildServiceProvider();
      ServiceLocator.SetServiceProvider(_serviceProvider);
  }
  ```

- **Service Dependencies**: Always register services in the correct dependency order:
  1. `AssemblyContextManager` (core, no dependencies)
  2. `MemberResolver` (depends on AssemblyContextManager)
  3. All other services (DecompilerService, UsageAnalyzer, etc.)
  4. `ResponseFormatter` (standalone)

- **Test Cleanup**: Do NOT override Dispose() with [Fact] attribute - this causes xUnit errors. Let ServiceTestBase handle disposal naturally.

- **SearchService Pattern**: When tools need SearchServiceBase functionality, create a concrete implementation:
  ```csharp
  internal class SearchService : SearchServiceBase
  {
      public SearchService(AssemblyContextManager contextManager, MemberResolver memberResolver)
          : base(contextManager, memberResolver) { }
  }
  ```

## Performance Optimization

- Implement lazy loading for expensive operations
- Use concurrent collections for thread-safe caching
- Build indexes incrementally rather than upfront
- Consider memory usage when caching large decompiled sources

## MCP Tool Implementation Guidelines

### Static Method Pattern
MCP tools must be implemented as static methods with specific attributes:
```csharp
[McpServerTool, Description("Tool description")]
public static string ToolName(parameters...)
{
    return ResponseFormatter.TryExecute(() =>
    {
        // Access services via ServiceLocator
        var service = ServiceLocator.GetRequiredService<ServiceType>();
        // Implementation logic
        return result;
    });
}
```

### ServiceLocator Usage
- Use ServiceLocator pattern to provide dependency injection for static MCP tools
- Always check if assembly is loaded before performing operations
- Use ResponseFormatter.TryExecute() for consistent error handling across all tools

## Final Development Step

**Before completing any major work or making the final commit, always review and update `.github/copilot-instructions.md` if:**
- You encountered unexpected patterns or pitfalls during development
- New architectural patterns or testing approaches were discovered
- Additional framework-specific guidance would help future development
- The current instructions have become outdated or incomplete

This ensures knowledge from each development cycle is captured for improved efficiency in future work.