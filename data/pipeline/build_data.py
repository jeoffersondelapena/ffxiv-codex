import json, glob, re, sys, collections, urllib.parse
sys.path.insert(0,'./pylib')

INST={'dungeon','trial','raid','carnivale','guildhest','tdungeon'}
NOT_FOR_BLU={'The Diadem','Eureka Anemos','Eureka Pagos','Eureka Pyros','Eureka Hydatos','Bozjan Southern Front','Zadnor','The Forbidden Land, Eureka Anemos','Blue Sky'}
ICON_KIND={'Dungeon.png':'dungeon','Trials_icon1.png':'trial','Raid.png':'raid','The_masked_carnival_icon.png':'carnivale',
           'Levequest_icon.png':'leve','FATE-Path.png':'fate','Guildhest.png':'guildhest','Treasure_Dungeon.png':'tdungeon',
           'Boss_FATE_icon.png':'fate','Slay_enemy_FATE_icon.png':'fate','Defense_FATE_icon.png':'fate','Escort_FATE_icon.png':'fate','Collection_FATE_icon.png':'fate',
           'Daily_Quest_icon.png':'questmob','Quest_icon.png':'questmob','Feature_Quest_icon.png':'quest'}
stages={v['name']:(int(k),v['level']) for k,v in json.load(open('carnivale_stages.json')).items()}
elite=set(json.load(open('elite_marks.json')))
def title_of(href): return urllib.parse.unquote(href.split('/wiki/',1)[1]).replace('_',' ')
def slug(t): return re.sub(r'[^A-Za-z0-9._-]+','_',t)[:120]
def lv_parse(s):
    s=(s or '').strip().replace('–','-')
    m=re.match(r'^(\d+)\s*(?:-\s*(\d+))?',s)
    if not m: return (None,None)
    a=int(m.group(1)); b=int(m.group(2)) if m.group(2) else None
    return (a, b if b and b!=a else None)
def norm_xy(s):
    s=(s or '').strip()
    nums=re.findall(r'\d+(?:\.\d+)?',s)
    return (nums[0]+', '+nums[1]) if len(nums)>=2 else None
def field(block,name):
    m=re.search(r'\|\s*'+name+r'\s*=\s*([^\n]*)',block); return m.group(1).strip() if m else ''
def infobox(wt,name):
    m=re.search(r'\{\{'+name+r'(.*?)\n\}\}',wt,re.S); return m.group(1) if m else None
def base(p): return re.sub(r'\s*\((Hard|Extreme|Savage|Unreal|Ultimate)\)\s*$','',p.strip())

# ---------- places ----------
ZONES=set(); place={}   # name -> dict(kind, zone, level, stage); filled by load_places()
OPEN_WORLD_ZONES=['Middle La Noscea','Lower La Noscea','Eastern La Noscea','Western La Noscea','Upper La Noscea','Outer La Noscea','Central Shroud','East Shroud','South Shroud','North Shroud','Western Thanalan','Central Thanalan','Eastern Thanalan','Southern Thanalan','Northern Thanalan','Coerthas Central Highlands','Coerthas Western Highlands','Mor Dhona','The Sea of Clouds','Azys Lla','The Dravanian Forelands','The Dravanian Hinterlands','The Churning Mists','The Fringes','The Peaks','The Lochs','The Ruby Sea','Yanxia','The Azim Steppe','Lakeland','Kholusia','Amh Araeng','Il Mheg',"The Rak'tika Greatwood",'The Tempest','Labyrinthos','Thavnair','Garlemald','Mare Lamentorum','Elpis','Ultima Thule','Urqopacha',"Kozama'uka","Yak T'el",'Shaaloani','Heritage Found','Living Memory','Foundation','The Pillars',"Ul'dah - Steps of Thal","Ul'dah - Steps of Nald",'Limsa Lominsa Upper Decks','Limsa Lominsa Lower Decks','New Gridania','Old Gridania','Kugane',"Rhalgr's Reach",'The Crystarium','Eulmore','Old Sharlayan','Radz-at-Han','Tuliyollal','Solution Nine','Idyllshire']
LEVE_ZONE={'Moraby Drydocks Levequests':'Lower La Noscea','Aleport Levequests Wanted Target':'Western La Noscea'}
ALIAS={'Sea of Clouds':'The Sea of Clouds','Overwold':'Overworld','Moraby Drydocks#Levequests':'Lower La Noscea','Aleport#Levequests':'Western La Noscea'}
unknown=collections.Counter()

