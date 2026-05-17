# DecompilerServer TODO

This file is the backlog, not the architecture reference.

Documentation policy:
- `README.md` is user-facing.
- `ARCHITECTURE.md` holds durable implementation and contract details.
- `TODO.md` tracks pending work only.

## Next

### Context-aware analysis

Goal:
- Make the server better at answering "how is this used?" and "what is this code doing?" instead of only returning isolated structural facts.

Candidate work:
- Add `get_usage_context` to include surrounding conditions, parameter flow, and local call-site context.
- Add `analyze_call_chains` for bounded multi-hop traversal.
- Add `find_patterns` for common code patterns such as singleton and observer.
- Add `get_semantic_summary` for higher-level behavior summaries derived from method bodies.
- Add `analyze_state` to track field and property mutations plus dependent state.

Notes:
- High effort.
- Likely spans multiple services rather than fitting cleanly into one new endpoint.

## Later

### Response modes

Goal:
- Let callers trade detail for cost when the default response is too verbose.

Candidate work:
- Extend the existing member-summary modes (`ids`, `discovery`, `signatures`, `full`) beyond search/list endpoints where they fit.
- Consider smaller output modes for `get_member_details` and `get_decompiled_source`.
- Keep the default behavior stable so existing callers do not silently lose information.

### Error reporting and diagnostics

Goal:
- Make failures easier to act on without forcing the caller to guess what went wrong.

Candidate work:
- Extend stable error codes beyond the current symbol-resolution and assembly-loaded cases.
- Include suggestions for more fuzzy misses where there is a clear likely match, using the symbol-exploration diagnostics as the model.
- Add a `validate_assembly` or equivalent health-check tool.
- Expand `get_server_stats` with diagnostic warnings where useful.

## Maintenance Rules

- Add items here only if they are still pending.
- Move durable design rules to `ARCHITECTURE.md`.
- Update `README.md` only for user-facing workflow or setup changes.
- Prefer a short backlog with clear outcomes over long explanatory review notes.
