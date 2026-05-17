# DecompilerServer Copilot Instructions

Read these files first:
- `ARCHITECTURE.md` for durable technical rules and contracts
- `README.md` for user-facing workflow
- `TODO.md` for backlog

## Repository-Specific Rules

- MCP tools live as static methods under `Tools/`.
- Tools should return via `ResponseFormatter.TryExecute(...)`.
- Discovery/search tools should route with `ToolSessionRouter.GetForContext(...)`.
- `memberId` follow-up tools should route with `ToolSessionRouter.GetForMember(...)`.
- Reuse `TypeSurfaceComparer` for type-surface semantics instead of duplicating compare logic.
- Use `DecompilerService.DecompileEntitySnippet(...)` for focused compare body retrieval.
- Prefer structured JSON output over pre-rendered diff text for overview commands.
- For unknown foreign code, start with `search_symbols` for fragments. For fully-qualified or XML-doc-like guessed symbols, start with `resolve_member_id` so structured `member_not_found` diagnostics can resolve the type and suggest candidates.
- After a type resolves, use `list_members`/`get_members_of_type`, then source or caller/callee tools. Do not fall back to shell scans after one bad member guess unless MCP diagnostics are exhausted.

## Workspace and Compare Expectations

- Assume multi-context workspace support is the primary model.
- Preserve stable member ID format: `<mvid-32hex>:<token-8hex>:<kind-code>`.
- `compare_contexts` is structural. `changed` means direct member surface changed.
- `compare_symbols(compareMode: "body")` is method-only and should stay opt-in.
- Do not broaden compare semantics casually; update `ARCHITECTURE.md` if a durable contract changes.

## Testing

- Use xUnit and real compiled test assemblies.
- `Tests/ServiceTestBase.cs` is the base fixture for service-level tests.
- Add dedicated `*ToolTests.cs` files for MCP tool behavior.
- Use `Tests/TemporaryAssemblyBuilder.cs` for controlled version-drift and compare tests.
- When tests need workspace-aware behavior, register `DecompilerWorkspace` in the test service provider.

## Verification

Run after code changes:

```bash
dotnet format DecompilerServer.sln
dotnet test -c Release
```

Use `dotnet test -c Release --no-restore` only after restore/build assets already exist.

## Documentation Policy

- Keep durable technical content in `ARCHITECTURE.md`.
- Keep `README.md` user-facing.
- Keep `TODO.md` backlog-focused.
- Do not add new helper, testing, or plan documents unless there is a durable need that cannot fit those files.
