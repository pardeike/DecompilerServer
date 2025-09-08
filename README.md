# DecompilerServer

A powerful MCP (Model Context Protocol) server for decompiling and analyzing .NET assemblies, with specialized support for Unity's Assembly-CSharp.dll files. DecompilerServer provides comprehensive decompilation, search, and code analysis capabilities through a rich set of tools and APIs.

## ✨ Features

- **🔍 Comprehensive Analysis**: 38 specialized MCP tools for deep assembly inspection
- **⚡ High Performance**: Optimized decompilation with intelligent caching and lazy loading  
- **🎮 Unity-Focused**: Specialized support for Unity Assembly-CSharp.dll files
- **🔧 Code Generation**: Generate Harmony patches, detour stubs, and extension method wrappers
- **📊 Advanced Search**: Search types, members, attributes, string literals, and usage patterns
- **🧬 Relationship Analysis**: Inheritance tracking, usage analysis, and implementation discovery
- **📝 Source Management**: Line-precise source slicing and batch decompilation
- **🛠️ Developer Tools**: IL analysis, AST outlining, and transpiler target suggestions

## 🚀 Quick Start

### Prerequisites

- .NET 8.0 SDK or later
- Windows, macOS, or Linux

### Installation

1. **Clone the repository**:
   ```bash
   git clone https://github.com/pardeike/DecompilerServer.git
   cd DecompilerServer
   ```

2. **Build the project**:
   ```bash
   dotnet build DecompilerServer.sln
   ```

3. **Run tests** (optional):
   ```bash
   dotnet test
   ```

