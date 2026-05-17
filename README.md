# DecompilerServer

DecompilerServer is an MCP server that decompiles and analyzes .NET assemblies. You connect it to your AI coding tool (Claude Desktop, Cursor, VS Code, Codex, etc.) and it lets the AI read, search, and compare .NET code — even across different versions of an assembly.

This is a particularly strong LLM use case because so much real-world .NET behavior lives in binaries the model cannot inspect on its own: third-party libraries, game assemblies, internal builds, and older releases. By giving the model direct decompiler-backed access to those assemblies, you turn "I only see the source in this repo" into "I can inspect the actual code that runs", which makes reverse engineering, migration work, debugging, and version-to-version analysis much more effective.

Common use cases: general .NET assembly inspection, Unity `Assembly-CSharp.dll` workflows, and multi-version browsing such as RimWorld mod porting.

> **Already familiar with .NET and MCP?** Skip to [Advanced Guide](#advanced-guide) for build-from-source instructions, full tool reference, and development details.

---

## Quick Start

This section helps you install DecompilerServer and connect it to your MCP client in a few steps.

### What You Need

**[.NET 10 runtime](https://dotnet.microsoft.com/download/dotnet/10.0)** — the prebuilt release binaries depend on it. Download and install the runtime for your platform before continuing.

### Step 1 — Download

Go to the [latest release](https://github.com/pardeike/DecompilerServer/releases/latest) and download the archive for your platform:

| Platform | File |
|---|---|
| Windows (x64) | `decompilerserver-v…-win-x64.zip` |
| macOS (Apple Silicon) | `decompilerserver-v…-osx-arm64.tar.gz` |
| macOS (Intel) | `decompilerserver-v…-osx-x64.tar.gz` |
| Linux (x64) | `decompilerserver-v…-linux-x64.tar.gz` |
| Linux (ARM64) | `decompilerserver-v…-linux-arm64.tar.gz` |

Extract the archive to a folder you can remember (for example `~/tools/decompiler-server`).

<details>
<summary>Extract commands</summary>

**Windows** (PowerShell):

```powershell
Expand-Archive decompilerserver-v*-win-x64.zip -DestinationPath "$HOME\tools\decompiler-server"
```

**macOS / Linux**:

```bash
mkdir -p ~/tools/decompiler-server
tar -xzf decompilerserver-v*-*.tar.gz -C ~/tools/decompiler-server --strip-components=1
```

</details>

### Step 2 — Configure Your MCP Client

Add DecompilerServer to your MCP client configuration. Replace the path below with the actual location where you extracted the files.

<details>
<summary><strong>Claude Desktop</strong></summary>

Edit the Claude Desktop config file:
- **macOS**: `~/Library/Application Support/Claude/claude_desktop_config.json`
- **Windows**: `%APPDATA%\Claude\claude_desktop_config.json`

```json
{
  "mcpServers": {
    "decompiler": {
      "command": "/absolute/path/to/DecompilerServer"
    }
  }
}
```

On Windows, use the `.exe` extension and escaped backslashes:

```json
{
  "mcpServers": {
    "decompiler": {
      "command": "C:\\tools\\decompiler-server\\DecompilerServer.exe"
    }
  }
}
```

Restart Claude Desktop after saving.

</details>

<details>
<summary><strong>Cursor</strong></summary>

Open Cursor Settings → MCP and add a new server, or edit `.cursor/mcp.json` in your project root:

```json
{
  "mcpServers": {
    "decompiler": {
      "command": "/absolute/path/to/DecompilerServer"
    }
  }
}
```

On Windows, use the `.exe` extension and escaped backslashes.

</details>

<details>
<summary><strong>VS Code (GitHub Copilot)</strong></summary>

Add an entry to your `.vscode/mcp.json` (or workspace settings):

```json
{
  "servers": {
    "decompiler": {
      "type": "stdio",
      "command": "/absolute/path/to/DecompilerServer"
    }
  }
}
```

On Windows, use the `.exe` extension and escaped backslashes.

</details>

<details>
<summary><strong>Codex</strong></summary>

Add an entry to `~/.codex/config.toml`:

```toml
[mcp_servers.decompiler]
command = "/absolute/path/to/DecompilerServer"
```

If you run a framework-dependent build instead of the native app host, configure Codex like this:

```toml
[mcp_servers.decompiler]
command = "dotnet"
args = ["/absolute/path/to/DecompilerServer/bin/Release/net10.0/DecompilerServer.dll"]
```

On Windows, use the `.exe` extension and escaped backslashes.

</details>

### Step 3 — Use It

Once connected, ask your AI assistant to load and explore assemblies. For example:

> "Load the assembly at `/path/to/MyLibrary.dll` and search for types that contain `Controller`."

The assistant will use DecompilerServer tools automatically. It can load assemblies, search types, decompile source, compare versions, and more. See the [Workflow Reference](#workflow-reference) for the full list of operations.

### Troubleshooting

| Problem | Solution |
|---|---|
| **"command not found"** or the server does not start | Verify the path in your MCP config points to the correct executable. On macOS/Linux, ensure the file is executable: `chmod +x DecompilerServer`. |
| **"You must install .NET to run this application"** | Install the [.NET 10 runtime](https://dotnet.microsoft.com/download/dotnet/10.0). The prebuilt binaries do not include the runtime. |
| **macOS blocks the binary** ("unidentified developer") | Open System Settings → Privacy & Security and allow the application, or run: `xattr -d com.apple.quarantine DecompilerServer`. |
| **Server starts but the AI does not use it** | Check that the MCP client has the server enabled. In Claude Desktop, restart the app. In Cursor, check the MCP panel. In VS Code, reload the window. |
| **Assembly fails to load** | Confirm the `.dll` path is correct and that the assembly is a managed .NET assembly (not a native library). |

---

## Advanced Guide

This section covers building from source, the full set of MCP launch options, the complete tool workflow, and contributor information.

### Highlights

- Load assemblies directly with `assemblyPath` or use Unity-friendly `gameDir` loading.
- Keep multiple assemblies loaded at once under aliases such as `rw14`, `rw15`, and `rw16`.
- Use `memberId` follow-up tools without repeatedly resupplying the alias; routing uses the member ID's MVID.
- Compare aliases structurally with `compare_contexts`, then drill into types or members with `compare_symbols`.
- Diff individual method bodies with `compare_symbols(..., compareMode: "body")`.
- Decompiled non-type member source is returned as a member-scoped snippet rather than a whole containing type.

### Requirements

- .NET 10 runtime for release assets or built output
- .NET 10 SDK if you want to build from source

### Build from Source

```bash
git clone https://github.com/pardeike/DecompilerServer.git
cd DecompilerServer
dotnet build DecompilerServer.sln -c Release
dotnet run --project DecompilerServer.csproj -c Release
```

### MCP Client Launch Options

**Framework-dependent build output** (requires `dotnet` on PATH):

```text
command: dotnet
args:
  - /absolute/path/to/DecompilerServer/bin/Release/net10.0/DecompilerServer.dll
```

**Native app host from `dotnet build`:**
- macOS/Linux: `/absolute/path/to/DecompilerServer/bin/Release/net10.0/DecompilerServer`
- Windows: `C:\\absolute\\path\\to\\DecompilerServer\\bin\\Release\\net10.0\\DecompilerServer.exe`

**Native app host from `dotnet publish`:**
- macOS/Linux: `/absolute/path/to/DecompilerServer/bin/Release/net10.0/publish/DecompilerServer`
- Windows: `C:\\absolute\\path\\to\\DecompilerServer\\bin\\Release\\net10.0\\publish\\DecompilerServer.exe`

Release assets are packaged as framework-dependent single-file executables and still require the .NET 10 runtime.

### Workflow Reference

**Load one or more assemblies:**

```text
load_assembly({
  "assemblyPath": "/path/to/rw14/Assembly-CSharp.dll",
  "contextAlias": "rw14"
})

load_assembly({
  "gameDir": "/path/to/rw16",
  "assemblyFile": "Assembly-CSharp.dll",
  "contextAlias": "rw16"
})
```

**Inspect loaded aliases:**

```text
list_contexts({})
status({})
```

**Search and decompile:**

```text
search_symbols({
  "query": "Pawn.Kill",
  "limit": 10,
  "contextAlias": "rw16"
})

search_types({
  "query": "Pawn",
  "limit": 10,
  "contextAlias": "rw16"
})

list_members({
  "typeId": "<type-member-id-from-search>",
  "mode": "signatures"
})

get_decompiled_source({
  "memberId": "<member-id-from-search>"
})
```

Once you have a `memberId`, follow-up tools normally route to the correct loaded assembly automatically.

For foreign-code exploration, prefer this path before falling back to shell tools:

1. Load the assembly with `load_assembly` and verify the active alias with `list_contexts` or `status`.
2. Start broad with `search_symbols` when you have a partial or guessed name; use `search_types` only when you specifically want types.
3. Use `list_members` or `get_members_of_type` on the resolved type before guessing method IDs.
4. Fetch code with `get_decompiled_source`, `get_source_slice`, or `plan_chunking`.
5. Move outward with `find_callers`, `find_usages`, `find_callees`, `get_il`, or compare tools.

If a guessed member does not exist, member-based tools return structured error hints with likely candidates and concrete next tool calls. For example, a stale method guess can still resolve the type and point you at nearby overrides instead of forcing a `grep` or `monodis` fallback.

**Compare aliases:**

```text
compare_contexts({
  "leftContextAlias": "rw14",
  "rightContextAlias": "rw16",
  "namespaceFilter": "Verse",
  "deep": true
})

compare_symbols({
  "leftContextAlias": "rw14",
  "rightContextAlias": "rw16",
  "symbol": "Verse.Root_Play",
  "symbolKind": "type"
})

compare_symbols({
  "leftContextAlias": "rw14",
  "rightContextAlias": "rw16",
  "symbol": "Verse.Pawn:Kill",
  "symbolKind": "method",
  "compareMode": "body"
})
```

### Development

```bash
dotnet format DecompilerServer.sln
dotnet test -c Release
```

After restore/build has already completed, `dotnet test -c Release --no-restore` is the faster repeat check.

### Agent Skill

The repo includes a portable skill at [skills/decompiler-mcp/SKILL.md](skills/decompiler-mcp/SKILL.md). Install or copy that folder into Codex, Claude, or another skill-aware assistant to bias agents toward the intended DecompilerServer workflow and away from premature shell fallbacks.

### Releasing

The GitHub `Release` workflow is triggered by pushing a tag that starts with `v` and matches the project version in `DecompilerServer.csproj`.

For example, if the project version is `1.3.4`:

```bash
git tag -a v1.3.4 -m "Release v1.3.4"
git push origin v1.3.4
```

### Documentation

- [README.md](README.md): user-facing overview and quick start
- [ARCHITECTURE.md](ARCHITECTURE.md): durable technical reference (implementation details, routing, compare semantics, testing)
- [TODO.md](TODO.md): backlog and future work

If you are changing implementation details, tool-routing behavior, compare semantics, or test patterns, update [ARCHITECTURE.md](ARCHITECTURE.md) rather than creating a new standalone guide.
