# DecompilerServer

DecompilerServer is a stdio MCP server for decompiling and analyzing managed .NET assemblies. It works well for general assemblies, Unity `Assembly-CSharp.dll` workflows, and multi-version browsing such as RimWorld mod porting.

## Documentation

- [README.md](README.md): user-facing overview and quick start
- [ARCHITECTURE.md](ARCHITECTURE.md): durable technical reference
- [TODO.md](TODO.md): backlog and future work

## Highlights

- Load assemblies directly with `assemblyPath` or use Unity-friendly `gameDir` loading.
- Keep multiple assemblies loaded at once under aliases such as `rw14`, `rw15`, and `rw16`.
- Use `memberId` follow-up tools without repeatedly resupplying the alias; routing uses the member ID's MVID.
- Compare aliases structurally with `compare_contexts`, then drill into types or members with `compare_symbols`.
- Diff individual method bodies with `compare_symbols(..., compareMode: "body")`.
- Decompiled non-type member source is returned as a member-scoped snippet rather than a whole containing type.

## Requirements

- .NET 10 runtime for release assets or built output
- .NET 10 SDK if you want to build from source

## Build and Run

```bash
git clone https://github.com/pardeike/DecompilerServer.git
cd DecompilerServer
dotnet build DecompilerServer.sln -c Release
dotnet run --project DecompilerServer.csproj -c Release
```

## MCP Client Launch

Framework-dependent build output:

```text
command: dotnet
args:
  - /absolute/path/to/DecompilerServer/bin/Release/net10.0/DecompilerServer.dll
```

Native app host from `dotnet build` or `dotnet publish`:
- macOS/Linux: `/absolute/path/to/DecompilerServer/bin/Release/net10.0/DecompilerServer`
- Windows: `C:\\absolute\\path\\to\\DecompilerServer\\bin\\Release\\net10.0\\DecompilerServer.exe`

Release assets are framework-dependent and still require the .NET 10 runtime.

## Basic Workflow

Load one or more assemblies:

```text
load_assembly({
  "assemblyPath": "/path/to/YourAssembly.dll",
  "contextAlias": "libA"
})

load_assembly({
  "gameDir": "/path/to/unity/game",
  "assemblyFile": "Assembly-CSharp.dll",
  "contextAlias": "rw16"
})
```

Inspect loaded aliases:

```text
list_contexts({})
status({})
```

Search and decompile:

```text
search_types({
  "query": "Pawn",
  "limit": 10,
  "contextAlias": "rw14"
})

get_decompiled_source({
  "memberId": "<member-id-from-search>"
})
```

Once you have a `memberId`, follow-up tools normally route to the correct loaded assembly automatically.

Compare aliases:

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

## Development

```bash
dotnet format DecompilerServer.sln
dotnet test -c Release --no-restore
```

If you are changing implementation details, tool-routing behavior, compare semantics, or test patterns, update [ARCHITECTURE.md](ARCHITECTURE.md) rather than creating a new standalone guide.
