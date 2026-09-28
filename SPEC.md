# Codex — spec, decisions, acceptance criteria

_Last updated: 2026-09-28. Read this first after any context compaction._

## What it is
A private Dalamud plugin, **Codex** (InternalName `Codex`, command `/codex`), for FFXIV on the user's Mac (XIV on Mac). It shows which Blue Mage spell and which Beastmaster beast to get next, per level band, with check marks for what the logged-in character already has, and helps go there. Public repository `jeoffersondelapena/ffxiv-codex` (to be created; user approved). Built like `~/Projects/overlay-doctor` (Dalamud.NET.Sdk 15, net10.0-windows, `~/.dotnet/dotnet`, DALAMUD_HOME = XIV on Mac `dalamud/Hooks/dev`), loaded as a dev plugin via `wedge-watch/register_dev_plugin.py`. Not part of the upstream-sync scheme (no upstream).

## Data
- Source: the wiki pages https://ffxiv.consolegameswiki.com/wiki/Blue_Magic_Spellbook and https://ffxiv.consolegameswiki.com/wiki/Master%27s_Bestiary, scraped by the pipeline already in `~/ffxiv-log-tracker` (`run_all.py` -> `data.json`: entries with id, name, minLv, sources[k, name, t, loc, xy, lv, lvMax, rank, note, rec]). The scraper and its compact output move into this repo; raw wiki page caches live under `data/cache/` in the project folder, git-ignored (wiki text is not ours to republish).
- Source kinds and rules copied from the tracker artifact: always shown: world, fate, leve, questmob, totem, quest, default, and **hunt with rank B**; opt-in, one checkbox each per list, in this order: Dungeons, Trials, Raids, Masked Carnivale, Guildhests, Treasure dungeons, A/S-rank hunts, Treasure maps. Labels Title Case.
- Bands: 1-15, 16-30, 31-40, 41-50, 51-60, 61-70, 71-80, 81-90, 91-100, by the entry's minLv; entries sorted by level within a band; the level is shown on every entry.
- Refresh: re-run the scraper (weekly via the hourly wedge-watch script, or on demand); the plugin re-reads the data file on load and on `/codex reload`.

## Game state
- Spells: automatic. Each AozAction's Action row has an UnlockLink; `UIState.Instance()->IsUnlockLinkUnlockedOrQuestCompleted(unlockLink, ...)` tells learned. Map AozAction -> Action -> UnlockLink at data-build time (XIVAPI) or at runtime from Lumina.
- Beasts: **manual ticks** for now. ClientStructs exposes only an opaque `ActionManager._beastmasterPets`; no tamed flag. Revisit when a bestiary structure is mapped.
- Per-character state file keyed by content id (never the name in the file name) in the plugin's config directory; two game windows write separate files; survives restarts. Nothing character-related ever enters the repository.

## UI
- Window per list (Blue Magic, Beasts): band groups, entries with level and check mark, "Unobtained Only" filter, the opt-in category checkboxes, a band shows complete when everything shown in it is done ("look at the next band"), search optional.
- Tap an entry: map flag at the source's coordinates (map link + flag), zone named. Sources without coordinates show the location text.
- Travel toggle (off by default): teleport to the zone's nearest aetheryte via Lifestream IPC (`Lifestream.ExecuteCommand`, e.g. `tp <name>`), then walk with vnavmesh: wait for `vnavmesh.Nav.IsReady` (show progress from `Nav.BuildProgress`, like GatherBuddy Reborn), convert map coords to world X/Z (Map sheet scale/offset), `vnavmesh.Query.Mesh.PointOnFloor` for height, `vnavmesh.SimpleMove.PathfindAndMoveTo`. Best effort; never required.
- Logs: Information lines for meaningful events (data loaded, state saved, tap/travel actions, IPC missing), none per frame. Comments in code terse (the user's commit hook rejects explainer prose).

## Acceptance criteria
1. `/codex` opens the window; both lists render all entries from the data file grouped by band, sorted by level, level visible.
2. Learned spells show a check mark without user action; beasts can be ticked and unticked and the tick persists across a game restart.
3. Two windows (two characters) keep separate state; ticking on one never changes the other.
4. "Unobtained Only" hides done entries; category checkboxes match the artifact's rules (B-rank hunts always on).
5. A band whose shown entries are all done is marked complete.
6. Tapping an entry places a map flag at its coordinates and opens the map.
7. With the travel toggle on and Lifestream + vnavmesh present, tapping teleports and walks; with vnavmesh building, it waits and reports; without them, it degrades to the flag with a log line.
8. The repository contains no character data; per-character files are outside it; raw wiki caches are ignored.
9. Build passes on this Mac; registered as a dev plugin; loads with no errors in Dalamud's log.
10. `dotnet test Codex.Tests` passes; pure logic (Rules, MapMath, StateStore) stays free of Dalamud types so it remains testable.

## Status / next actions
- 2026-09-28: repo created and pushed; pipeline moved in; codex-data.json built (124 spells with unlock links, 50 beasts, 42/56 zones mapped); plugin v0.1 builds (window, bands, filters, automatic spell checks, manual beast ticks, map flag, travel toggle with navmesh wait) and is registered as a dev plugin. Data file renamed codex-data.json because the manifest Codex.json collides on a case-insensitive disk. Next: in-game test against the acceptance criteria; then wire the weekly data refresh.
