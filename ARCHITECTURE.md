# DecompilerServer Architecture

This is the single durable technical reference for the repository.

Documentation policy:
- `README.md` is the user-facing entry point.
- `ARCHITECTURE.md` is the long-lived implementation reference.
- `TODO.md` is backlog only.
- Historical plans, migration notes, helper guides, and testing guides should not be recreated unless they add durable information that does not fit here.

## Runtime Model

`Program.cs` builds a hosted stdio MCP server and auto-discovers tools from the assembly.

Key startup behavior:
- registers `DecompilerWorkspace` plus the legacy singleton services;
- registers `WorkspaceBootstrapService`;
- calls `.AddMcpServer().WithStdioServerTransport().WithToolsFromAssembly()`;
- initializes `ServiceLocator` with the built service provider.

Tool shape:
- MCP tools are static methods under `Tools/`;
- tool methods should return through `ResponseFormatter.TryExecute(...)`;
- tool names exposed to clients are normally snake_case versions of the C# method names.

## Service Graph

### DecompilerWorkspace

`DecompilerWorkspace` is the root of the multi-context model.

Responsibilities:
- loads or replaces one alias at a time;
- tracks the current alias;
- holds the alias-to-session map;
- maps assembly MVID to alias for follow-up routing;
- persists registrations to disk;
- restores registered aliases at startup.

Registry location:
- default path is `LocalApplicationData/DecompilerServer/contexts.json`;
- if `LocalApplicationData` is unavailable, it falls back to `~/.decompilerserver/DecompilerServer/contexts.json`.

### DecompilerSession

Each loaded alias owns one `DecompilerSession`.

A session bundles:
- `AssemblyContextManager`
- `MemberResolver`
- `DecompilerService`
- `UsageAnalyzer`
- `InheritanceAnalyzer`

Sessions are isolated per loaded assembly so caches and member resolution stay version-specific.

### AssemblyContextManager

Owns one loaded assembly context and the expensive decompiler state:
- loaded PE file;
- type system;
- configured `CSharpDecompiler`;
- indexes and cache-adjacent metadata;
- assembly summary counts and settings.

This is the boundary for one loaded assembly, not the whole process.

### MemberResolver

`MemberResolver` owns stable member IDs, normalization, resolution, and resolution caching.

Stable ID format:
- `<mvid-32hex>:<token-8hex>:<kind-code>`

Kind codes:
- `T` type
- `M` method or constructor
- `P` property
- `F` field
- `E` event
- `N` namespace

These IDs are stable for a given assembly MVID and are the basis for cheap follow-up MCP calls.

### DecompilerService

`DecompilerService` handles source retrieval and source caching.

Important behaviors:
- caches decompiled documents and related source payloads;
- supports line-range retrieval;
- supports focused entity decompilation through `DecompileEntitySnippet(...)`.
- decompiled non-type members are cached and returned as member-scoped snippets rather than whole containing types.

`DecompileEntitySnippet(...)` is the correct path for compare workflows that need one concrete member body rather than the whole containing type.

### UsageAnalyzer and InheritanceAnalyzer

These services provide graph-style analysis:
- usages;
- callers and callees;
- string literal search;
- base and derived types;
- implementations;
- overrides and overloads.

### TypeSurfaceComparer

`TypeSurfaceComparer` defines the shared semantics for structural type diffs.

It is the authority for:
- direct-member enumeration;
- member-kind normalization;
- compiler-generated type filtering;
- type-surface change detection used by both `compare_symbols` and `compare_contexts`.

### ResponseFormatter

`ResponseFormatter` centralizes tool response formatting and exception wrapping.

Current response conventions:
- camelCase JSON;
- null values omitted where appropriate;
- structured errors instead of throwing across the MCP boundary.

Structured error responses preserve top-level `status`, `message`, and `details` fields, and also include `error.code`, `error.message`, `error.details`, and optional `error.hints`. Symbol-resolution failures should use stable codes such as `type_not_found`, `member_not_found`, `ambiguous_member`, `wrong_symbol_kind`, `invalid_member_id`, and `no_assembly_loaded`.

## Routing Model

The repository currently supports both the workspace model and the older single-context fallback. New work should target the workspace-aware path.

### ToolSessionRouter

`ToolSessionRouter` is the only routing layer tools should use.

Rules:
- discovery or search tools with no `memberId` use `GetForContext(contextAlias)`;
- follow-up tools that take `memberId` use `GetForMember(memberId, contextAlias)`;
- explicit `contextAlias` on a member-based tool wins over MVID routing;
- without an explicit alias, member-based tools route by the `memberId` MVID and then fall back to the current alias.

### ServiceLocator

`ServiceLocator` bridges static MCP tool methods to DI-managed services.

Important behavior:
- production uses the global provider;
- tests can override the provider thread-locally;
- current-session services are resolved from the workspace when available.

## Workspace and Alias Workflow

The intended workflow is to keep multiple assemblies loaded and address them by alias.

Operational rules:
- `load_assembly` loads or replaces one alias;
- omitted aliases normalize to the default alias `default`;
- `makeCurrent` controls whether the loaded alias becomes the current one;
- `list_contexts` reports loaded aliases and which one is current;
- `select_context` changes the default alias;
- `unload` can unload one alias or all aliases, and removes persisted registrations by default;
- `unload(..., preserveRegistration: true)` keeps restart-restore registrations while unloading memory;
- `status` reports current alias plus loaded contexts when the workspace is active.

