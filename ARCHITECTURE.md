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
- configures concise MCP server instructions through `McpServerOptions.ServerInstructions`;
- calls `.AddMcpServer(...).WithStdioServerTransport().WithToolsFromAssembly()`;
- initializes `ServiceLocator` with the built service provider.

Tool shape:
- MCP tools are static methods under `Tools/`;
- tool methods should return through `ResponseFormatter.TryExecute(...)`;
- tool names exposed to clients are normally snake_case versions of the C# method names.

## MCP tool annotations

Every tool explicitly declares `McpServerTool(ReadOnly = ...)`, exposed as
`annotations.readOnlyHint` in `tools/list`. Inspection, search, comparison, and
code-template tools are read-only. Generated code is returned as text, never
written to a project. Internal caches, diagnostics, and lazy context activation
are implementation details of inspection, not edits to the inspected assemblies.

`load_assembly`, `unload`, `select_context`, `set_decompile_settings`,
`clear_caches`, and `warm_index` remain non-read-only because their requested
operation changes workspace registrations, selection, settings, or cache state.
Do not blanket-label the server read-only or change client permission settings.
Other annotation hints retain the SDK defaults, including open-world behavior
because source retrieval can fetch SourceLink URLs.

`ToolAnnotationTests` starts the actual server over stdio and checks the tool
catalog received by a client. Approval decisions remain client policy.

## Service Graph

### DecompilerWorkspace

`DecompilerWorkspace` is the root of the multi-context model.

Responsibilities:
- loads or replaces one alias at a time;
- tracks the current alias;
- holds the alias-to-session map;
- maps assembly MVID to alias for follow-up routing;
- persists registrations to disk;
- activates registered aliases on demand;
- keeps at most four sessions resident by default and evicts the least recently used idle session when capacity is reached.

The resident-session limit is a hard count, configurable through `DECOMPILER_MAX_LOADED_CONTEXTS`. Registrations, load requests, runtime MVID routing, and per-context decompiler settings survive an LRU eviction, so addressing the alias or one of its member IDs can rebuild the session. If every resident session has an active lease, loading another context fails with `context_capacity_busy` instead of temporarily exceeding the limit.

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
Disposing a session eagerly clears its source, member-resolution, usage, and string-literal caches, disposes original-source and PDB resources, and then disposes the assembly context.

Tool calls acquire a `DecompilerSessionLease` for each routed session. Batch source holds each lease until that item has been materialized, then releases it before processing the next item; other operations hold their leases for the entire operation. A leased session cannot be replaced, explicitly unloaded, or selected as an LRU victim. This is required because MCP tools may execute concurrently and compare tools hold two sessions at once.

### AssemblyContextManager

Owns one loaded assembly context and the expensive decompiler state:
- loaded PE file;
- type system;
- configured `CSharpDecompiler`;
- indexes and cache-adjacent metadata;
- assembly summary counts and settings.

This is the boundary for one loaded assembly, not the whole process.

Resolved dependency `PEFile` instances are owned by one `OwnedAssemblyResolver` per context. The resolver prefetches complete images so dependency streams do not retain native file descriptors, caches resolutions shared by the analysis and decompiler type systems, and deterministically disposes cached files with the context.

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
IDs use the defining module's MVID, including inherited or dependency members. Parameter-qualified guesses preserve overload identity; unqualified ambiguous members return candidates rather than selecting the first method. Exact XML documentation signatures use ILSpy's ID parser.
Synthetic entities with nil metadata tokens are excluded from discovery and indexes;
zero-token IDs are rejected with `invalid_member_id` rather than resolving to another entity.

Signatures preserve generic arguments and method parameters, arrays, nullable types,
and `ref`/`out`/`in` modifiers. Signature summaries retain `declaringType`.
Search and member-list tools hide compiler-generated members and accessors by default;
`includeCompilerGenerated: true` exposes real metadata members, including accessors.
Generated-type filtering includes enclosing types. Structural comparison still uses
the direct surface defined by `TypeSurfaceComparer`, without duplicate accessors.

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

`find_callees` is callee-shaped, not usage-shaped. `targetMemberId` identifies a local
member (`resolution: "assembly"`) or an exact member in another resident context
(`resolution: "workspace"`). External dependencies retain `referencedAssembly`
when available, plus symbol/type and call-site metadata. Generic references are
resolved to their metadata definitions. Legacy aliases remain in extended output.

`find_callers` and `find_usages` accept `scope: "assembly"` (default) or `"workspace"`.
Responses identify `scope` and `searchedContexts`; workspace scope searches only
currently resident contexts, without activating persisted aliases. An atomic leased
snapshot prevents replacement/eviction during the query. Matching uses defining
MVID plus metadata token, including generic method/type references, so different
versions and overloads cannot cross-match. Usage entries include call-site `offset`
and `memberName`; properties/events match their accessor calls. Workspace results
are deduplicated by site and sorted by member ID and offset before pagination.

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
- JSON operators and Unicode emitted literally, with required string/control-character escaping;
- structured errors instead of throwing across the MCP boundary.

