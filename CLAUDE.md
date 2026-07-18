# AfterAll — Claude Code Project Guide

Mobile-first Backrooms horror, run-based with mid-run augment picks. Unity **6000.5.0f1**, **URP Forward+**, C#. Solo dev (Harun / Hedaron), Phase 1 (ship a small game on itch.io). Game code lives under `Assets/_AfterAll/`.

> This file is read every session — keep it short. Session history/tasks live in the vault, not here. Run `/standup` to see where things stand.

## Code standards (non-negotiable)

- **Input:** New Input System only (`UnityEngine.InputSystem`) via `InputActionReference`; `Enable()` in `OnEnable`, `Disable()` in `OnDisable`. Never `UnityEngine.Input`.
- **Namespaces:** always (`namespace AfterAll.X`). Never name a namespace the same as a class inside it (inventory namespace is `AfterAll.Inventories`, class `Inventory` — avoids CS0118).
- **Fields:** `[SerializeField] private` for inspector exposure — never `public` fields.
- **Rendering:** URP-compatible only.
- **UI:** TextMeshPro (never legacy `UnityEngine.UI.Text`). One Canvas per screen under an `HUD` child; repeating elements use Horizontal/Vertical Layout Group + Layout Element — never hand-placed slot X/Y. Must survive different aspect ratios (mobile-first).
- **Naming:** PascalCase methods/classes, `_camelCase` private fields.
- **Refs:** prefer `[SerializeField]` / events over `FindFirstObjectByType` in production (OK for quick prototype, refactor before ship).
- **Rule of thumb:** goal first, then the industry-standard way — no quick hacks / legacy patterns when a modern Unity API exists.

## Proc-gen contract — do NOT rewrite

Rooms are **hand-crafted Blender prefabs**; code only **places and sockets** them (no mesh generation). Keep this contract intact:

- `RoomFootprint` (baked SO: XZ AABB + wall list) — the planner reads only this.
- `WallGapController` — splits `WallLeft`/`WallRight`, opens a gap, creates a `RoomSocket` at gap center.
- `RoomSocket` — **yaw-only cardinal snap** (0/90/180/270); contract = direction + wall-name tag + wallIndex.
- Planner (`PaintGrowthPlanner.cs`, class *Hub-Centric ClusterSpine*) outputs a pure-data `LayoutPlan` (placements + wall-name connections + gap offsets) — **no Instantiate**. `RoomPoolSpawner` applies it at Play.
- Connections: both walls `doorValid` + unused; `AreOpeningsPairable` (±0.35m); no AABB overlap (0.2m inset).

Silhouette targets (measured by `LayoutSilhouetteMetrics`, not auto-enforced): aspect≤2.5, fill≥35%, cluster≥0.20 at Rooms≈20.

## Language & session hygiene

- **Chat:** Harun writes Turkish; reply in Turkish or mixed TR/EN (both fine). **Everything outside chat is English** — code, comments, commit messages, branch names, vault/research notes, file names.
- **Context hygiene:** if the conversation is getting long or token-wasteful, proactively say so and offer: (1) a paste-ready handoff prompt for a fresh chat, (2) which model fits the next step (research/writing → cheaper model; heavy coding → stronger model). Don't wait to be asked.
- **Vault edits:** Claude may add/update/delete vault notes (`C:\Users\Harun\Documents\.Harun`) when it finds noteworthy info or spots outdated content — keep edits small, dated, and in English.

## Working style

- Keep tasks small and specific. When Harun is indecisive, make the call — don't hand him 5 options.
- **Harun owns visuals** — do not implement shaders, materials, lighting, mesh generation, or art-facing proc-gen geometry. Advise only. Code owns placement / streaming / spawn / gameplay logic.
- **End every working session with a git commit + push.** Message format: `verb - what changed`.

## Unity MCP

MCP For Unity (v6) bridges this repo to the running Unity Editor. After any script change, always `read_console` to catch compile errors before using the new types. Bridge only works while the Editor is open.

- **Claude is slow over MCP; Harun is fast in the Editor.** Route work by who's actually faster at it, not by who technically can do it. Quick logic/data checks (e.g. running a planner seed loop via `execute_code`) — fine to do via MCP. Anything that means building/wiring up scene objects, prefab hierarchies, or GameObject trees (new colliders, button meshes, hierarchy restructuring) — describe the exact steps and hand it to Harun instead of doing it live over MCP.
- If genuinely unsure which side a task belongs on, ask instead of defaulting to doing it over MCP.

## Vault (personal notes, outside this repo)

`C:\Users\Harun\Documents\.Harun` — `Goals Dashboard.md` (current status = source of truth), `Daily Notes/YYYY-MM-DD.md` (raw log), `Game Dev Journey/Games/AfterAll — AI Context.md` (project context), `Game Dev Journey/Games/AfterAll — Core Design.md` (living gameplay/story design doc — read it when brainstorming or implementing core systems, append decisions with dates). Use `/standup` to read them; don't read the whole vault.