Startup behavior:
- `WorkspaceBootstrapService` restores persisted registrations;
- startup logs announce each restored alias and its resolved assembly path;
- failed restores are logged as warnings but do not abort server startup.

## Stable API Contracts

These are the contracts other work should preserve unless there is a deliberate breaking change.

### Structured Output

Tool output should stay structured JSON. Do not move compare or overview tools toward pre-rendered text output when structured data is feasible.

### Pagination

Search-style and overview-style endpoints should use:
- `limit`
- `cursor`
- `items`
- `nextCursor`
- `totalEstimate` when applicable

`compare_contexts` uses integer-offset cursors today.

### Member-ID Follow-Up Flow

Once a discovery tool returns a `memberId`, the caller should be able to use follow-up tools without resupplying the alias. That behavior depends on the MVID prefix and must remain reliable.

### Symbol Exploration Flow

Unknown-assembly exploration should stay inside MCP tools:
- use `search_symbols` when the caller has a partial, qualified, or guessed name;
- use `search_types` for type-only discovery and `search_members` for member-only discovery;
- use `list_members` or `get_members_of_type` after a type is found;
- if a member-based tool receives a stale or human-entered symbol, return structured candidates and suggested next tool calls rather than only `Invalid member ID`.

## Compare Model

Comparison is intentionally layered so the caller can stay cheap on context and only drill in when needed.

### compare_contexts

`compare_contexts` is the alias-level structural overview.

Semantics:
- compares type presence and direct member surface between two aliases;
- returns structured summary counts plus type items;
- filters by `namespaceFilter` and optional `deep` traversal;
- filters compiler-generated types by default;
- includes unchanged types only when `includeUnchanged` is true.

Status meanings:
- `added`: type only exists on the right alias;
- `removed`: type only exists on the left alias;
- `changed`: type exists in both aliases and its direct member surface changed;
- `unchanged`: type exists in both aliases and its direct member surface is the same.

`changed` does not mean arbitrary method bodies changed. It means the direct member surface changed according to `TypeSurfaceComparer`.

### compare_symbols

`compare_symbols` is the drill-down tool.

Supported modes:
- `symbolKind: "type"` with `compareMode: "surface"`
- `symbolKind: "method"` with `compareMode: "surface"` or `compareMode: "body"`
- `symbolKind: "field" | "property" | "event"` with `compareMode: "surface"`

Accepted member symbol formats:
- `Namespace.Type:MemberName`
- `Namespace.Type.MemberName`

Surface semantics:
- type compare reports added, removed, and changed direct members;
- member compare reports left and right signatures plus `signatureChanged`.

Body semantics:
- method-only;
- uses `DecompilerService.DecompileEntitySnippet(...)`;
- returns `bodyChanged`, `bodyDiff`, and compact `diffStats`.

Compatibility note:
- `compareMode: "source"` is accepted as an alias for method `compareMode: "body"`.

This limitation is intentional. Non-method symbols do not have one coherent cross-kind meaning for `"body"`.

## Intentional Boundaries

These are current boundaries, not bugs.

- `compare_contexts` is structural and does not inspect method bodies.
- `compare_symbols(compareMode: "body")` is method-only.
- `get_il` currently supports `"IL"` output, not `ILAst`.
- `get_il` returns real IL instructions when the method has a body; abstract, extern, and interface methods report `no_il_body`.
- rename detection across aliases is not special-cased; a rename appears as remove-plus-add at the type or member level.
- compiler-generated noise is excluded from context-wide compare by default.

## Testing Model

Tests use xUnit and real compiled test assemblies.

Important fixtures:
- `Tests/ServiceTestBase.cs`
- `TestLibrary/`
- `EmbeddedSourceTestLibrary/`
- `NestedNoSymbolsTestLibrary/`
- `Tests/TemporaryAssemblyBuilder.cs`

Coverage focus:
- service behavior on real assemblies;
- workspace lifecycle and persistence;
- context-aware routing;
- structured tool output;
- compare behavior under controlled version drift.

Test naming pattern:
- use dedicated `*ToolTests.cs` files for MCP tool behavior;
- use service-level test files when the behavior is below the MCP boundary.

## Contributor Rules

When adding or changing tools:
- keep tool methods static under `Tools/`;
- route through `ToolSessionRouter`, not ad hoc service resolution;
- use `ResponseFormatter.TryExecute(...)`;
- prefer shared helpers such as `TypeSurfaceComparer` over re-implementing comparison semantics;
- prefer structured JSON over preformatted diff text for overview endpoints.

When changing compare behavior:
- keep `compare_contexts` and type-level `compare_symbols` aligned through `TypeSurfaceComparer`;
- keep method body diff opt-in;
- do not broaden `"body"` semantics without a clear symbol-kind-specific design.

When changing documentation:
- update `README.md` for user-facing workflow changes;
- update `ARCHITECTURE.md` for durable implementation or contract changes;
- update `TODO.md` only for backlog changes.

## Verification Checklist

After code changes, run:

```bash
dotnet format DecompilerServer.sln
dotnet test -c Release --no-restore
```

After documentation cleanup or API reshaping, also run a reference sweep:

```bash
rg -n "HELPER_METHODS_GUIDE|TESTING\\.md|MULTI_VERSION_WORKSPACE_PLAN|CommonImplementorGuide|ARCHITECTURE\\.md" .
```
