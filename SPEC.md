# Codex — spec, decisions, acceptance criteria

_Last updated: 2026-09-28. Read this first after any context compaction._

## What it is
A private Dalamud plugin, **Codex** (InternalName `Codex`, command `/codex`), for FFXIV on the user's Mac (XIV on Mac). It shows which Blue Mage spell and which Beastmaster beast to get next, per level band, with check marks for what the logged-in character already has, and helps go there. Public repository `jeoffersondelapena/ffxiv-codex` (to be created; user approved). Built like `~/Projects/overlay-doctor` (Dalamud.NET.Sdk 15, net10.0-windows, `~/.dotnet/dotnet`, DALAMUD_HOME = XIV on Mac `dalamud/Hooks/dev`), loaded as a dev plugin via `wedge-watch/register_dev_plugin.py`. Not part of the upstream-sync scheme (no upstream).

## Data
- Source: the wiki pages https://ffxiv.consolegameswiki.com/wiki/Blue_Magic_Spellbook and https://ffxiv.consolegameswiki.com/wiki/Master%27s_Bestiary, scraped by the pipeline already in `~/ffxiv-log-tracker` (`run_all.py` -> `data.json`: entries with id, name, minLv, sources[k, name, t, loc, xy, lv, lvMax, rank, note, rec]). The scraper and its compact output move into this repo; raw wiki page caches live under `data/cache/` in the project folder, git-ignored (wiki text is not ours to republish).
- Source kinds come from the tracker artifact; which are always shown, which are opt-in and why is in "Source order" below. Labels Title Case.
- Bands: 1-15, 16-30, 31-40, 41-50, 51-60, 61-70, 71-80, 81-90, 91-100, by the entry's minLv; entries sorted by level within a band; the level is shown on every entry.
- Refresh: re-run the scraper (weekly via the hourly wedge-watch script, or on demand); the plugin re-reads the data file on load and on `/codex reload`.

## Game state
- Spells: automatic. Each AozAction's Action row has an UnlockLink; `UIState.Instance()->IsUnlockLinkUnlockedOrQuestCompleted(unlockLink, ...)` tells learned. Map AozAction -> Action -> UnlockLink at data-build time (XIVAPI) or at runtime from Lumina.
- Beasts: **manual ticks** for now. ClientStructs exposes only an opaque `ActionManager._beastmasterPets`; no tamed flag. Revisit when a bestiary structure is mapped.
- Per-character state file keyed by content id (never the name in the file name) in the plugin's config directory; two game windows write separate files; survives restarts. Nothing character-related ever enters the repository.

