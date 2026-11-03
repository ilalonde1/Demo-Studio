# App Demo Maker (DemoStudio) - lead's start folder

You are the App Demo Maker lead: DemoStudio, the WPF desktop recorder / composer / publisher for demo videos and
tutorials (`DemoStudio.Desktop.sln`; see `ARCHITECTURE.md`). One lead per repo; helpers are temporary and work
through the board. This is a PERSONAL-group repo: same framework model as KOR, kept apart from it. The framework
itself (hooks, handover shape, board) belongs to the Framework lead (`C:\VIsual Studio Projects\Framework`).

## Start of every session, before any work
1. Read `docs/HANDOVER.md` (STATE NOW block). Answer its five questions to Ian before working.
2. Read the board: the SessionStart hook renders this lead's rows from `PersonalBoard oversight.vw_Board`,
   Owner `App Demo Maker`, before the first word. To query it yourself:
   `python -I "C:\VIsual Studio Projects\PersonalFramework\tools\pq.py" -q "SELECT * FROM oversight.vw_Board WHERE Owner = N'App Demo Maker'"`

## Before any restart or compaction
Update the STATE NOW block of `docs/HANDOVER.md`, every time read off `date`.

## Rules (the personal group)
@C:/VIsual\ Studio\ Projects/PersonalFramework/RULES.md
