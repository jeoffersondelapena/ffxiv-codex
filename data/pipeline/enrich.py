"""Turn data.json (wiki) into ../codex.json: add each spell's unlock link and each located source's territory and map
(with the map's scale) from the game tables, so the plugin can check learned spells and place map flags."""
import json, os, re, urllib.parse, urllib.request
API = "https://v2.xivapi.com/api/"
here = os.path.dirname(os.path.abspath(__file__)); os.chdir(here)
CACHE = "gamedata"; os.makedirs(CACHE, exist_ok=True)

def get(path):
    url = API + path
    key = os.path.join(CACHE, re.sub(r"[^A-Za-z0-9]+", "_", path)[:150] + ".json")
    if os.path.exists(key): return json.load(open(key))
    data = json.load(urllib.request.urlopen(urllib.request.Request(url, headers={"User-Agent": "codex-pipeline/1"}), timeout=60))
    json.dump(data, open(key, "w")); return data

def walk(sheet, fields):
    after, out = 0, []
    while True:
        rows = get(f"sheet/{sheet}?limit=100&after={after}&fields={fields}").get("rows", [])
        if not rows: return out
        out += rows; after = rows[-1]["row_id"]

def spell_unlock_links():
    """spellbook number -> unlock link, via AozActionTransient (Number) -> AozAction row -> Action.UnlockLink"""
    transient = {r["row_id"]: r["fields"].get("Number") for r in walk("AozActionTransient", "Number")}
    aoz = {r["row_id"]: r["fields"] for r in walk("AozAction", "Action.row_id,Action.UnlockLink,Action.Name")}
    out = {}
    for row, number in transient.items():
        a = aoz.get(row, {}).get("Action") or {}
        link = (a.get("fields") or {}).get("UnlockLink")
        link = link.get("value") if isinstance(link, dict) else link
        if number and a.get("row_id"): out[int(number)] = {"unlockLink": int(link or 0), "actionId": int(a["row_id"]), "gameName": (a.get("fields") or {}).get("Name")}
    return out

def zone_lookup(name):
    q = urllib.parse.quote(f'PlaceName.Name="{name}"')
    res = get(f'search?sheets=TerritoryType&query={q}&fields=PlaceName.Name,Map.row_id,Map.SizeFactor,Map.OffsetX,Map.OffsetY,Bg,TerritoryIntendedUse').get("results", [])
    # the open-world instance of a zone: intended use 1 (overworld); fall back to the first row with a map
    res = [r for r in res if (r["fields"].get("Map") or {}).get("row_id")]
    res.sort(key=lambda r: 0 if (r["fields"].get("TerritoryIntendedUse") or {}).get("value", r["fields"].get("TerritoryIntendedUse")) == 1 else 1)
    if not res: return None
    f = res[0]["fields"]; m = f["Map"]
    return {"terr": res[0]["row_id"], "map": m["row_id"], "size": m["fields"].get("SizeFactor", 100), "offX": m["fields"].get("OffsetX", 0), "offY": m["fields"].get("OffsetY", 0)}

NPC_CACHE = os.path.join("gamedata", "npc_positions.json")


def world_to_map(v, size, off):
    c = size / 100.0
    return round(41.0 / c * ((v + off) * c + 1024.0) / 2048.0 + 1.0, 1)


def npc_position(name, zone, cache):
    """Map spot of a named NPC in a zone, from the ENpcResident and Level sheets; None when the data has no such NPC there."""
    key = f"{name}|{zone}"
    if key in cache:
        return cache[key]
    found = None
    q = urllib.parse.quote(f'Singular="{name}"')
    for npc in get(f"search?sheets=ENpcResident&query={q}&limit=20&fields=Singular").get("results", []):
        q2 = urllib.parse.quote(f"Object={npc['row_id']}")
        for row in get(f"search?sheets=Level&query={q2}&limit=5&fields=X,Z,Territory.PlaceName.Name,Map.SizeFactor,Map.OffsetX,Map.OffsetY").get("results", []):
            f = row["fields"]
            terr = f.get("Territory") or {}
            if (terr.get("fields") or {}).get("PlaceName", {}).get("fields", {}).get("Name") != zone:
                continue
            m = (f.get("Map") or {}).get("fields") or {}
            size, offx, offy = m.get("SizeFactor", 100), m.get("OffsetX", 0), m.get("OffsetY", 0)
            found = {"xy": [world_to_map(f["X"], size, offx), world_to_map(f["Z"], size, offy)], "terr": terr["row_id"], "map": f["Map"]["row_id"],
                     "size": size, "offX": offx, "offY": offy}
            break
        if found:
            break
    cache[key] = found
    return found


def main():
    data = json.load(open("data.json"))
    links = spell_unlock_links()
    zones, missing = {}, set()
    os.makedirs("gamedata", exist_ok=True)
    npcs = json.load(open(NPC_CACHE)) if os.path.exists(NPC_CACHE) else {}
    for kind in ("blu", "bst"):
        for e in data[kind]:
            if kind == "blu":
                e.update(links.get(e["id"], {}))
            for s in e["sources"]:
                for spot in [s] + ([s["via"]] if s.get("via") else []):
                    xy = spot.get("xy")
                    if isinstance(xy, str):
                        m = re.match(r"\s*([\d.]+)\s*,\s*([\d.]+)", xy); spot["xy"] = [float(m.group(1)), float(m.group(2))] if m else None
                    loc = spot.get("loc")
                    if spot.get("xy") and loc:
                        if loc not in zones: zones[loc] = zone_lookup(loc)
                        if zones[loc]: spot.update(zones[loc])
                        else: missing.add(loc)
                    # a levemete is an NPC with an exact game position; the wiki's numbers are rounded to whole units
                    if spot is not s and spot.get("name") and spot.get("loc"):
                        pos = npc_position(spot["name"], spot["loc"], npcs)
                        if pos: spot.update(pos)
    json.dump(npcs, open(NPC_CACHE, "w"), indent=1)
    out = {"schema": 1, "built": __import__("datetime").date.today().isoformat(), "source": "ffxiv.consolegameswiki.com + XIVAPI", "blu": data["blu"], "bst": data["bst"]}
    json.dump(out, open("../codex-data.json", "w"), ensure_ascii=False, separators=(",", ":"))
    print(f"codex-data.json: {len(out['blu'])} spells ({sum(1 for e in out['blu'] if e.get('unlockLink'))} with unlock links), {len(out['bst'])} beasts, zones resolved {sum(1 for z in zones.values() if z)}/{len(zones)}; unresolved: {sorted(missing)[:8]}")

if __name__ == "__main__":
    main()