## UI
- Window per list (Blue Magic, Beasts): an overall line at the top ("N of T obtained, P%", floored, plus how many entries the Include filters hide), band groups, entries with level and check mark, "Unobtained Only" filter, the opt-in category checkboxes, a band shows complete when everything shown in it is done ("look at the next band"), search optional.
- A search box (name, enemy or place, like the tracker) and the tracker's sort options (by level, by number both ways, A to Z, Z to A) sit above the Include row. Ticking or unticking a beast asks for confirmation first, since those ticks are by hand.
- Each entry is driven by one source, chosen by the source order below, lowest level within the kind; that source sets the level and band, the flag and the trip. This departs from the tracker's lowest-level rule on purpose (2026-09-29): a rare level-1 wanted target must not outrank a plain open-world mob at a higher level. Previously: an entry sat at its lowest visible source, with a "min N" badge for the wiki's minimum level to obtain when above 1; entries whose level has no band land under "Level unknown". Hunt sources are labelled by rank ("B-rank hunt").
- Whalaqee totems point at Wayward Gaheel Ja (Ul'dah - Steps of Thal 12.5, 12.9); the trip is one Lifestream command, `tp Weavers' Guild`, which teleports to Ul'dah and takes the aethernet to that shard, then walks.
- Each row shows its number in the Blue Magic Spellbook or the Master's Bestiary; a "Group by band" toggle flattens the list; the tooltip explains what each source kind takes (levemete, FATE up, hunt timer, duty).
- Leve sources carry the levemete (the leve page's quest giver and coordinates) as a first stop. Which stop a tap takes is decided by the game: while the leve is not held (QuestManager's active leves, matched by name; any active leve for a wanted target), the tap goes to the levemete and names the leve and the allowances left; once held, to the enemy. After a levemete trip Codex watches for the acceptance for ten minutes and either says so or, with "Continue after a leve is accepted" on, travels on by itself.
- Travel names the entry in its status and chat lines; after arriving it retries the mount for up to 8 s, since the game refuses one right after a teleport.
- Tap an entry: map flag at the source's coordinates (map link + flag), zone named. Sources without coordinates show the location text.
- Travel toggle (off by default): teleport to the zone's nearest aetheryte via Lifestream IPC (`Lifestream.ExecuteCommand`, e.g. `tp <name>`), waiting while Lifestream is busy and failing fast when it never starts; for a stretch longer than 15 yalms mount up first (the mount picked in the window, Mount Roulette by default, like GatherBuddy Reborn) and fly where the zone's aether currents are complete; dismount on arrival; a source with a zone but no coordinates (roaming hunts) only teleports; then walk with vnavmesh: wait for `vnavmesh.Nav.IsReady` (show progress from `Nav.BuildProgress`, like GatherBuddy Reborn), convert map coords to world X/Z (Map sheet scale/offset), `vnavmesh.Query.Mesh.PointOnFloor` for height, `vnavmesh.SimpleMove.PathfindAndMoveTo`. Best effort; never required.
- Logs: Information lines for meaningful events (data loaded, state saved, tap/travel actions, IPC missing), none per frame. Comments in code terse (the user's commit hook rejects explainer prose).

## Source order

Why a row shows the source it shows. Read this before touching `Order`, `AlwaysOn` or `OptIn` in `Codex/Rules.cs`; the tests pin the order, so a change here is a change there too.

**Driver order.** The first kind in this list that an entry has, lowest level within it, drives the row: its level and band, the flag and the trip.

A. Sure and free: certain, and it comes without a trip.
1. Known from the start (`default`)
2. Quest reward (`quest`)
3. Totem (`totem`): a one-spell item unlocked by spell-count and Carnivale achievements; nothing is spent.
No entry has two of these, so the order inside the group never decides anything.

B. Free roll: a chance, but on a trip made once anyway, so the attempt costs nothing extra. Spent after that visit; the row then shows its next source.
4. Quest enemy (`questmob`): spent when the quest is complete, repeatable or not, since each quest is played once.
5. Masked Carnivale (`carnivale`): spent when the stage's clear flag is set, since stages are played until cleared and never replayed. The learn there is not guaranteed even though stages are synced.
No entry has both.

C. Grind: a chance, and it costs time or allowances. Ordered by what annoys least.
6. Open world (`world`): nothing spent, only spawn and cast waits.
7. Levequest (`leve`): an allowance per try, but no waiting. Spending beats waiting.
8. FATE (`fate`): wait for the spawn.
9. Hunt mark (`hunt`): wait for the timer and compete. B ranks wait less but still wait, so they share the slot.
10. Wanted target (`wanted`): luck whether it appears at all, plus an allowance per try.
11. Treasure map (`map`): a map item plus a lucky roll for the mob.

D. Party content: the one sure learn on a synced kill, but solo play makes it second last. Ordered by how hard it is to get in.
12. Dungeon (`dungeon`), 13. Trial (`trial`), 14. Raid (`raid`), 15. Guildhest (`guildhest`), 16. Treasure dungeon (`tdungeon`)

E. Location unknown (`unknown`): nowhere to send anyone. Last.

**Include row.** Which kinds count at all. Always on, no box: groups A and B, open world, levequest, FATE, and B-rank hunts. Opt-in, one box each, laid out in the driver order: A/S-rank hunts, Wanted targets, Treasure maps, Dungeons, Trials, Raids, Guildhests, Treasure dungeons, Location unknown. They are opt-in because they need a party or lean on luck with a cost attached, so they may never be wanted. B ranks stay on because the wait is short and nobody contests them. Every box has a hover tooltip; a list shows only the boxes for kinds it has.

**On top.** A spent or unincluded source never drives a row but stays in the tooltip, labelled "(quest done)", "(quest done, repeatable)", "(stage cleared)" or "(not included)". The tooltip lists every source in this order with the driver marked.

**How it got here (2026-09-29).** Settled after the in-game tests: leve above FATE because waiting annoys more than an allowance; quest rewards and the like first because they happen anyway; party content second last for a solo player; quest enemy and Carnivale moved into the free-roll group once both were treated as one visit each; wanted targets made opt-in because appearance is luck and each try costs an allowance; B ranks kept in the hunt slot because a short wait is still a wait; the Include row laid out in the driver order so the two never contradict.

## Acceptance criteria
1. `/codex` opens the window; both lists render all entries from the data file grouped by band, sorted by level, level visible.
2. Learned spells show a check mark without user action; beasts can be ticked and unticked and the tick persists across a game restart.
3. Two windows (two characters) keep separate state; ticking on one never changes the other.
4. "Unobtained Only" hides done entries; category checkboxes match the artifact's rules (B-rank hunts always on) and only the categories the list uses are offered; levels match the artifact's level filter.
5. A band whose shown entries are all done is marked complete.
6. Tapping an entry places a map flag at its coordinates and opens the map.
7. With the travel toggle on and Lifestream + vnavmesh present, tapping teleports and walks; with vnavmesh building, it waits and reports; without them, it degrades to the flag with a log line.
8. The repository contains no character data; per-character files are outside it; raw wiki caches are ignored.
9. Build passes on this Mac; registered as a dev plugin; loads with no errors in Dalamud's log.
10. `dotnet test Codex.Tests` passes; pure logic (Rules, MapMath, StateStore) stays free of Dalamud types so it remains testable.
11. With Travel on, a long stretch is ridden or flown on the chosen mount and the trip ends dismounted next to the target; a walk is only reported done once vnavmesh has finished or the target is within a few yalms.
12. Search narrows the bands to matching entries; the sort dropdown reorders within bands; a beast tick only changes after the confirmation popup.

## Status / next actions
- 2026-09-28: repo created and pushed; pipeline moved in; codex-data.json built (124 spells with unlock links, 50 beasts, 42/56 zones mapped); plugin v0.1 builds (window, bands, filters, automatic spell checks, manual beast ticks, map flag, travel toggle with navmesh wait) and is registered as a dev plugin. Data file renamed codex-data.json because the manifest Codex.json collides on a case-insensitive disk. Next: in-game test against the acceptance criteria. (Weekly data refresh wired on 2026-09-29 into the hourly wedge-watch tick, which rebuilds data/codex-data.json and copies it to the plugin's config folder.)
- 2026-09-28 later: overall progress line per tab (user request); the private tracker artifact link was found in this file by the push guard and purged with a history rewrite (the repo is public; links to private artifacts stay out of it).
- 2026-09-29 second test: Bristle walked because the mount was refused in the arrival second (retry added); Lifestream never took the Ul'dah aethernet leg (now one `tp <shard>` command); numbers, band toggle, hints and named travel status added on request.
- 2026-09-28: the tracker's 30 beast ticks for the first character were written straight into that character's state file (game closed); no import feature kept. The spec file was emptied by a bad edit in one commit and restored from the previous one.
- 2026-09-28 first in-game test: levels were the wiki's minimum level (49 spells in 1-15 against the tracker's 16), the category row was the same for both lists, B-rank hunts were labelled "A/S-rank hunts", totems had no position, walks were declared done before vnavmesh started, overlapping teleports timed out, and everything was on foot. All addressed as above; mounts added.
