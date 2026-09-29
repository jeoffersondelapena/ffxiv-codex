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
GOURD_CACHE = os.path.join("gamedata", "kornago_gourds.json")
WIKI = "https://ffxiv.consolegameswiki.com/mediawiki/api.php"
# stage -> the blue mage job quest that opens it (the first 25 come with the Carnivale itself)
CARNIVALE_UNLOCK = [(25, "The Real Folk Blues"), (30, "Blue Scream of Death"), (31, "Master of Mimicry"), (32, "A New Gold Standard")]


def gourd_costs():
    """gourd name -> cost, from the wiki's Kornago Gourd table"""
    if os.path.exists(GOURD_CACHE):
        return json.load(open(GOURD_CACHE))
    q = urllib.parse.urlencode({"action": "parse", "page": "Kornago Gourd", "prop": "wikitext", "format": "json", "redirects": 1})
    wt = json.load(urllib.request.urlopen(urllib.request.Request(WIKI + "?" + q, headers={"User-Agent": "ffxiv-codex/1.0"}), timeout=60))["parse"]["wikitext"]["*"]
    costs = {}
    for line in wt.splitlines():
        line = re.sub(r"\{\{i\|([^}|]*)[^}]*\}\}", r"\1", line)
        m = re.match(r"\|\s*(.+? Gourd)\s*\|\|.*?\|\|\s*(.+?)\s*$", line)
        if m:
            costs[m.group(1)] = m.group(2)
    json.dump(costs, open(GOURD_CACHE, "w"), indent=1)
    return costs


def wiki_links(data):
    """each entry's own wiki page, from the links the spellbook and bestiary tables carry"""
    base = "https://ffxiv.consolegameswiki.com"
    by_title, by_name = {}, {}
    for kind, name, href in json.load(open("page_urls.json")):
        title = urllib.parse.unquote(href.split("/wiki/", 1)[1]).replace("_", " ")
        by_title[(kind, title.lower())] = base + href
        if name:
            by_name[(kind, name.lower())] = base + href
    for lst, kind in (("blu", "spell"), ("bst", "beast")):
        for e in data[lst]:
            key = e["name"].lower()
            e["wiki"] = by_name.get((kind, key)) or by_title.get((kind, key)) or base + "/wiki/" + urllib.parse.quote(e["name"].replace(" ", "_"))


QUEST_CACHE = os.path.join("gamedata", "quest_givers.json")


def quest_giver(name, cache):
    """Where the quest is taken: the issuer NPC's name and map spot, from the Quest, ENpcResident and Level sheets."""
    if name in cache:
        return cache[name]
    found = None
    q = urllib.parse.quote(f'Name="{name}"')
    fields = "Name,IssuerStart,IssuerLocation.X,IssuerLocation.Y,IssuerLocation.Z,IssuerLocation.Territory,IssuerLocation.Map"
    for row in get(f"search?sheets=Quest&query={q}&limit=5&fields={fields}").get("results", []):
        f = row["fields"]
        at = f.get("IssuerLocation") or {}
        terr = (at.get("fields") or {}).get("Territory") or {}
        if not terr.get("row_id"):
            continue
        npc = (f.get("IssuerStart") or {}).get("row_id") or 0
        who = get(f"sheet/ENpcResident/{npc}?fields=Singular").get("fields", {}).get("Singular") if npc else None
        mp = (at["fields"].get("Map") or {})
        zone = get(f"sheet/TerritoryType/{terr['row_id']}?fields=PlaceName.Name").get("fields", {}).get("PlaceName", {}).get("fields", {}).get("Name")
        mf = get(f"sheet/Map/{mp['row_id']}?fields=SizeFactor,OffsetX,OffsetY").get("fields", {}) if mp.get("row_id") else {}
        size, offx, offy = mf.get("SizeFactor", 100), mf.get("OffsetX", 0), mf.get("OffsetY", 0)
        x, y, z = at["fields"]["X"], at["fields"]["Y"], at["fields"]["Z"]
        # the sheet writes names in lower case; capitalise each word, not each letter after an apostrophe
        found = {"name": " ".join(w[:1].upper() + w[1:] for w in (who or "the quest giver").split(" ")), "loc": zone, "xy": [world_to_map(x, size, offx), world_to_map(z, size, offy)],
                 "terr": terr["row_id"], "map": mp.get("row_id", 0), "size": size, "offX": offx, "offY": offy, "npc": npc, "world": [round(x, 2), round(y, 2), round(z, 2)],
                 "quest": name}
        break
    cache[name] = found
    return found


def quest_stops(data, cache):
    """A quest enemy is reached through its quest giver; a locked source through its unlock quest's giver."""
    for kind in ("blu", "bst"):
        for e in data[kind]:
            for s in e["sources"]:
                note = s.get("note") or ""
                if s["k"] == "questmob" and note.startswith("quest: ") and not s.get("via"):
                    giver = quest_giver(note[len("quest: "):], cache)
                    if giver:
                        s["via"] = dict(giver)
                if s.get("unlock"):
                    giver = quest_giver(s["unlock"], cache)
                    if giver:
                        s["unlockVia"] = dict(giver)


def page_text(path):
    html = open(path, encoding="utf-8", errors="replace").read()
    return re.sub(r"\s+", " ", re.sub(r"<[^>]+>", " ", html))


