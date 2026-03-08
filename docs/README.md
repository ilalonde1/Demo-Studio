# Docs Index

- [Architecture Overview](./architecture-overview.md)
- [Runtime Flow](./runtime-flow.md)
- [Failure Handling](./failure-handling.md)
- [Generated ViewModel Boundaries](./_generated/viewmodel-boundaries.md)
- [Generated ViewModel Hotspots](./_generated/viewmodel-hotspots.md)

## Update Workflow
- Regenerate architecture docs:
  - `pwsh -NoLogo -NoProfile -File ./scripts/update-architecture-docs.ps1`
- Verify docs are in sync (CI-safe):
  - `pwsh -NoLogo -NoProfile -File ./scripts/check-architecture-docs.ps1`