Compact output is the default. Users can set `DECOMPILER_RESPONSE_MODE=extended`
in their MCP process environment to restore the previous response fields. The
setting is read at startup, registered as immutable `ResponseFormatOptions`,
and has no tool parameter or runtime setter. Invalid values fail startup.

`ResponseFormatter` uses separate cached serializers for compact and extended
output. Compact type metadata changes only the wire representation; service
documents, source hashes, caches, and IL analysis retain their complete data.

Compact contracts:
- Successful responses retain `status: "ok"` and `data`.
- Errors retain `status: "error"` and one `error` object with `code`, `message`,
  `details`, and optional `hints`. Extended output also retains the legacy
  top-level `message` and `details` aliases. Candidate lists and recovery hints
  are preserved, with compact member summaries.
- Complete source uses a single `code` string instead of `lines`, preserving
  every newline and trailing blank line. It omits echoed `memberId`, derivable
  `totalLines`, `hash`, and `includeHeader`. Omitted source `language` means C#;
  VB remains explicit. `sourceKind`, `sourcePath`, and `sourceUri` retain provenance.
- Source slices retain `code`, actual `startLine`/`endLine`, document `totalLines`,
  and provenance, but omit `memberId`, `hash`, and C# language.
- Batch entries contain one member ID, code, actual line bounds, total lines,
  and provenance. Errors are per-entry `error` objects instead of fabricated
  source documents. `totalCharacters` remains; echoed `requested` and derivable
  `processed` counts are omitted. `maxLinesPerMember` defaults to 50 and must
  be positive. Each entry reports `hasMore` and, when partial, `nextStartLine`
  for source-slice continuation. Outer `truncated` covers partial entries or
  budget exhaustion; `budgetTruncated` specifically identifies exhaustion of
  `maxTotalChars`. Batch method snippets use the same header-free coordinate system as source slices; type documents retain headers.
- Compact string-literal results omit a `snippet` that merely repeats `value`;
  extended output keeps the legacy field.
- IL retains structured `instructions`, body metadata, paging/window metadata,
  and completeness. It omits parallel text disassembly, echoed member/format,
  and derivable counts. Instructions keep hexadecimal metadata tokens; the
  duplicate decimal token/operand representations are omitted. Non-token
  operands remain. Methods without a body retain a structured method summary.
- Callees retain target IDs, symbols, declaring types, kind, opcode, offset,
  resolution, and hexadecimal operand tokens. The legacy `inMember`/`inType`,
  redundant `resolved`, and decimal operand-token fields are omitted.
- Signature previews return their member summary directly instead of repeating
  the signature outside it. Ordinary member summaries/details omit `fullName`
  and `namespace` aliases when `declaringType` + `name` identifies the symbol.
  Type summaries and symbols without a declaring type retain those fields.
  Identity, signatures, modifier flags, attributes, docs, and inheritance links
  remain available. Existing search/list detail modes remain independent of the
  user's response format selection.

Symbol-resolution failures use stable codes such as `type_not_found`,
`member_not_found`, `ambiguous_member`, `wrong_symbol_kind`, `invalid_member_id`,
and `no_assembly_loaded`.

## Routing Model

The repository currently supports both the workspace model and the older single-context fallback. New work should target the workspace-aware path.

### ToolSessionRouter

`ToolSessionRouter` is the only routing layer tools should use.

Rules:
- discovery or search tools with no `memberId` use `GetForContext(contextAlias)`;
- follow-up tools that take `memberId` use `GetForMember(memberId, contextAlias)`;
- callers dispose the returned `ToolSessionView` with `using` so its workspace lease spans the full tool operation;
- explicit `contextAlias` on a member-based tool wins over MVID routing;
- without an explicit alias, canonical member IDs route only by their MVID; an unknown MVID returns `context_not_loaded` with an activation/loading hint. Human names use the current alias. Source/IL reject dependency entities in a consuming context with `member_context_mismatch` before interpreting any token.

### ServiceLocator

`ServiceLocator` bridges static MCP tool methods to DI-managed services.

Important behavior:
- production uses the global provider;
- tests can override the provider thread-locally;
- legacy singleton services remain available for the single-context fallback;
- workspace tools obtain session-owned services through `ToolSessionRouter` leases rather than naked `ServiceLocator` references.

## Workspace and Alias Workflow

The intended workflow is to keep multiple assemblies loaded and address them by alias.

