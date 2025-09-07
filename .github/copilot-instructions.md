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
├── DecompilerServer.Tests/        # xUnit test suite
├── TestLibrary/                   # Test assembly for validation
├── Program.cs                     # Application entry point
└── *.md                          # Documentation files
```

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

### Service Dependencies
Services typically depend on:
- `AssemblyContextManager` - for assembly access
- `MemberResolver` - for ID resolution and validation
- Other services as needed for specific functionality

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

## Performance Optimization

- Implement lazy loading for expensive operations
- Use concurrent collections for thread-safe caching
- Build indexes incrementally rather than upfront
- Consider memory usage when caching large decompiled sources