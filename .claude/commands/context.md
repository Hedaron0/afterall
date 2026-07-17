---
description: Regenerate the AfterAll — AI Context note from a live scan of the real project state
---

Regenerate `C:\Users\Harun\Documents\.Harun\Game Dev Journey\Games\AfterAll — AI Context.md` from the ACTUAL current project — do not just re-read and return the old note.

Live scan:
- `Assets/_AfterAll/Scripts/` — group by folder; **lead with whatever was modified most recently** (that's the active subsystem). One line per important script.
- `ProjectSettings/ProjectVersion.txt` — Unity version.
- `Assets/_AfterAll/Prefabs/` + `Assets/Scenes/` if relevant to the active task.
- Latest `Daily Notes/YYYY-MM-DD.md` and `Goals Dashboard.md` for current focus/blocker.
- If Unity Editor is open: optionally `read_console` for errors and note play-mode state.

Keep the note's existing section structure (Project paragraph, Tech stack, Architecture map, Current State: Working/Broken/In Progress, Design decisions, Prefab kit, Next steps). Rewrite the state + next-steps + timestamp; keep stable sections unless architecture actually changed. Keep it **paste-sized** — no long session logs inside. English only.

After writing, summarize in chat what changed vs the previous note.