Operational rules:
- `load_assembly` loads or replaces one alias;
- omitted aliases normalize to the default alias `default`;
- `makeCurrent` controls whether the loaded alias becomes the current one;
- `list_contexts` reports resident contexts, registered-alias count, and current alias. Compact output omits registry history by default; `includeRegistered` enables filtered `limit`/`cursor` paging. Extended output includes the first registry page by default;
- `select_context` changes the default alias and activates a registered alias if necessary;
- `unload` can unload one alias or all aliases, and removes persisted registrations by default;
- `unload(..., preserveRegistration: true)` keeps on-demand registrations while unloading memory;
- `status` reports current alias plus loaded contexts when the workspace is active;
- `get_server_stats(contextAlias)` reports detailed cache, index, performance, resident-limit, lease, eviction, and reload diagnostics for one alias or the current alias.

Startup behavior:
- `WorkspaceBootstrapService` registers persisted aliases without opening assemblies;
- startup logs one registration summary;
- the current alias and explicitly requested `contextAlias` values load on first use;
- MVID routing applies to contexts loaded in the current server process. After a restart, callers using an older member ID for a non-current deferred alias must pass its `contextAlias` once to activate the owning context.

Eviction behavior:
- use and lease completion update recency;
- replacement inputs pass a lightweight PE/metadata preflight before any resident session is displaced;
- the oldest unleased session is disposed before a replacement is created, so the resident count never crosses the configured limit;
- if full session construction still fails, the displaced context is immediately rebuilt from its activation request and settings without crossing the limit;
- persistent loads write the prospective registry state before committing the resident swap; a persistence failure disposes the replacement and rebuilds the displaced context;
- current selection does not pin a session and may point to a registered but currently unloaded alias;
- runtime MVID routing remains available after eviction, while the documented post-restart limitation still applies;
- comparisons acquire both context leases for their full operation;
- unloading or replacing an actively leased alias returns `context_busy`.

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

`get_il` supports the same `limit`/`cursor` paging pattern for instruction lists and also accepts `startOffset`/`endOffset` windows when callers need a byte-offset slice around a suspected anchor.

### Member-ID Follow-Up Flow

Once a discovery tool returns a `memberId`, the caller should be able to use follow-up tools without resupplying the alias. That behavior depends on the MVID prefix and must remain reliable.

### Symbol Exploration Flow

Unknown-assembly exploration should stay inside MCP tools:
- use `search_symbols` when the caller has a fragment or is unsure whether a name is a type or member;
- use `resolve_member_id` first for fully-qualified or XML-doc-like guessed symbols such as `Namespace.Type.Member` or `M:Namespace.Type.Member`;
- use `search_types` for type-only discovery and `search_members` for member-only discovery;
- use `list_members` or `get_members_of_type` after a type is found;
- if a member-based tool receives a stale or human-entered symbol, return structured candidates and suggested next tool calls rather than only `Invalid member ID`.
- if `search_symbols` receives `Type.MissingMember` and the type resolves, return a diagnostic plus the type and direct members instead of an empty success.

### MCP Server Instructions

`Program.ServerInstructions` is intentionally short and workflow-oriented.

It should:
- steer clients toward `search_symbols`, `resolve_member_id`, `list_members`, structured errors, and common parameter names;
- complement the tool schemas rather than duplicate the full README or tool reference;
- stay concise enough to be useful in the MCP handshake.

If the workflow changes, update `Program.ServerInstructions`, the Codex skill, and this section together.

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
- member compare reports qualified metadata declarations plus `signatureChanged`. Comparison declarations include accessibility/modifiers, qualified types, accessors, generic constraints, and parameter defaults, independently of short discovery signatures. Ambiguous method names require parameter types or a canonical ID.

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
./scripts/verify.py
```

After documentation cleanup or API reshaping, also run a reference sweep:

```bash
rg -n "HELPER_METHODS_GUIDE|TESTING\\.md|MULTI_VERSION_WORKSPACE_PLAN|CommonImplementorGuide|ARCHITECTURE\\.md" .
```

Search services live in each session. Symbol summaries, reverse IL references, and
literal inventories are built once per context version and reused across queries.
Clearing all caches clears these indexes; settings/version changes invalidate them.
`get_ast_outline` type children use canonical member summaries, hide generated noise,
include events, and expose `limit`/`cursor` coverage. Depth zero omits children;
positive depth shows direct members, not recursive statement-level ASTs.

Expensive caches use byte-estimated LRU retention and entry caps. Raw and prepared
source each retain at most 32 MiB; original-source and signature caches each 16 MiB;
resolution and source-key caches each 2 MiB; search pages 8 MiB; symbol and reverse
reference indexes each 64 MiB; usage pages 16 MiB; literal queries 8 MiB and literal
inventory 16 MiB. An oversized result is returned completely without being retained.
Concurrent misses for a key share one factory; clearing a cache prevents an older
in-flight result from repopulating it. These are retention estimates, not process RSS
limits. Dependency images remain session-owned and prefetched; diagnostics include
resolved image bytes and retained search/analysis estimates.
