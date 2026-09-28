"""Collect every enemy linked from the main tables and the per-spell/per-beast pages, then pull their wikitext via the MediaWiki API (batches of 50)."""
import json, glob, os, re, sys, time, urllib.parse, urllib.request
sys.path.insert(0,'./pylib')
from bs4 import BeautifulSoup
API='https://ffxiv.consolegameswiki.com/mediawiki/api.php'
def title_of(href):
    return urllib.parse.unquote(href.split('/wiki/',1)[1]).replace('_',' ')
titles=set()
# main BLU table: all links in the acquisition cells that are not zones/duties are candidates; just take every link and let the infobox decide
s=BeautifulSoup(open('blu.html').read(),'html.parser')
for r in s.select('table')[0].select('tr')[1:]:
    for a in r.select('td')[7].select('a[href^="/wiki/"]'):
        if not a.find('img'): titles.add(title_of(a['href']))
s=BeautifulSoup(open('bst.html').read(),'html.parser')
for r in s.select('table')[0].select('tr')[1:]:
    c=r.select('td')
    for a in c[2].select('a[href^="/wiki/"]'): titles.add(title_of(a['href']))
for f in glob.glob('pages/spells/*.html'):
    s=BeautifulSoup(open(f).read(),'html.parser')
    for t in s.select('#mw-content-text table'):
        rows=t.select('tr'); hdr=[x.get_text(' ',strip=True) for x in rows[0].select('th,td')]
        if hdr[:3]==['Enemy','Level','Location']:
            for r in rows[1:]:
                a=r.select('td')[0].find('a')
                if a and a.get('href','').startswith('/wiki/'): titles.add(title_of(a['href']))
for f in glob.glob('pages/beasts/*.html'):
    s=BeautifulSoup(open(f).read(),'html.parser')
    for t in s.select('#mw-content-text table'):
        rows=t.select('tr'); hdr=[x.get_text(' ',strip=True) for x in rows[0].select('th,td')]
        if hdr[:2]==['Mob','Level']:
            for r in rows[1:]:
                a=r.select('td')[0].find('a')
                if a and a.get('href','').startswith('/wiki/'): titles.add(title_of(a['href']))
titles={t for t in titles if not t.startswith('File:')}
have={json.load(open(f))['title'] for f in glob.glob('enemies/*.json')}
todo=sorted(t for t in titles if t not in have)
print(len(titles),'titles total,',len(todo),'to fetch', flush=True)
def slug(t): return re.sub(r'[^A-Za-z0-9._-]+','_',t)[:120]
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
        json.dump(rec,open(f'enemies/{slug(t)}.json','w'))
    print('batch',i//50+1,'ok', flush=True); time.sleep(0.5)
print('DONE', flush=True)
