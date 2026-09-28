import json, urllib.parse, urllib.request, os, time, sys
BASE='https://ffxiv.consolegameswiki.com'
urls=json.load(open('page_urls.json'))
todo=[]
for kind,name,href in urls:
    fn=urllib.parse.unquote(href.split('/wiki/',1)[1]).replace('/','_')
    path=f"pages/{kind}s/{fn}.html"
    if os.path.exists(path) and os.path.getsize(path)>5000: continue
    todo.append((href,path))
print(len(todo),'to fetch', flush=True)
for href,path in todo:
    req=urllib.request.Request(BASE+href, headers={'User-Agent':'Mozilla/5.0'})
    try:
        data=urllib.request.urlopen(req, timeout=30).read()
        open(path,'wb').write(data)
    except Exception as e:
        print('FAIL',href,e, flush=True)
    time.sleep(0.2)
print('DONE', len(os.listdir('pages/spells')), 'spells', len(os.listdir('pages/beasts')), 'beasts', flush=True)
