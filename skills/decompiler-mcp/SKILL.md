---
name: decompiler-mcp
description: Use when working with the DecompilerServer MCP server to inspect, search, decompile, analyze, or compare .NET assemblies, especially foreign code such as Unity/RimWorld assemblies, third-party DLLs, game binaries, or version-to-version assembly diffs. Prefer this skill when choosing the right DecompilerServer tool sequence and avoiding premature shell fallbacks.
---

# DecompilerServer MCP

Use DecompilerServer as the primary source of truth for loaded .NET assemblies. Do not guess member IDs or fall back to shell scans after one failed symbol lookup; use the discovery and diagnostic tools first.

## Default Workflow

1. Load or confirm context:
   - `load_assembly` with `assemblyPath`, or `gameDir` plus `assemblyFile` for Unity layouts.
   - `list_contexts` or `status` to confirm aliases and current context.
   - Use `get_server_stats` only when cache, index, or performance diagnostics matter.
2. Discover symbols:
   - Use `search_symbols` first for fragments or when unsure whether a name is a type or member.
   - Use `resolve_member_id` first for fully-qualified or XML-doc-like guesses such as `Namespace.Type.Member`, `Namespace.Type:Member`, or `M:Namespace.Type.Member`.
   - Use `search_types` when looking only for types.
   - Use `search_members` when looking only for methods, fields, properties, or events.
3. Inspect type surface:
   - Use `list_members` or `get_members_of_type` after resolving a type.
   - Prefer `mode: "signatures"` for orientation and `mode: "full"` only when extra metadata is needed.
4. Read code:
   - Use `get_decompiled_source` for complete focused source.
   - Use `plan_chunking` and `get_source_slice` for large types or methods.
5. Analyze relationships:
   - Use `find_callers`, `find_callees`, and `find_usages` for call/use questions.
   - Use `find_base_types`, `find_derived_types`, `get_overrides`, and `get_implementations` for inheritance questions.
   - Use `get_il` before proposing transpiler anchors; `suggest_transpiler_targets` should be treated as a real-IL hint list, not a substitute for reading IL.
6. Compare versions:
   - Use `compare_contexts` for structural alias-level overview.
   - Use `compare_symbols` for type/member drill-down.
   - Use `compare_symbols` with `compareMode: "body"` only for method bodies.

## Recovery Rules

- If a member-based tool returns `type_not_found`, call `search_types` or `search_symbols` with the type fragment.
- If it returns `member_not_found`, inspect `error.details.candidates` and call the suggested `get_members_of_type` or `search_symbols`.
- If `search_symbols` returns `diagnostic.code: "member_guess_unresolved"`, the type resolved but the member guess did not; inspect the returned direct members or call the suggested `list_members`.
- If it returns `wrong_symbol_kind`, switch to the tool for the actual kind instead of retrying the same call.
- If a `memberId` contains an MVID, follow-up calls normally do not need `contextAlias`.
- Use explicit `contextAlias` when working from human-entered symbols or when multiple versions are loaded and no canonical `memberId` has been resolved yet.

## Tool Choice Bias

- `search_symbols` beats broad shell search for unknown names.
- `resolve_member_id` beats `search_symbols` for fully-qualified stale method guesses because its errors return typed candidates.
- `list_members` beats guessing conventional method names.
- `get_source_slice` beats dumping huge source into context when line ranges are enough.
- `get_il` beats external IL tools for first-pass opcode inspection; use `limit`/`cursor` or `startOffset`/`endOffset` for large methods.
- `find_callees` returns callee-shaped fields. Prefer `targetMemberId`, `symbol`, `opcode`, `offset`, and `resolution` over legacy `inMember`/`inType` aliases.
- Shell tools are a last resort for files outside loaded assemblies or for validating packaging/runtime environment, not for ordinary symbol exploration.
