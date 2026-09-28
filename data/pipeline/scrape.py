"""Step 1: download the two wiki tables plus helper pages; write page_urls.json, carnivale_stages.json, elite_marks.json."""
import json, re, sys, time, urllib.parse, urllib.request
sys.path.insert(0, './pylib')
from bs4 import BeautifulSoup
BASE = 'https://ffxiv.consolegameswiki.com'
def get(url):
    req = urllib.request.Request(url, headers={'User-Agent': 'Mozilla/5.0'})
    data = urllib.request.urlopen(req, timeout=60).read()
    time.sleep(0.3)
    return data
def title_of(href): return urllib.parse.unquote(href.split('/wiki/', 1)[1]).replace('_', ' ')

open('blu.html', 'wb').write(get(BASE + '/wiki/Blue_Magic_Spellbook'))
open('bst.html', 'wb').write(get(BASE + "/wiki/Master%27s_Bestiary"))

urls = []
s = BeautifulSoup(open('blu.html').read(), 'html.parser')
for r in s.select('table')[0].select('tr')[1:]:
    a = r.select('td')[0].find('a'); urls.append(('spell', a.get_text(' ', strip=True), a['href']))
s = BeautifulSoup(open('bst.html').read(), 'html.parser')
for r in s.select('table')[0].select('tr')[1:]:
    a = r.select('td')[1].select('a')[-1]; urls.append(('beast', a.get_text(' ', strip=True), a['href']))
json.dump(urls, open('page_urls.json', 'w'), indent=1)

s = BeautifulSoup(get(BASE + '/wiki/Masked_Carnivale'), 'html.parser')
stages = {}
for r in s.select('#mw-content-text table')[0].select('tr')[1:]:
    c = [x.get_text(' ', strip=True) for x in r.select('td')]
    if len(c) >= 3 and c[0].isdigit(): stages[int(c[0])] = {'name': c[1], 'level': int(c[2])}
json.dump(stages, open('carnivale_stages.json', 'w'), indent=1)

names = set(); url = BASE + '/wiki/Category:Elite_Marks'
while url:
    s = BeautifulSoup(get(url), 'html.parser')
    names.update(a.get_text() for a in s.select('#mw-pages li a'))
    nxt = [a['href'] for a in s.select('#mw-pages a') if 'next page' in a.get_text()]
    url = BASE + nxt[0] if nxt else None
names -= {'A Rank', 'B Rank', 'S Rank'}
json.dump(sorted(names), open('elite_marks.json', 'w'))
print(len(urls), 'pages;', len(stages), 'Carnivale stages;', len(names), 'elite marks')