def load_places():
    """Classify every place page fetched into places/ plus the hard-coded open-world zones and Carnivale stages."""
    ZONES.clear(); place.clear(); unknown.clear()
    for f in glob.glob('places/*.json'):
        d=json.load(open(f)); wt=d['wikitext']; t=d['title']
        ib=infobox(wt,'Duty infobox')
        if ib:
            ty=field(ib,'type').lower(); lv=lv_parse(field(ib,'level'))[0]
            k=('dungeon' if ty=='dungeon' else 'trial' if ty=='trial' else 'guildhest' if ty=='guildhest' else 'tdungeon' if 'treasure' in ty else 'skip' if 'ultimate' in ty else 'raid' if 'raid' in ty else None)
            if k: place[t]=dict(kind=k,level=lv); continue
        ib=infobox(wt,'Masked Carnivale infobox')
        if ib:
            n=int(field(ib,'number') or 0); place[t]=dict(kind='carnivale',level=int(field(ib,'level') or 50),stage=n); continue
        ib=infobox(wt,'Area infobox')
        if ib and field(ib,'type').lower()=='zone':
            place[t]=dict(kind='skip')
            continue
        ib=infobox(wt,'FATE infobox')
        if ib:
            place[t]=dict(kind='fate',zone=field(ib,'location'),level=lv_parse(field(ib,'level'))[0]); continue
        ib=infobox(wt,'Quest infobox')
        if ib:
            ty=field(ib,'type').lower()
            place[t]=dict(kind=('leve' if 'leve' in ty else 'questmob'),zone=field(ib,'location'),level=lv_parse(field(ib,'level'))[0]); continue
    for n in stages: place.setdefault(n,dict(kind='carnivale',level=stages[n][1],stage=stages[n][0]))
    for z in OPEN_WORLD_ZONES:
        ZONES.add(z); place[z]=dict(kind='zone')

def resolve_place(p):
    p=ALIAS.get(p.strip(),p.strip())
    if re.search(r'\((Unreal|Savage|Ultimate)\)',p): return p,dict(kind='skip')
    if p in place: return p,place[p]
    b=base(p)
    for cand in (b, b+' (Hard)'):
        if cand in place and place[cand]['kind']!='zone': return p,dict(place[cand],level=place[cand].get('level'))
    if p in NOT_FOR_BLU: return p,dict(kind='skip')
    unknown[p]+=1; return p,None

# ---------- enemies ----------
enemies={}   # title -> dict(fields, rows, wt, img); filled by load_enemies()

def load_enemies():
    """Parse every enemy wikitext fetched into enemies/ (NPC infobox, location rows, a location image)."""
    enemies.clear()
    for f in glob.glob('enemies/*.json'):
        d=json.load(open(f)); wt=d['wikitext']
        ib=infobox(wt,'NPC infobox')
        if not ib: continue
        fields={}
        for line in ib.split('\n'):
            mm=re.match(r'\s*\|\s*([\w-]+)\s*=\s*(.*)$',line)
            if mm and mm.group(2).strip(): fields[mm.group(1)]=mm.group(2).strip()
        rows=[]
        for r in re.findall(r'\{\{NPC location info\|([^}]*)\}\}',wt):
            p=[x.strip() for x in r.split('|')]
            if len(p)>=3: rows.append((p[0],p[1],p[2]))
        imgs=[i.strip() for i in re.findall(r'\[\[File:([^\]|]+?)(?:\||\]\])',wt)]
        imgs=[i for i in imgs if re.search(r'location|map|spawn',i,re.I) and not re.search(r'icon',i,re.I)]
        enemies[d['title']]=dict(fields=fields,rows=rows,wt=wt,img=(imgs[0] if imgs else None))
        enemies.setdefault(d['resolved'],enemies[d['title']])


