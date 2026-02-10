# Skills Status

Skills are **not implemented in the current phase**.

The previous skill loader and instruction injection system was removed during the library-first simplification.

## Planned Future Direction

If skills are reintroduced, they should be implemented as a separate module with:

- explicit activation points
- deterministic loading rules
- testable integration with `AgentSession`

This keeps the core runtime small and stable while the base loop/tool/session architecture is finalized.
