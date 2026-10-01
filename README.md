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

### Tool approvals and plan mode

Inspection tools advertise the standard MCP `readOnlyHint` annotation so clients
can recognize read-only operations in plan mode and approval policies. Loading,
unloading, selecting contexts, changing settings, and explicit cache operations
remain state-changing tools. Update the server and reconnect or restart your MCP
client to refresh its tool catalog. Claude Code, Codex, and other clients decide
how to use these hints; annotations do not override their permission settings.

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

Tool responses are compact by default to reduce AI context use. Complete source
is returned in `data.code`; source slices keep their actual line ranges, IL is
returned as structured instructions, and errors contain one diagnostic object.
Source provenance, paging, modifier flags, and recovery hints remain available.

For clients that require the previous response fields, configure the MCP server
environment with `DECOMPILER_RESPONSE_MODE=extended` and restart the server.
This is a user setting; tools do not accept a response-mode argument.

For Codex, add the setting to the existing server entry:

```toml
[mcp_servers.decompiler.env]
DECOMPILER_RESPONSE_MODE = "extended"
```

For JSON-based MCP clients, add `"env": { "DECOMPILER_RESPONSE_MODE": "extended" }`
to the server configuration. Remove the setting or use `compact` for the default.
See [ARCHITECTURE.md](ARCHITECTURE.md#responseformatter) for the field contracts.

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

### Trusted Local Codex Install

If Codex for Mac warns about the downloaded MCP executable, install the server from source instead of running a quarantined release artifact:

```bash
./scripts/install-codex-mcp.sh
```

The installer publishes an `osx-arm64` Release build to `~/.codex/mcp-servers/decompiler/DecompilerServer`, removes quarantine metadata, signs the executable with the best local code-signing identity available, updates Codex's global `decompiler` MCP entry to launch that installed copy, installs the bundled `decompiler-mcp` skill into `~/.codex/skills/decompiler-mcp`, and maintains a managed DecompilerServer pointer block in `~/.codex/AGENTS.md`.

For local Codex stdio use, this avoids Safari/Gatekeeper quarantine state on downloaded binaries. For distributable Gatekeeper acceptance, sign with a `Developer ID Application` identity and notarize the archive before installation.

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

**Inspect loaded and registered aliases:**

```text
status({})
list_contexts({ "includeRegistered": true, "limit": 50 })
get_server_stats({ "contextAlias": "rw16" })
```

Registered aliases are restored lazily when the server starts. `list_contexts` returns resident contexts and a registered-alias count; request `includeRegistered: true` with `limit`, `cursor`, and optional `filter` to page `registeredAliases`; selecting or explicitly addressing a registered alias loads it on demand. This keeps a large workspace history from consuming memory and file descriptors in every MCP client process.

At most four contexts are resident by default. When another context is needed, the least recently used context that is not serving a tool call is disposed while its lightweight registration remains available for transparent reload. Set `DECOMPILER_MAX_LOADED_CONTEXTS` to a positive integer in the MCP server environment to choose a different hard limit. A request returns `context_capacity_busy` rather than exceeding the limit when every resident context is actively in use.

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

1. Load the assembly with `load_assembly`, or activate a registered alias by passing `contextAlias`, then verify the active alias with `status`.
2. Start broad with `search_symbols` when you have a fragment; use `resolve_member_id` first when you have a fully-qualified or XML-doc-like guess such as `Namespace.Type.Member` or `M:Namespace.Type.Member`.
3. Use `list_members` on the resolved type (`get_members_of_type` is its compatibility alias) before guessing method IDs.
4. Fetch code with `get_decompiled_source`, `get_source_slice`, or `plan_chunking`.
5. Move outward with `find_callers`, `find_usages`, `find_callees`, `get_il`, or compare tools. `find_callees` returns callee-focused `targetMemberId`, `symbol`, `opcode`, `offset`, and `resolution` fields; calls into another loaded assembly link to its exact member ID; other external calls retain referenced assembly identity.
6. Use `get_server_stats` when you need cache/index/performance diagnostics for one alias; `status` is the cheaper current-alias overview.

Member lists and searches hide generated members, backing fields, and accessors by
default. Use `includeCompilerGenerated: true` when inspecting that metadata.
Signatures retain generic arguments and `ref`/`out`/`in` modifiers, and identify
the declaring type.

Symbol lookup preserves supplied method parameters. Ambiguous names return candidates;
use a canonical ID or a signature such as `M:Namespace.Type.Method(System.String)`.
Source and IL requests must use the defining assembly context. Dependency failures
provide loading hints; unknown canonical MVIDs never fall back to an unrelated assembly.

Type metadata outlines return child member IDs and support `limit`/`cursor` paging.
Structural comparison includes qualified types, modifiers, property/event accessors,
generic constraints, and parameter defaults.

`find_callers` and `find_usages` default to `scope: "assembly"`. Use
`scope: "workspace"` to trace references across currently loaded contexts.
Responses state the scope and searched contexts; deferred registrations are not
loaded automatically. Different assembly versions remain distinct.

`batch_get_decompiled_source` returns at most `maxLinesPerMember` lines per member
(default 50). Check each entry's `hasMore` and continue with `get_source_slice`
starting at `nextStartLine`. Outer `truncated` indicates partial content or budget
exhaustion; `budgetTruncated` identifies the latter. Raise `maxLinesPerMember`
when full members fit the requested character budget.

If a guessed member does not exist, member-based tools return structured error hints with likely candidates and concrete next tool calls. `search_symbols` also degrades gracefully for `Type.MissingMember`: if the type resolves but the member does not, it returns the type, its direct members, and a diagnostic instead of an empty success.

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
./scripts/verify.py
```

The verification command runs formatting, Release tests, and a diff check. It prints `ok` only when all steps pass; full output is saved under ignored `artifacts/logs/`. On failure it reports the failed step, diagnostics, and log path.

### Agent Skill

The repo includes a portable skill at [skills/decompiler-mcp/SKILL.md](skills/decompiler-mcp/SKILL.md). For Codex, `./scripts/install-codex-mcp.sh` installs that skill and keeps the global `AGENTS.md` pointer up to date. For Claude or another skill-aware assistant, copy the skill folder manually to bias agents toward the intended DecompilerServer workflow and away from premature shell fallbacks.

### Releasing

The GitHub `Release` workflow is triggered by pushing a tag that starts with `v` and matches the project version in `DecompilerServer.csproj`.

To publish reviewed release text, add `.github/release-notes/<tag>.md` with the release title as a first-line Markdown heading, a blank line, and the release body. The workflow uses that title and body verbatim; tags without a notes file use GitHub-generated notes.

For example, if the project version is `1.3.7`:

```bash
git tag -a v1.3.7 -m "Release v1.3.7"
git push origin v1.3.7
```

### Documentation

- [README.md](README.md): user-facing overview and quick start
- [ARCHITECTURE.md](ARCHITECTURE.md): durable technical reference (implementation details, routing, compare semantics, testing)
- [TODO.md](TODO.md): backlog and future work

If you are changing implementation details, tool-routing behavior, compare semantics, or test patterns, update [ARCHITECTURE.md](ARCHITECTURE.md) rather than creating a new standalone guide.