def enemy_image(title, loc):
    """The enemy page's location image, only where it cannot mislead: the file name names this place,
    or the enemy is known in no other place. A map of the wrong zone is worse than none."""
    e=enemies.get(title); img=((e or {}).get('img') or '')
    if not img: return None
    squash=lambda s: re.sub(r'[\s_\-]+',' ',s.lower())
    if squash(loc) in squash(img): return img
    places=set()
    for pl,_xy,_lv in e['rows']:
        pname,pinfo=resolve_place(pl)
        if pinfo and pinfo['kind']!='skip': places.add(pname)
    return img if (not places or places=={loc}) else None

def enemy_sources(title, fallback=None):
    """fallback: dict(kind, place, lv) from the main table when the enemy page is unusable."""
    e=enemies.get(title); out=[]
    display=re.sub(r'\s*\((Enemy|Boss|boss|enemy|Beast|NPC)\)$','',title)
    if e and e['fields'].get('name') and '{{' not in e['fields']['name']: display=e['fields']['name'].split('|')[0].strip()
    if not e:
        if fallback and fallback.get('kind') and fallback.get('lv'):
            out.append(dict(k=fallback['kind'],name=display,t=title,loc=fallback.get('place') or '',xy=None,lv=fallback['lv'],lvMax=None,note='',rank=None))
        return out
    f=e['fields']; wt=e['wt']; low=wt.lower(); goal=f.get('goal',''); rank=f.get('rank')
    special=None
    if rank in ('A','B','S','SS'): special='hunt'
    elif 'map' in goal.lower() or 'treasure map' in low or 'timeworn' in low: special='map'
    elif goal and goal in place and place[goal]['kind'] in ('fate','leve','questmob'): special=place[goal]['kind']
    elif goal and re.search(r'\bFATE',wt): special='fate'
    elif 'levequest' in low or 'wanted target' in low: special='leve'
    elif goal: special='questmob'
    elif re.search(r'\bFATE\b',wt) and re.search(r'only (appear|spawn)s? during',low): special='fate'
    lv0,lv0max=lv_parse(f.get('level',''))
    rows=list(e['rows'])
    if not rows and f.get('location'): rows=[(f['location'],f.get('coordinates',''),f.get('level',''))]
    for key in ('dungeon','trial','raid','other-duty'):
        if f.get(key) and not any(base(r[0])==base(f[key]) for r in rows): rows.append((f[key],'',f.get('level','')))
    byplace=collections.OrderedDict()
    for pl,xy,lv in rows:
        if not pl: continue
        lvtxt=lv.strip()
        if lvtxt and not re.match(r'^\d',lvtxt): continue      # explicit '-' means not a real spawn
        pname,pinfo=resolve_place(pl)
        if not pinfo or pinfo['kind']=='skip': continue
        lvv,lvmax=lv_parse(lvtxt)
        if lvv is None:
            lvv,lvmax=(pinfo.get('level'),None) if pinfo['kind'] in INST else (lv0,lv0max)
        if lvv is None: continue
        pk=pinfo['kind']
        if pk=='zone': k=special or 'world'; loc=pname; note=''
        elif pk in ('fate','leve','questmob'): k=pk; loc=pinfo.get('zone') or f.get('location','') or pname; note=pname
        else: k=pk; loc=pname; note=''
        if k=='map': loc='Treasure map'
        if k=='hunt': xy=''
        if k=='carnivale': lvv=pinfo.get('level',50); lvmax=None; note='Stage %d'%pinfo.get('stage',0)
        key=(k,loc)
        if key not in byplace:
            byplace[key]=dict(k=k,name=display,t=title,img=e.get('img'),loc=loc,xy=norm_xy(xy),lv=lvv,lvMax=lvmax,note=note,rank=rank if k=='hunt' else None,spots=1)
        else:
            b=byplace[key]; b['spots']+=1
            if lvv<b['lv']: b['lv']=lvv; b['xy']=norm_xy(xy) or b['xy']
            if lvmax and (not b['lvMax'] or lvmax>b['lvMax']): b['lvMax']=lvmax
    worlds=[b for b in byplace.values() if b['k']=='world']
    duty_only = (f.get('objective')=='boss' and any(f.get(x) for x in ('dungeon','trial','raid','other-duty'))) or re.search(r'not (be )?found in the open world|only (appears?|found|spawns?) (in|during) (the )?(guildhest|dungeon|trial|raid|instance|duty)',low)
    if duty_only:
        for key in [k for k,b in byplace.items() if b['k']=='world']: del byplace[key]
    if any(b['xy'] for b in worlds) or any(b['k'] in INST for b in byplace.values()):
        for key in [k for k,b in byplace.items() if b['k']=='world' and not b['xy']]: del byplace[key]
    for b in byplace.values():
        if b['k'] in ('map','carnivale','hunt') or b['xy']=='0, 0': b['xy']=None
        if b['k']=='fate': b['note']=('FATE: '+(b['note'] or goal)) if (b['note'] or goal) else 'FATE'
        elif b['k']=='map': b['note']=goal or 'treasure map'
        elif b['k']=='questmob': b['note']='quest: '+(b['note'] or goal)
        elif b['k']=='leve': b['note']=('levequest: '+b['note']) if b['note'] else 'levequest'
        elif b['k']=='hunt': b['note']='%s-rank hunt mark'%rank
        if b.pop('spots')>1 and b['k']=='world': b['note']='several spots'
        out.append(b)
    for x in out: x['img']=None if x['k']=='map' else enemy_image(title, x['loc'])
    return out

