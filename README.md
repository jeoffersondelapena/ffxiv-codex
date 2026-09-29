# Codex

A private Dalamud plugin for FFXIV: which Blue Mage spell or Beastmaster beast to get next.

- `/codex` opens the window: the Blue Magic Spellbook and the Master's Bestiary grouped by level band (1-15, 16-30, then tens), lowest level first, each entry with its level and a check mark. Learned spells are read from the game; beasts are ticked by hand and remembered per character.
- "Unobtained Only" hides what is done. Open-world, FATE, levequest, quest, totem, Masked Carnivale and B-rank hunt sources are always shown; A/S-rank hunts, wanted targets, treasure maps, dungeons, trials, raids, guildhests and treasure dungeons are opt-in per list. Which source a row shows, and why, is the "Source order" section of `SPEC.md`.
- A band shows "Complete" when everything shown in it is done.
- Tapping an entry flags its source on the map. Each row's Go button switches to the list's job, teleports there through Lifestream and walks there through vnavmesh, waiting for the navmesh first; Map and Go are greyed with the reason when they cannot work. A source behind a quest (a Carnivale stage, a gourd, a quest enemy not yet taken) sends you to the quest giver instead and says why.

## Data

`data/codex-data.json` is generated: the wiki's Blue Magic Spellbook and Master's Bestiary pages, scraped by `data/pipeline/` (moved from the log tracker), plus each spell's unlock link and each location's map data from the game tables (XIVAPI). Refresh after a patch:

```bash
python3 data/pipeline/run_all.py
```

The wiki page caches under `data/pipeline/` are ignored by git; only the compact output is committed. Nothing about a character is ever in this repository: per-character files live in the plugin's own settings folder.

## Tests

`dotnet test Codex.Tests/Codex.Tests.csproj` runs the suite before every commit: the category rules (B-rank hunts always shown, instances opt-in), the level bands, the map-coordinate maths, and the per-character state file. The test project compiles the plugin's pure-logic files directly, because the plugin assembly cannot be referenced on a Mac.

## Build

Same shape as XIV Doctor: .NET 10 SDK, Dalamud dev assemblies at the XIV on Mac `dalamud/Hooks/dev` path, `dotnet build Codex/Codex.csproj -c Release`; the output in `out/` is loaded as a Dalamud dev plugin.
