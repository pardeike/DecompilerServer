# DecompilerServer

A Model Context Protocol (MCP) server for decompiling and analyzing .NET assemblies. DecompilerServer works with any managed assembly (`.dll` or `.exe`) and includes a Unity-oriented `gameDir` workflow for locating `Assembly-CSharp.dll` under a game install.

## ✨ Features

- **🔍 Broad MCP surface**: 38 MCP tools for decompilation, search, relationship analysis, cache management, and code generation
- **⚡ Fast iteration**: Cached decompiled documents, line-precise slicing, and optional index warming
- **🛠️ General .NET support**: Load assemblies directly via `assemblyPath` or let the server resolve Unity layouts from `gameDir`
- **🎮 Unity-friendly workflows**: Convenience loading for `Assembly-CSharp.dll` plus modding-oriented code generation helpers
- **📊 Search and graph analysis**: Search types, members, attributes, and string literals; inspect usages, callers, callees, base/derived types, overrides, overloads, and implementations
- **🧬 Stable automation**: Member IDs remain stable per assembly MVID for repeatable MCP-driven workflows

## 🚀 Quick Start

### Prerequisites

- .NET 10 runtime to run release downloads or framework-dependent published output
- .NET 10 SDK only if you want to build from source (`DecompilerServer.csproj` targets `net10.0`)
- Windows, macOS, or Linux

### Installation

1. **Download a release asset** from the latest GitHub release
   - macOS Apple Silicon: `decompilerserver-vX.Y.Z-osx-arm64.tar.gz`
   - macOS Intel: `decompilerserver-vX.Y.Z-osx-x64.tar.gz`
   - Linux x64: `decompilerserver-vX.Y.Z-linux-x64.tar.gz`
   - Linux arm64: `decompilerserver-vX.Y.Z-linux-arm64.tar.gz`
   - Windows x64: `decompilerserver-vX.Y.Z-win-x64.zip`

2. **Extract the archive** and point your MCP client at the packaged executable
   - macOS/Linux: `DecompilerServer`
   - Windows: `DecompilerServer.exe`

3. **Install the .NET 10 runtime first** if it is not already available on the target machine

4. **Or build from source** if you prefer
   ```bash
   git clone https://github.com/pardeike/DecompilerServer.git
   cd DecompilerServer
   dotnet build DecompilerServer.sln -c Release
   ```

5. **Run tests** (optional)
   ```bash
   dotnet test -c Release
   ```