def finish(entry):
    dd=collections.OrderedDict()
    for sd in entry['sources']:
        key=(sd['k'],sd['name'],sd['loc'])
        if key in dd:
            o=dd[key]
            if sd['lv']<o['lv']: o['lv']=sd['lv']; o['xy']=sd['xy'] or o['xy']
            if sd['lvMax'] and (not o['lvMax'] or sd['lvMax']>o['lvMax']): o['lvMax']=sd['lvMax']
            o['rec']=o.get('rec') or sd.get('rec')
            if sd['note'] and sd['note'] not in o['note']:
                po=o['note'].split(': ',1); ps=sd['note'].split(': ',1)
                if len(po)==2 and len(ps)==2 and po[0]==ps[0]: o['note']=po[0]+': '+po[1]+' · '+ps[1]  # names may contain commas
                else: o['note']=(o['note']+'; ' if o['note'] else '')+sd['note']
        else: dd[key]=dict(sd); dd[key].setdefault('rec',False)
    entry['sources']=list(dd.values())
    entry['sources'].sort(key=lambda x:(not x['rec'], x['k'] in INST, x['k'] in ('hunt','map'), x['lv'], x['name']))
    for sdict in entry['sources']:
        for k in list(sdict):
            if sdict[k] in (None,'',False): del sdict[k]

