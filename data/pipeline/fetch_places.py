import json, glob, re, sys, time, urllib.parse, urllib.request
sys.path.insert(0,'./pylib')
from bs4 import BeautifulSoup
API='https://ffxiv.consolegameswiki.com/mediawiki/api.php'
def title_of(href): return urllib.parse.unquote(href.split('/wiki/',1)[1]).replace('_',' ')
def slug(t): return re.sub(r'[^A-Za-z0-9._-]+','_',t)[:120]
names=set()
enemy_titles=set()
for f in glob.glob('enemies/*.json'):
    d=json.load(open(f)); wt=d['wikitext']
    if '{{NPC infobox' in wt: enemy_titles.add(d['title']); enemy_titles.add(d['resolved'])
    for r in re.findall(r'\{\{NPC location info\|([^}]*)\}\}', wt):
        p=[x.strip() for x in r.split('|')]
        if p and p[0]: names.add(p[0])
    m=re.search(r'\{\{NPC infobox(.*?)\n\}\}', wt, re.S)
    if m:
        for line in m.group(1).split('\n'):
            mm=re.match(r'\s*\|\s*(dungeon|raid|trial|other-duty|location|goal)\s*=\s*(.+)$', line)
            if mm and mm.group(2).strip(): names.add(mm.group(2).strip())
s=BeautifulSoup(open('blu.html').read(),'html.parser')
for r in s.select('table')[0].select('tr')[1:]:
    for a in r.select('td')[7].select('a[href^="/wiki/"]'): names.add(title_of(a['href']))
s=BeautifulSoup(open('bst.html').read(),'html.parser')
for r in s.select('table')[0].select('tr')[1:]:
    for a in r.select('td')[2].select('a[href^="/wiki/"]')+r.select('td')[5].select('a[href^="/wiki/"]'): names.add(title_of(a['href']))
for f in glob.glob('pages/beasts/*.html'):
    ps=BeautifulSoup(open(f).read(),'html.parser')
    for t in ps.select('#mw-content-text table'):
        rows=t.select('tr'); hdr=[x.get_text(' ',strip=True) for x in rows[0].select('th,td')]
        if hdr[:2]==['Mob','Level']:
            for rr in rows[1:]:
                td=rr.select('td')
                if len(td)>=3:
                    for a in td[2].select('a[href^="/wiki/"]'): names.add(title_of(a['href']))
names={n for n in names if n and not n.startswith('File:') and n not in enemy_titles}
have={json.load(open(f))['title'] for f in glob.glob('places/*.json')}
todo=sorted(n for n in names if n not in have)
print(len(names),'place names,',len(todo),'to fetch',flush=True)
for i in range(0,len(todo),50):
    batch=todo[i:i+50]
    q=urllib.parse.urlencode({'action':'query','prop':'revisions','rvprop':'content','rvslots':'main','format':'json','formatversion':'2','redirects':'1','titles':'|'.join(batch)})
    req=urllib.request.Request(API+'?'+q, headers={'User-Agent':'Mozilla/5.0'})
    d=json.loads(urllib.request.urlopen(req,timeout=60).read())
    redirects={r['from']:r['to'] for r in d['query'].get('redirects',[])}
    by_title={p['title']:p for p in d['query']['pages']}
    for t in batch:
        p=by_title.get(redirects.get(t,t))
        rec={'title':t,'resolved':redirects.get(t,t),'missing':(p is None or p.get('missing',False)),
             'wikitext': (p['revisions'][0]['slots']['main']['content'] if p and 'revisions' in p else '')}
        json.dump(rec,open(f'places/{slug(t)}.json','w'))
    print('batch',i//50+1,'ok',flush=True); time.sleep(0.4)
# summarize templates
import collections
tm=collections.Counter(); ex={}
for f in glob.glob('places/*.json'):
    d=json.load(open(f)); m=re.search(r'\{\{([^|\n}]+)', d['wikitext'])
    t=(m.group(1).strip() if m else ('MISSING' if d['missing'] else '?'))
    tm[t]+=1; ex.setdefault(t,[]).append(d['title'])
for t,c in tm.most_common(): print(f'{c:>3} {t:<28} e.g. {ex[t][:6]}')
# show infobox fields for one Duty, one Levequest, one FATE, one Area
for want in ['Duty infobox','Levequest infobox','FATE infobox','Area infobox','Masked Carnivale infobox','Guildhest infobox']:
    for f in glob.glob('places/*.json'):
        d=json.load(open(f))
        if d['wikitext'].startswith('{{'+want):
            m=re.search(r'\{\{'+want+r'(.*?)\n\}\}', d['wikitext'], re.S)
            print('====',want,'|',d['title']); print((m.group(1) if m else d['wikitext'])[:700]); break
