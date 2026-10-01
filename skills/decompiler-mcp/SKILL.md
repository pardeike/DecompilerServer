---
name: decompiler-mcp
description: Use when working with the DecompilerServer MCP server to inspect, search, decompile, analyze, or compare .NET assemblies, especially foreign code such as Unity/RimWorld assemblies, third-party DLLs, game binaries, or version-to-version assembly diffs. Prefer this skill when choosing the right DecompilerServer tool sequence and avoiding premature shell fallbacks.
---

# DecompilerServer MCP

Use DecompilerServer as the primary source of truth for loaded .NET assemblies. Do not guess member IDs or fall back to shell scans after one failed symbol lookup; use the discovery and diagnostic tools first.

## Default Workflow

1. Load or confirm context:
   - `load_assembly` with `assemblyPath`, or `gameDir` plus `assemblyFile` for Unity layouts.
   - `status` for readiness and the current context. Use `list_contexts(includeRegistered: true, limit, cursor, filter)` only to discover registered aliases. Passing `contextAlias` activates a registered alias on demand.
   - Use `get_server_stats` only when cache, index, or performance diagnostics matter.
2. Discover symbols:
   - Use `search_symbols` first for fragments or when unsure whether a name is a type or member.
   - Use `resolve_member_id` first for fully-qualified or XML-doc-like guesses such as `Namespace.Type.Member`, `Namespace.Type:Member`, or `M:Namespace.Type.Member`.
   - Use `search_types` when looking only for types.
   - Use `search_members` when looking only for methods, fields, properties, or events.
3. Inspect type surface:
   - Use `list_members` after resolving a type (`get_members_of_type` is its compatibility alias).
   - Prefer `mode: "signatures"` for orientation and `mode: "full"` only when extra metadata is needed.
   - Generated members and accessors are hidden by default; use `includeCompilerGenerated: true` when those are the subject of inspection.
4. Read code:
   - Use `get_decompiled_source` for complete focused source.
   - Use `plan_chunking` and `get_source_slice` for large types or methods.
   - Batch source defaults to 50 lines per member (`maxLinesPerMember`). Partial entries have `hasMore` and `nextStartLine`; continue with a source slice. Outer `truncated` includes partial members; `budgetTruncated` specifically means the total character cap was reached.
5. Analyze relationships:
   - Use `find_callers`, `find_callees`, and `find_usages` for call/use questions.
   - Callers/usages default to `scope: "assembly"`; use `scope: "workspace"` for all currently loaded contexts. Inspect `searchedContexts`; deferred aliases are not activated by that query.
   - Callees link exact members in other loaded contexts with `resolution: "workspace"`; external targets retain referenced assembly identity.
   - Use `find_base_types`, `find_derived_types`, `get_overrides`, and `get_implementations` for inheritance questions.
   - Type metadata outlines have addressable child IDs and `limit`/`cursor` paging; `maxDepth: 0` omits children, and larger values show the direct member level.
   - Use `get_il` before proposing transpiler anchors; `suggest_transpiler_targets` should be treated as a real-IL hint list, not a substitute for reading IL.
6. Compare versions:
   - Use `compare_contexts` for structural alias-level overview.
   - Use `compare_symbols` for type/member drill-down.
   - Use `compare_symbols` with `compareMode: "body"` only for method bodies.

## Recovery Rules

- If it returns `context_not_loaded` or `member_context_mismatch`, follow the assembly-loading hint and retry the canonical ID in its owning context. Do not search the MVID or kind suffix as a symbol.
- If it returns `ambiguous_member`, choose a candidate ID or supply the parameter types.
- If a member-based tool returns `type_not_found`, call `search_types` or `search_symbols` with the type fragment.
- If it returns `member_not_found`, inspect `error.details.candidates` and call the suggested `get_members_of_type` or `search_symbols`.
- If `search_symbols` returns `diagnostic.code: "member_guess_unresolved"`, the type resolved but the member guess did not; inspect the returned direct members or call the suggested `list_members`.
- If it returns `wrong_symbol_kind`, switch to the tool for the actual kind instead of retrying the same call.
- If a `memberId` contains an MVID, follow-up calls normally do not need `contextAlias`.
- Use explicit `contextAlias` when working from human-entered symbols or when multiple versions are loaded and no canonical `memberId` has been resolved yet.

## Tool Choice Bias

- Output is compact by default. Complete source is in `data.code`; omitted source language means C#. Source slices retain `startLine`, `endLine`, and `totalLines`. Use structured IL `instructions`, and use `error.details`/`error.hints` for recovery. Ordinary member summaries identify symbols with `declaringType` + `name`; type summaries retain `fullName`.
- Extended output is a user startup configuration (`DECOMPILER_RESPONSE_MODE=extended`), not a tool argument or an agent-controlled setting.

- `search_symbols` beats broad shell search for unknown names.
- `resolve_member_id` beats `search_symbols` for fully-qualified stale method guesses because its errors return typed candidates.
- `list_members` beats guessing conventional method names.
- `get_source_slice` beats dumping huge source into context when line ranges are enough.
- `get_il` beats external IL tools for first-pass opcode inspection; use `limit`/`cursor` or `startOffset`/`endOffset` for large methods.
- `find_callees` returns callee-shaped fields. Prefer `targetMemberId`, `symbol`, `opcode`, `offset`, and `resolution` over legacy `inMember`/`inType` aliases.
- Shell tools are a last resort for files outside loaded assemblies or for validating packaging/runtime environment, not for ordinary symbol exploration.