> **💡 Tip**: See [🤖 MCP Client Integration](#-mcp-client-integration) for the launch command you will wire into your MCP client.

## 🤖 MCP Client Integration

DecompilerServer is a stdio MCP server. The stable part across clients is the process you launch.

**Framework-dependent build output**
```text
command: dotnet
args:
  - /absolute/path/to/DecompilerServer/bin/Release/net10.0/DecompilerServer.dll
```

**Release download or RID-specific publish output**
- macOS/Linux: `/absolute/path/to/DecompilerServer`
- Windows: `C:\\absolute\\path\\to\\DecompilerServer.exe`

**Native app host produced by `dotnet build` or local `dotnet publish`**
- macOS/Linux: `/absolute/path/to/DecompilerServer/bin/Release/net10.0/DecompilerServer`
- Windows: `C:\\absolute\\path\\to\\DecompilerServer\\bin\\Release\\net10.0\\DecompilerServer.exe`

Release assets are framework-dependent single-file bundles. They are easier to install than a source build, but they still require the .NET 10 runtime on the target machine.

Use whichever launch style your MCP client supports. If you publish the project locally, point your client at the published executable or published DLL instead of the `bin/Release/net10.0` build output shown above.

### Basic Usage

Most MCP clients expose these tools in `snake_case` even though the underlying C# methods are PascalCase. The examples below show the tool name and arguments that the server expects; your client may wrap them in its own request envelope.

1. **Start the server locally**
   ```bash
   dotnet run --project DecompilerServer.csproj -c Release
   ```

2. **Load an assembly directly**
   ```text
   load_assembly({
     "assemblyPath": "/path/to/YourAssembly.dll",
     "rebuildIndex": true
   })
   ```

   **Or load a Unity game directory**
   ```text
   load_assembly({
     "gameDir": "/path/to/unity/game",
     "assemblyFile": "Assembly-CSharp.dll",
     "rebuildIndex": true
   })
   ```

3. **List namespaces**
   ```text
   list_namespaces({})
   ```

4. **Search for types**
   ```text
   search_types({
     "query": "Player",
     "limit": 10,
     "mode": "discovery"
   })
   ```

5. **Decompile a discovered member**
   ```text
   get_decompiled_source({
     "memberId": "<member-id-from-search>"
   })
   ```

## 🏗️ Architecture

DecompilerServer uses a small singleton service graph and auto-discovers MCP tools from the assembly at startup.

### Core Services
- **AssemblyContextManager**: Assembly loading, indexing, locking, and context lifetime
- **MemberResolver**: Stable member ID generation, normalization, resolution, and caching
- **DecompilerService**: C# decompilation plus cached document retrieval
- **SearchServiceBase**: Shared filtering, pagination, and cached search helpers
- **UsageAnalyzer**: Usage analysis, caller/callee traversal, and string-literal search support
- **InheritanceAnalyzer**: Base/derived type analysis, overrides, and interface implementation discovery
- **ResponseFormatter**: Consistent camelCase JSON serialization and error handling

### MCP Tool Surface (38 tools)
- **Core/server**: `ping`, `status`, `get_server_stats`, `load_assembly`, `unload`, `warm_index`, `clear_caches`, `set_decompile_settings`
- **Discovery/search**: `list_namespaces`, `get_types_in_namespace`, `search_types`, `search_members`, `search_attributes`, `search_string_literals`, `get_members_of_type`
- **Member inspection/source**: `resolve_member_id`, `normalize_member_id`, `get_member_signature`, `get_member_details`, `get_decompiled_source`, `get_source_slice`, `get_xml_doc`, `get_il`, `get_ast_outline`, `batch_get_decompiled_source`, `plan_chunking`
- **Relationship analysis**: `find_usages`, `find_callers`, `find_callees`, `find_base_types`, `find_derived_types`, `get_overrides`, `get_overloads`, `get_implementations`
- **Code generation/modding**: `generate_harmony_patch_skeleton`, `generate_detour_stub`, `generate_extension_method_wrapper`, `suggest_transpiler_targets`

> **Current behavior note**: `get_il` currently accepts only `{ "format": "IL" }`. The implementation does not yet support `ILAst`.

### Member ID System

All members use a stable ID format: `<mvid-32hex>:<token-8hex>:<kind-code>`
- **Kind codes**: `T` = Type, `M` = Method/Constructor, `P` = Property, `F` = Field, `E` = Event, `N` = Namespace
- IDs remain stable for a given assembly MVID, which makes automated follow-up calls reliable

## 📖 Examples

### Analyze any .NET assembly

```text
load_assembly({
  "assemblyPath": "/path/to/MyLibrary.dll"
})

search_types({
  "query": "Simple",
  "limit": 10,
  "mode": "discovery"
})

get_member_details({
  "memberId": "abc123...def:12345678:T"
})
```

### Analyze a Unity assembly

```text
load_assembly({
  "gameDir": "/path/to/Game",
  "assemblyFile": "Assembly-CSharp.dll"
})

search_members({
  "query": "Player",
  "kind": "method",
  "limit": 10,
  "mode": "signatures"
})

generate_harmony_patch_skeleton({
  "memberId": "abc123...def:87654321:M",
  "patchKinds": "Prefix,Postfix",
  "includeReflectionTargeting": true
})
```

### Batch analysis workflow

```text
search_string_literals({
  "pattern": "PlayerDied",
  "limit": 20
})

batch_get_decompiled_source({
  "memberIds": ["id1", "id2", "id3"]
})

find_usages({
  "memberId": "target-member-id",
  "limit": 25
})
```

### Result modes for high-churn discovery tools

`search_types`, `search_members`, and `get_members_of_type` support output modes so callers can trade detail for context size:

- `ids`: minimal chaining payload (`memberId`, `name`, `kind`)
- `discovery`: candidate selection payload for follow-up inspection
- `signatures`: callable/member surface focused payload
- `full`: legacy rich summary payload

Defaults are tuned for common workflows:

- `search_types`: `discovery`
- `search_members`: `discovery`
- `get_members_of_type`: `signatures`

## 🔧 Development

### Building

```bash
# Build entire solution
dotnet build DecompilerServer.sln -c Release

# Build specific project
dotnet build DecompilerServer.csproj -c Release
```

### Testing

```bash
# Run all tests
dotnet test -c Release

# Run with verbose output
dotnet test -c Release --verbosity normal

# Run specific test class
dotnet test -c Release --filter "ClassName=CoreToolTests"
```

### Code Formatting

```bash
# Format code before committing
dotnet format DecompilerServer.sln
```

### Project Structure

```text
DecompilerServer/
├── Services/           # Core service implementations (7 services)
├── Tools/              # MCP tool implementations (38 tools)
├── Tests/              # Comprehensive xUnit test suite
├── TestLibrary/        # Test assembly for validation
├── Program.cs          # Application entry point
├── ServiceLocator.cs   # Service locator for MCP tools
└── *.md                # Documentation files
```

## 📚 Documentation

- **[HELPER_METHODS_GUIDE.md](HELPER_METHODS_GUIDE.md)** - Comprehensive guide to service helpers and implementation patterns
- **[TESTING.md](TESTING.md)** - Complete testing framework documentation and best practices
- **[TODO.md](TODO.md)** - Prioritized enhancement opportunities and development roadmap
- **[.github/copilot-instructions.md](.github/copilot-instructions.md)** - Detailed project architecture and AI development guidelines

## 🤝 Contributing

We welcome contributions. Please use the project documentation and existing tool patterns as the baseline:

1. **Read the documentation**: Start with [HELPER_METHODS_GUIDE.md](HELPER_METHODS_GUIDE.md) and [TESTING.md](TESTING.md)
2. **Check the roadmap**: Review [TODO.md](TODO.md) for priority items
3. **Follow established patterns**: Match the existing service and tool structure
4. **Test thoroughly**: Use the xUnit suite before submitting changes
5. **Format code**: Run `dotnet format` before committing

### Development Workflow

1. Fork the repository
2. Create a feature branch
3. Make your changes following existing patterns
4. Add tests for new functionality
5. Run `dotnet test -c Release` to ensure the suite passes
6. Run `dotnet format` to maintain code style
7. Submit a pull request with a clear description

## 🛡️ Thread Safety & Performance

DecompilerServer is designed for concurrent, tool-driven analysis:

- **Thread-safe assembly access**: `AssemblyContextManager` uses `ReaderWriterLockSlim` around load/read/update paths
- **Caching**: Decompiled source, member resolution, and search results are cached to reduce repeated work
- **Lazy and opt-in indexing**: Basic indexes build on demand, and `warm_index` can precompute additional data
- **Cursor-based pagination**: Search-style tools page results with tool-specific defaults and limits

## 🔌 MCP Integration

DecompilerServer implements MCP for seamless integration with AI and developer tooling:

- **Auto-discovery**: Tools are registered via `[McpServerTool]` attributes and loaded from the assembly
- **Consistent payload conventions**: Responses are camelCase JSON strings with shared success/error formatting
- **Structured errors**: Tool failures return machine-readable error payloads
- **Typed parameters**: Tool signatures map directly to strongly typed C# parameters

See [🤖 MCP Client Integration](#-mcp-client-integration) for launch guidance.

## 📋 System Requirements

- **Build from source**: .NET 10 SDK
- **Run framework-dependent output**: .NET 10 runtime
- **Memory**: Recommended 4 GB+ for large assemblies
- **Storage**: Varies by assembly size and cache growth
- **Platform**: Windows, macOS, or Linux

## 📜 License

This project is open source. See [LICENSE](LICENSE) for the current license text.

## 🙏 Acknowledgments

Built with:
- **[ICSharpCode.Decompiler](https://github.com/icsharpcode/ILSpy)** - Core decompilation engine
- **[ModelContextProtocol](https://github.com/microsoft/model-context-protocol)** - MCP server framework
- **Microsoft.Extensions.Hosting** - Application hosting and dependency injection

---

For deeper implementation details and testing guidance, see the additional markdown guides in the repository.
