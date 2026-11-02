# App Demo Maker - HANDOVER

## STATE NOW - 2 Nov 2026 16:09 BCDT (`date`), written by a Framework helper at enrollment. Read first.

**The App Demo Maker lead has not run a session under this handover yet.** At its first close it replaces this
block with its own state: the work in hand, decisions, traps found. Until then, these are the verified facts:

- **What it is:** DemoStudio, a WPF desktop recorder / composer / publisher for demo videos and tutorials (browser
  capture, demo-step synthesis, narration, HTML tutorial export). Solution `DemoStudio.Desktop.sln`, projects under
  `src/` and `tests/`, structure in `ARCHITECTURE.md`, open remediation plan in `REMEDIATION.md`.
- **Git, read 16:09:** branch `develop`, last commit before enrollment 439a6ec (1 Aug 2026, "Work-in-progress
  sweep: commit outstanding changes on develop"), level with the local `origin/develop` ref (not fetched today).
  The only remote is `origin` = GitHub `ilalonde1/Demo-Studio`; there is no KOR-FS01 remote, so the framework
  enrollment commit is local only until Ian pushes it.
- **Other branches:** `main` (= 439a6ec), `feature/capture-stability-fix`, `recovery/working-recording-now` are level
  with origin. `recovery/known-good-window-capture` (bf1b4ba, 8 Mar 2026) exists only locally - no origin copy.
- **Board:** no rows owned by this repo yet (row 146, Owner Framework, is this enrollment).
- **Framework:** enrolled 2 Nov - start routine in `CLAUDE.md`, 25 guard hooks in `.claude/settings.json`
  (copied from KOR.Drafter, less its Drafter-only plan-doc check), this handover. Further framework pieces (goal/done-when/budget, review before merge,
  stage gates, scorecard) arrive from the Framework lead.

## Five questions to answer before any work
1. Where do things stand (the STATE NOW block, the board, the branches)?
2. What is next, in order? Not recorded at enrollment - that is the repo owner's (Ian's) call; `REMEDIATION.md` is
   the last written plan, not a confirmed order. Ask before starting.
3. Three traps: (a) build output is partly tracked in git (`_isolated_obj/`, `artifacts/`, `docs/_generated/`) -
   commit only named paths, never `git add -A`; (b) `docs/_generated/` is produced by
   `scripts/update-architecture-docs.ps1` and CI fails if it drifts (`scripts/check-architecture-docs.ps1`) -
   regenerate, do not hand-edit; (c) `.claude/settings.local.json` is Ian's own permissions file - never overwrite
   it; the hooks live in `.claude/settings.json`. And always: never the Anthropic API - everything in-session.
4. Who owns what: you (this repo's lead) own DemoStudio; the Framework lead owns the framework (hooks, handover
   shape, board); Codex works only on bounded tasks issued by a lead; Ian decides pushes to GitHub and any change to
   live systems.
5. What must you not touch: other repos and other leads' work; the local-only `recovery/known-good-window-capture`
   branch (no other copy exists); GitHub pushes and live systems without Ian's yes.