# ---------- BLU ----------
def build_blu():
    """Every Blue Magic spell with its classified sources."""
    from bs4 import BeautifulSoup, Tag  # imported here so the classification helpers need no parser
    s=BeautifulSoup(open('blu.html').read(),'html.parser')
    blu=[]
    for r in s.select('table')[0].select('tr')[1:]:
        c=r.select('td')
        name=c[0].get_text(' ',strip=True); no=int(c[1].get_text(strip=True)); rank=len(c[2].get_text(strip=True))
        typ=[x.strip() for x in c[3].get_text('|',strip=True).split('|') if x.strip()]
        def num(x): x=x.get_text(strip=True); return None if x in ('~','') else int(x)
        minlv=num(c[4]) or 1; dutylv=num(c[6])
        segs=[[]]
        for n in c[7].children:
            if isinstance(n,Tag) and n.name=='br': segs.append([])
            else: segs[-1].append(n)
        common=[]
        for sg in segs:
            text=' '.join(n.get_text(' ',strip=True) if isinstance(n,Tag) else str(n) for n in sg); text=re.sub(r'\s+',' ',text).strip()
            if not text: continue
            links=[]; icons=[]
            for n in sg:
                if isinstance(n,Tag):
                    for a in ([n] if n.name=='a' else n.select('a')):
                        if a.find('img'): icons.append(a.find('img').get('src','').rsplit('/',1)[-1].split('px-')[-1]); continue
                        if a.get('href','').startswith('/wiki/'): links.append(title_of(a['href']))
            frags=[]
            for x in re.findall(r'\(([^()]*)\)',text):
                x=x.strip()
                if re.match(r'^\s*X:',x) or x in ('Hard','Extreme','Savage','Unreal','Enemy','Boss','boss','Blue Mage') or x in frags: continue
                frags.append(x)
            notes=' · '.join(frags)
            common.append(dict(text=text,links=links,icons=icons,notes=notes,
                               enemies=[l for l in links if l in enemies], places=[l for l in links if l not in enemies and resolve_place(l)[1]]))
        entry=dict(id=no,name=name,rank=rank,minLv=minlv,types=typ,sources=[])
        if 'Default' in typ: entry['sources'].append(dict(k='default',name='Known from the start',loc='',xy=None,lv=1,lvMax=None,note='',rank=None,rec=True))
        for cm in common:
            if any('totem' in ic.lower() for ic in cm['icons']):
                parts=cm['text'].split(' - ',1)
                entry['sources'].append(dict(k='totem',name=parts[0].strip(),loc="Ul'dah",xy=None,lv=minlv,lvMax=None,note=(parts[1].strip() if len(parts)>1 else ''),rank=None,rec=True))
        page=None
        for f in glob.glob('pages/spells/*.html'):
            if f.split('/')[-1][:-5].replace('_',' ') in (name,name+' (Blue Mage)'): page=f
        titles=[]
        if page:
            ps=BeautifulSoup(open(page).read(),'html.parser')
            for t in ps.select('#mw-content-text table'):
                rows=t.select('tr'); hdr=[x.get_text(' ',strip=True) for x in rows[0].select('th,td')]
                if hdr[:3]==['Enemy','Level','Location']:
                    for rr in rows[1:]:
                        a=rr.select('td')[0].find('a')
                        if a and a.get('href','').startswith('/wiki/'): titles.append(title_of(a['href']))
        else: print('NO PAGE for',name)
        for cm in common:
            for t in cm['enemies']:
                if t not in titles: titles.append(t)
        seen=set()
        for t in titles:
            if t in seen: continue
            seen.add(t)
            cms=[cm for cm in common if t in cm['enemies']]
            fb=None
            if cms:
                ks=[ICON_KIND.get(ic) for ic in cms[0]['icons'] if ICON_KIND.get(ic)]
                fb=dict(kind=ks[0] if ks else None, place=cms[0]['places'][0] if cms[0]['places'] else None, lv=dutylv)
            srcs=enemy_sources(t,fb)
            # main-table duty mention missing from the enemy page rows -> add it
            for cm in cms:
                for pl in cm['places']:
                    pname,pinfo=resolve_place(pl)
                    if pinfo and pinfo['kind'] in INST and not any(base(x['loc'])==base(pname) for x in srcs):
                        lv=pinfo.get('level') or dutylv or 1
                        note='Stage %d'%pinfo['stage'] if pinfo['kind']=='carnivale' else ''
                        srcs.append(dict(k=pinfo['kind'],name=srcs[0]['name'] if srcs else t,loc=pname,xy=None,lv=lv,lvMax=None,note=note,rank=None))
            for sd in srcs:
                sd['rec']=bool(cms)
                for cm in cms:
                    if cm['notes'] and cm['notes'] not in sd['note']: sd['note']=(sd['note']+'; ' if sd['note'] else '')+cm['notes']
            entry['sources'].extend(srcs)
        entry['sources']=[x for x in entry['sources'] if x['lv']<=80]
        finish(entry)
        blu.append(entry)

    return blu