def leve_only_targets(data):
    """A Locations row for a mob that exists only inside a levequest reads like an open-world spawn (Arch Demon for
    Abyssal Transfixion). When the page's Levequests section names that mob and no enemy page shows a spawn for it,
    the source becomes the leve, with its levemete first."""
    import glob
    leves = {}
    for f in glob.glob("places/*.json"):
        wt = json.load(open(f)).get("wikitext", "")
        if "Levequest infobox" not in wt and "| type = Levequest" not in wt:
            continue
        title = json.load(open(f)).get("title") or os.path.basename(f)[:-5].replace("_", " ")
        field = lambda k: (re.search(r"\|\s*" + k + r"\s*=\s*([^\n|]*)", wt) or [None, ""])[1].strip()
        if field("quest-giver"):
            leves[title.replace("(L)", "").strip().lower()] = (title, field("quest-giver"), field("location"), field("location-x"), field("location-y"))
    changed = []
    for e in data["blu"]:
        path = os.path.join("pages/spells", e["name"].replace(" ", "_").replace("/", "_") + ".html")
        if not os.path.exists(path):
            continue
        text = page_text(path)
        i = text.find(" Levequests ")
        if i < 0:
            continue
        section = text[i:i + 1500]
        for m in re.finditer(r"([A-Z][^()]*?)(?: \(L\))? \(Lv\. (\d+)\), obtained in ([^-]+?) - ([A-Z][A-Za-z' -]+?)(?= [A-Z][a-z]+:| Tips| For |$)", section):
            leve, lv, zone, mob = m.group(1).strip(), int(m.group(2)), m.group(3).strip(), m.group(4).strip()
            # the first match starts right after the section heading
            leve = re.sub(r"^Levequests\s+", "", leve)
            key = leve.lower()
            for s in e["sources"]:
                if s["k"] != "world" or s["name"] != mob or s.get("via"):
                    continue
                if glob.glob("enemies/" + mob.replace(" ", "_") + "*.html"):
                    continue
                info = leves.get(key)
                s["k"] = "leve"
                s["leve"] = leve
                s["note"] = "levequest: " + leve
                s["lv"] = lv
                if info:
                    s["via"] = {"name": info[1], "loc": info[2] or zone, "xy": [float(info[3] or 0), float(info[4] or 0)]}
                changed.append((e["name"], mob, leve, info[1] if info else None))
    return changed


def reshape(data, costs):
    """Wiki columns that name a thing rather than a place: every gourd is sold by one merchant, every Carnivale stage is
    entered through one attendant, every totem comes from one vendor. Each gets the NPC to stand at and the quest that opens it."""
    for kind in ("blu", "bst"):
        for e in data[kind]:
            for s in e["sources"]:
                if s["k"] == "totem":
                    s["npcName"] = "Wayward Gaheel Ja"
                elif s["k"] == "carnivale":
                    stage = int((re.search(r"\d+", s.get("note") or "") or re.match(r"(?=0)", "0")).group(0) or 0)
                    if s.get("loc") and s["loc"] not in (s.get("note") or ""):
                        s["note"] = f"{s.get('note')}: {s['loc']}"
                    s["loc"] = "Ul'dah - Steps of Thal"
                    s["npcName"] = "Celestium attendant"
                    s["unlock"] = next(q for lim, q in CARNIVALE_UNLOCK if stage <= lim)
                elif s["k"] == "quest" and s.get("loc") == "Kornago Gourd":
                    s["k"] = "gourd"
                    s["loc"] = "Central Shroud"
                    s["npcName"] = "Kornago merchant"
                    s["unlock"] = "Into the Crucible"
                    cost = costs.get(s["name"])
                    s["note"] = (f"{cost} at the Kornago merchant" if cost else "Kornago merchant") + "; needs 'Into the Crucible' (level 30)"


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
        for row in get(f"search?sheets=Level&query={q2}&limit=5&fields=X,Y,Z,Territory.PlaceName.Name,Map.SizeFactor,Map.OffsetX,Map.OffsetY").get("results", []):
            f = row["fields"]
            terr = f.get("Territory") or {}
            if (terr.get("fields") or {}).get("PlaceName", {}).get("fields", {}).get("Name") != zone:
                continue
            m = (f.get("Map") or {}).get("fields") or {}
            size, offx, offy = m.get("SizeFactor", 100), m.get("OffsetX", 0), m.get("OffsetY", 0)
            found = {"xy": [world_to_map(f["X"], size, offx), world_to_map(f["Z"], size, offy)], "terr": terr["row_id"], "map": f["Map"]["row_id"],
                     "size": size, "offX": offx, "offY": offy, "npc": npc["row_id"], "world": [round(f["X"], 2), round(f["Y"], 2), round(f["Z"], 2)]}
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
    reshape(data, gourd_costs())
    for name, mob, leve, giver in leve_only_targets(data):
        print(f"leve-only target: {name}: {mob} -> '{leve}' via {giver}")
    wiki_links(data)
    givers = json.load(open(QUEST_CACHE)) if os.path.exists(QUEST_CACHE) else {}
    quest_stops(data, givers)
    json.dump(givers, open(QUEST_CACHE, "w"), indent=1)
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
                    # an NPC has an exact game position (with its height); the wiki's numbers are rounded to whole units
                    who = spot.get("npcName") if spot is s else spot.get("name")
                    if who and spot.get("loc"):
                        pos = npc_position(who, spot["loc"], npcs)
                        if pos: spot.update(pos)
    json.dump(npcs, open(NPC_CACHE, "w"), indent=1)
    out = {"schema": 1, "built": __import__("datetime").date.today().isoformat(), "source": "ffxiv.consolegameswiki.com + XIVAPI", "blu": data["blu"], "bst": data["bst"]}
    json.dump(out, open("../codex-data.json", "w"), ensure_ascii=False, separators=(",", ":"))
    print(f"codex-data.json: {len(out['blu'])} spells ({sum(1 for e in out['blu'] if e.get('unlockLink'))} with unlock links), {len(out['bst'])} beasts, zones resolved {sum(1 for z in zones.values() if z)}/{len(zones)}; unresolved: {sorted(missing)[:8]}")

if __name__ == "__main__":
    main()
