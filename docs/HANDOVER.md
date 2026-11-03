# App Demo Maker - HANDOVER

## STATE NOW - 2 Nov 2026 17:07 BCDT (`date`), written by a Framework helper. Read first.

**The App Demo Maker lead has not run a session under this handover yet.** At its first close it replaces this
block with its own state: the work in hand, decisions, traps found. Until then, these are the verified facts:

- **What it is:** DemoStudio, a WPF desktop recorder / composer / publisher for demo videos and tutorials (browser
  capture, demo-step synthesis, narration, HTML tutorial export). Solution `DemoStudio.Desktop.sln`, projects under
  `src/` and `tests/`, structure in `ARCHITECTURE.md`, open remediation plan in `REMEDIATION.md`.
- **Group:** a PERSONAL-group repo (Ian, 2 Nov 2026): same framework model as KOR, kept apart - it loads the
  personal rules (`C:\VIsual Studio Projects\PersonalFramework\RULES.md`), its board is PersonalBoard, and it has
  no KOR data, no KOR-only guards and no traffic with the KOR repos.
- **Git, read 17:06 after `git fetch origin`:** branch `develop`; the framework commits (enrollment 2a9b3ac, then
  the rework to the personal model) sit on top of 439a6ec (1 Aug 2026, "Work-in-progress sweep: commit outstanding
  changes on develop"), which is `origin/develop`. The only remote is `origin` = GitHub `ilalonde1/Demo-Studio`;
  there is no KOR-FS01 remote, so the framework commits are local only until Ian pushes them.
- **Other branches:** `main` (= 439a6ec), `feature/capture-stability-fix` (1b40116) and
  `recovery/working-recording-now` are level with origin. `recovery/known-good-window-capture` (bf1b4ba, 8 Mar 2026)
  has no branch of its own on GitHub, but its commit IS on GitHub: `git branch -r --contains bf1b4ba` returns
  `origin/feature/capture-stability-fix` (bf1b4ba is one commit behind its head 1b40116), and the GitHub tag
  `known-good-window-capture-2026-03-09` peels to bf1b4ba (`git ls-remote origin`). Both read 17:06.
- **Board:** PersonalBoard `oversight.vw_Board`, Owner `App Demo Maker`: 0 open rows at 17:06 (rendered by
  `board_render.py --board personal`). Row 6 on PersonalBoard (Owner Framework) is this rework.
- **Framework:** enrolled 2 Nov, reworked the same day to the personal model - start routine and the personal-rules
  import in `CLAUDE.md`; 23 hooks in `.claude/settings.json` (the KOR enrollment's 25, less the two
  `claude_look_guard.py` entries and the `netops.exe signin-status` entry, which read KOR data; plus the SessionStart
  board render); this handover. Further framework pieces (goal/done-when/budget, review before merge, stage gates,
  scorecard) arrive from the Framework lead.

## The board
`PersonalBoard` on KOR-APP01\SQLEXPRESS, view `oversight.vw_Board`, Owner `App Demo Maker`. Shown at every session
start by the SessionStart hook. Query it yourself with
`python -I "C:\VIsual Studio Projects\PersonalFramework\tools\pq.py" -q "..."` (password from the environment, never
on a command line).

## Five questions to answer before any work
1. Where do things stand (the STATE NOW block, the board, the branches)?
2. What is next, in order? Not recorded yet - that is the repo owner's (Ian's) call; `REMEDIATION.md` is the last
   written plan, not a confirmed order. Ask before starting.
3. Three traps: (a) build output is partly tracked in git (`_isolated_obj/`, `artifacts/`, `docs/_generated/`) -
   commit only named paths, never `git add -A`; (b) `docs/_generated/` is produced by
   `scripts/update-architecture-docs.ps1` and CI fails if it drifts (`scripts/check-architecture-docs.ps1`) -
   regenerate, do not hand-edit; (c) `.claude/settings.local.json` is Ian's own permissions file - never overwrite
   it; the hooks live in `.claude/settings.json`. And always: never the Anthropic API - everything in-session.
4. Who owns what: you (this repo's lead) own DemoStudio; the Framework lead owns the framework (hooks, handover
   shape, board) for both groups, personal and KOR; Codex works only on bounded tasks issued by a lead; Ian owns
   GitHub pushes and live systems.
5. What must you not touch: other repos and other leads' work; KOR data and stores (the KOR board included); the
   `recovery/known-good-window-capture` branch (its commit is on GitHub via `origin/feature/capture-stability-fix`
   and the tag, but the local branch name is the only branch named for it - leave it); GitHub pushes and live
   systems without Ian's yes.