# ---------- BST ----------
def build_bst():
    """Every tamable beast with its classified sources."""
    from bs4 import BeautifulSoup, Tag  # imported here so the classification helpers need no parser
    s=BeautifulSoup(open('bst.html').read(),'html.parser')
    bst=[]
    for r in s.select('table')[0].select('tr')[1:]:
        c=r.select('td')
        no=int(c[0].get_text(strip=True)); name=c[1].select('a')[-1].get_text(' ',strip=True)
        hinted=c[2].get_text(' ',strip=True); minlv=int(c[4].get_text(strip=True)); gtxt=c[5].get_text(' ',strip=True)
        mapfiles=[title_of(a['href'])[5:] for a in c[3].select('a[href^="/wiki/File:"]')]
        entry=dict(id=no,name=name,minLv=minlv,sources=[])
        page=None
        for f in glob.glob('pages/beasts/*.html'):
            if f.split('/')[-1][:-5].replace('_',' ')==name+' (Beast)': page=f
        if page:
            ps=BeautifulSoup(open(page).read(),'html.parser')
            for t in ps.select('#mw-content-text table'):
                rows=t.select('tr'); hdr=[x.get_text(' ',strip=True) for x in rows[0].select('th,td')]
                if hdr[:2]!=['Mob','Level']: continue
                for rr in rows[1:]:
                    td=rr.select('td')
                    if len(td)<4: continue
                    mob=re.sub(r'\s*\[\s*\d+\s*\]','',td[0].get_text(' ',strip=True)); mob=re.sub(r'\s*\((Enemy|Beast|Boss)\)$','',mob).strip()
                    ma=td[0].find('a'); mtitle=title_of(ma['href']) if ma and ma.get('href','').startswith('/wiki/') else mob
                    mimg=(enemies.get(mtitle) or {}).get('img')
                    lv,lvmax=lv_parse(td[1].get_text(' ',strip=True))
                    po=td[2].get_text(' ',strip=True); loc=td[3].get_text(' ',strip=True)
                    if po=='Overwold': po='Overworld'
                    icons=[img.get('src','').rsplit('/',1)[-1].split('px-')[-1] for img in rr.select('img')]
                    iks=[ICON_KIND.get(ic) for ic in icons if ICON_KIND.get(ic)]
                    mz=re.match(r'^(.*?)\s*\((.*)\)\s*$',loc)
                    zone=ALIAS.get(mz.group(1).strip(),mz.group(1).strip()) if mz else (ALIAS.get(loc,loc) if loc in ZONES else None)
                    xy=norm_xy(mz.group(2).replace('28.22.1','28, 22')) if mz else None
                    if po in ('Dungeon','Trial','Raid','Alliance Raid','Guildhest') and resolve_place(loc)[1] and resolve_place(loc)[1]['kind'] in INST:
                        po,loc=loc,po
                    pname,pinfo=resolve_place(po)
                    k=None; plc=None; note=''; rank=None
                    if loc in ('Dungeon','Trial','Raid','Alliance Raid','Guildhest') or (pinfo and pinfo['kind'] in INST):
                        k=(pinfo['kind'] if pinfo and pinfo['kind'] in INST else (iks[0] if iks and iks[0] in INST else 'dungeon')); plc=pname
                    elif loc in ('Varies','Treasure Map') or 'map' in po.lower():
                        k='map'; plc='Treasure map'; note=po
                    elif (iks and iks[0]=='fate') or (pinfo and pinfo['kind']=='fate'):
                        k='fate'; plc=zone or (pinfo or {}).get('zone') or po; note='FATE: '+po
                    elif 'hunt mark' in po.lower() or mob in elite:
                        k='hunt'; plc=zone or po; m=re.search(r'([ABS])-Rank',po); rank=m.group(1) if m else None; note=po
                    elif (iks and iks[0]=='leve') or (pinfo and pinfo['kind']=='leve') or 'levequest' in loc.lower() or 'levequest' in po.lower():
                        k='leve'; plc=zone or (pinfo or {}).get('zone') or LEVE_ZONE.get(po,po); note='levequest: '+po
                    elif (iks and iks[0]=='questmob') or (pinfo and pinfo['kind']=='questmob'):
                        k='questmob'; plc=zone or (pinfo or {}).get('zone') or po; note='quest: '+po
                    elif po=='Overworld' and zone:
                        k='world'; plc=zone
                    else:
                        print('BST UNKNOWN ROW',name,mob,lv,po,loc,icons); continue
                    if lv is None: print('BST NO LEVEL',name,mob); continue
                    entry['sources'].append(dict(k=k,name=mob,t=mtitle,img=(None if k=='map' else enemy_image(mtitle, plc or '')),loc=plc,xy=xy if k in ('world','fate','leve','questmob') else None,lv=lv,lvMax=lvmax,note=note,rank=rank,rec=(mob==hinted)))
        else: print('NO BEAST PAGE',name)
        if gtxt and gtxt!='—':
            m=re.search(r'requires level (\d+) quest (.+)$',gtxt)
            if m: entry['sources'].append(dict(k='quest',name=gtxt.split(';')[0].strip(),loc='Kornago Gourd',xy=None,lv=int(m.group(1)),lvMax=None,note='after the level %s quest "%s"'%(m.group(1),m.group(2).strip()),rank=None,rec=False))
            elif ' from ' in gtxt: entry['sources'].append(dict(k='default',name=gtxt,loc='',xy=None,lv=1,lvMax=None,note='job quest reward',rank=None,rec=True))
            else: print('BST GOURD?',name,gtxt)
        if mapfiles:
            tgt=next((x for x in entry['sources'] if x.get('rec') and x['k'] not in INST), None) or next((x for x in entry['sources'] if x.get('rec')), None)
            if tgt: tgt['img']=mapfiles[0]
        finish(entry)
        bst.append(entry)

    return bst

def write_outputs(blu, bst):
    for e in blu: e.pop('types',None)
    json.dump({'blu':blu,'bst':bst},open('data.json','w'),ensure_ascii=False,separators=(',',':'))
    print('BLU',len(blu),'BST',len(bst),'bytes',len(open('data.json').read()))
    print('UNKNOWN PLACES:',unknown.most_common())
    with open('report.txt','w') as rep:
        for lst,label in ((blu,'BLU'),(bst,'BST')):
            for e in lst:
                ow=[x for x in e['sources'] if x['k'] not in INST]
                flag=''
                pass
                if not e['sources']: flag='  <-- NO SOURCES'
                rep.write(f"{label} #{e['id']} {e['name']} min{e['minLv']}{flag}\n")
                for x in e['sources']:
                    rep.write(f"     {'*' if x.get('rec') else ' '} {x['k']:<9} lv{x['lv']}{('-'+str(x['lvMax'])) if x.get('lvMax') else '':<4} {x['name']:<34} {x.get('loc',''):<36} {x.get('xy','') or '':<12} {x.get('note','')}\n")
    print(open('report.txt').read()[:6000])

def main():
    load_places(); load_enemies()
    write_outputs(build_blu(), build_bst())

if __name__ == '__main__':
    main()