> **💡 Ready for AI tools?** Skip to [🤖 AI Tool Integration](#-ai-tool-integration) to configure with GitHub Copilot, Claude Desktop, or other MCP-compatible AI assistants.

## 🤖 AI Tool Integration

DecompilerServer implements the Model Context Protocol (MCP) for seamless integration with AI coding assistants. The server communicates via STDIO and automatically exposes all 38 tools for AI discovery and usage.

### Supported AI Tools

- **GitHub Copilot** - With MCP support in VS Code
- **Claude Desktop** - Anthropic's desktop client with MCP integration  
- **Codeium** - AI coding assistant with MCP support
- **VS Code Extensions** - Any MCP-compatible extension
- **OpenAI ChatGPT** - Via MCP-compatible clients
- **Custom MCP Clients** - Any client implementing the MCP protocol

### Configuration Examples

#### Claude Desktop

Add to your `claude_desktop_config.json`:

```json
{
  "mcpServers": {
    "decompiler": {
      "command": "dotnet",
      "args": ["run", "--project", "/path/to/DecompilerServer"],
      "env": {
        "DOTNET_CLI_TELEMETRY_OPTOUT": "1"
      }
    }
  }
}
```

#### VS Code with MCP Extension

Configure in `.vscode/settings.json`:

```json
{
  "mcp.servers": [
    {
      "name": "decompiler", 
      "command": "dotnet",
      "args": ["run", "--project", "/path/to/DecompilerServer"],
      "stdio": true
    }
  ]
}
```

#### GitHub Copilot (VS Code)

With MCP support enabled:

```json
{
  "github.copilot.mcp.servers": {
    "decompiler": {
      "command": "dotnet run --project /path/to/DecompilerServer"
    }
  }
}
```

### How AI Tools Use DecompilerServer

1. **Tool Discovery**: AI tools automatically discover all 38 available tools via MCP's `tools/list` endpoint
2. **Assembly Loading**: AI assistants can load Unity assemblies using `LoadAssembly` 
3. **Code Analysis**: Tools like `SearchTypes`, `FindUsages`, and `GetDecompiledSource` help AI understand codebases
4. **Code Generation**: AI can generate Harmony patches, detour stubs, and extension methods using specialized tools

### Example AI Workflow

```
Human: "Help me understand this Unity game's player movement system"

AI: I'll help you analyze the player movement system. Let me start by loading the assembly and searching for player-related classes.

[AI calls LoadAssembly with the game's Assembly-CSharp.dll]
[AI calls SearchTypes with query "Player" to find relevant classes]  
[AI calls GetDecompiledSource to examine PlayerController methods]
[AI calls FindUsages to see how movement methods are called]

AI: Based on my analysis, I found a PlayerController class with Move(), Jump(), and HandleInput() methods. The movement system uses Unity's Rigidbody physics...
```

### Basic Usage

1. **Start the server**:
   ```bash
   dotnet run --project DecompilerServer
   ```

2. **Load an assembly** (via MCP client):
   ```json
   {
     "tool": "LoadAssembly",
     "arguments": {
       "assemblyPath": "/path/to/Assembly-CSharp.dll"
     }
   }
   ```

3. **Explore the assembly**:
   ```json
   {
     "tool": "ListNamespaces",
     "arguments": {}
   }
   ```

4. **Search for types**:
   ```json
   {
     "tool": "SearchTypes", 
     "arguments": {
       "query": "Player",
       "limit": 10
     }
   }
   ```

5. **Decompile source code**:
   ```json
   {
     "tool": "GetDecompiledSource",
     "arguments": {
       "memberId": "<member-id-from-search>"
     }
   }
   ```

## 🏗️ Architecture

DecompilerServer is built on a robust, modular architecture:

### Core Services
- **AssemblyContextManager**: Assembly loading and context management
- **MemberResolver**: Member ID resolution and validation  
- **DecompilerService**: C# decompilation with caching
- **SearchServiceBase**: Search and pagination framework
- **UsageAnalyzer**: Code usage analysis
- **InheritanceAnalyzer**: Inheritance relationship tracking
- **ResponseFormatter**: Standardized JSON response formatting

### MCP Tools (38 endpoints)
- **Core Operations**: Status, LoadAssembly, Unload, WarmIndex
- **Discovery**: ListNamespaces, SearchTypes, SearchMembers, SearchAttributes
- **Analysis**: GetMemberDetails, GetDecompiledSource, GetSourceSlice, GetIL
- **Relationships**: FindUsages, FindCallers, FindCallees, GetOverrides
- **Code Generation**: GenerateHarmonyPatchSkeleton, GenerateDetourStub
- **Advanced**: BatchGetDecompiledSource, SuggestTranspilerTargets, PlanChunking

### Member ID System
All members use a stable ID format: `<mvid-32hex>:<token-8hex>:<kind-code>`
- **Kind Codes**: T=Type, M=Method/Constructor, P=Property, F=Field, E=Event, N=Namespace
- IDs remain consistent across sessions for reliable automation

## 📖 Examples

### Analyzing a Unity Assembly

```bash
# 1. Load Unity's main assembly
{
  "tool": "LoadAssembly",
  "arguments": {
    "assemblyPath": "Game_Data/Managed/Assembly-CSharp.dll"
  }
}

# 2. Find all Player-related classes
{
  "tool": "SearchTypes",
  "arguments": {
    "query": "Player",
    "accessibility": "public"
  }
}

# 3. Get detailed information about a specific type
{
  "tool": "GetMemberDetails", 
  "arguments": {
    "memberId": "abc123...def:12345678:T"
  }
}

# 4. Generate a Harmony patch skeleton
{
  "tool": "GenerateHarmonyPatchSkeleton",
  "arguments": {
    "memberId": "abc123...def:87654321:M",
    "patchType": "Prefix"
  }
}
```

### Batch Analysis Workflow

```bash
# 1. Search for methods containing specific string literals
{
  "tool": "SearchStringLiterals",
  "arguments": {
    "query": "PlayerDied",
    "caseSensitive": false
  }
}

# 2. Batch decompile multiple members
{
  "tool": "BatchGetDecompiledSource",
  "arguments": {
    "memberIds": ["id1", "id2", "id3"]
  }
}

# 3. Analyze usage patterns
{
  "tool": "FindUsages",
  "arguments": {
    "memberId": "target-member-id",
    "includeReferences": true
  }
}
```

## 🔧 Development

### Building

```bash
# Build entire solution
dotnet build DecompilerServer.sln

# Build specific project
dotnet build DecompilerServer.csproj
```

### Testing

```bash
# Run all tests
dotnet test

# Run with verbose output
dotnet test --verbosity normal

# Run specific test class
dotnet test --filter "ClassName=CoreToolTests"
```

### Code Formatting

```bash
# Format code before committing
dotnet format DecompilerServer.sln
```

### Project Structure

```
DecompilerServer/
├── Services/           # Core service implementations (7 services)
├── Tools/             # MCP tool implementations (38 tools)  
├── Tests/             # Comprehensive xUnit test suite
├── TestLibrary/       # Test assembly for validation
├── Program.cs         # Application entry point
├── ServiceLocator.cs  # Service locator for MCP tools
└── *.md              # Documentation files
```

## 📚 Documentation

- **[HELPER_METHODS_GUIDE.md](HELPER_METHODS_GUIDE.md)** - Comprehensive guide to service helpers and implementation patterns
- **[TESTING.md](TESTING.md)** - Complete testing framework documentation and best practices  
- **[TODO.md](TODO.md)** - Prioritized enhancement opportunities and development roadmap
- **[.github/copilot-instructions.md](.github/copilot-instructions.md)** - Detailed project architecture and AI development guidelines

## 🤝 Contributing

We welcome contributions! Please see our development documentation for detailed guidelines:

1. **Read the documentation**: Start with [HELPER_METHODS_GUIDE.md](HELPER_METHODS_GUIDE.md) and [TESTING.md](TESTING.md)
2. **Check the roadmap**: Review [TODO.md](TODO.md) for priority items
3. **Follow patterns**: Study existing tools and services for consistency
4. **Test thoroughly**: Use the comprehensive xUnit framework
5. **Format code**: Run `dotnet format` before committing

### Development Workflow

1. Fork the repository
2. Create a feature branch
3. Make your changes following existing patterns
4. Add tests for new functionality
5. Run `dotnet test` to ensure all tests pass
6. Run `dotnet format` to maintain code style
7. Submit a pull request with a clear description

## 🛡️ Thread Safety & Performance

DecompilerServer is designed for high performance and thread safety:

- **Thread-Safe Access**: Uses `ReaderWriterLockSlim` for concurrent operations
- **Intelligent Caching**: Decompiled source with line indexing for efficient slicing
- **Lazy Loading**: Minimal upfront computation, build indexes on demand
- **Pagination**: All search results paginated (default: 50, max: 500 items)

## 🔌 MCP Integration

DecompilerServer implements the Model Context Protocol for seamless integration with AI development tools:

- **AI Tool Ready**: Compatible with GitHub Copilot, Claude Desktop, Codeium, and other MCP clients
- **Auto-Discovery**: Tools automatically discovered via `[McpServerTool]` attributes
- **STDIO Transport**: Communicates via standard input/output for universal compatibility
- **Standardized Responses**: Consistent JSON formatting across all endpoints
- **Error Handling**: Structured error responses with detailed messages
- **Type Safety**: Strong typing for all tool parameters and responses

See [🤖 AI Tool Integration](#-ai-tool-integration) for configuration examples and setup instructions.

## 📋 System Requirements

- **.NET 8.0** or later
- **Memory**: Recommended 4GB+ for large assemblies
- **Storage**: Varies by assembly size (caching may require additional space)
- **Platform**: Windows, macOS, or Linux

## 📜 License

This project is open source. Please check the repository for license details.

## 🙏 Acknowledgments

Built with:
- **[ICSharpCode.Decompiler](https://github.com/icsharpcode/ILSpy)** - Core decompilation engine
- **[ModelContextProtocol](https://github.com/microsoft/model-context-protocol)** - MCP server framework
- **Microsoft.Extensions.Hosting** - Application hosting and dependency injection

---

*For detailed technical documentation and advanced usage scenarios, please refer to the comprehensive guides in the repository documentation.*