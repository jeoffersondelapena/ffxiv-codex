"""Rebuild data/codex-data.json from the wiki and the game tables. Run from anywhere: python3 data/pipeline/run_all.py"""
import os, subprocess, sys
here = os.path.dirname(os.path.abspath(__file__)); os.chdir(here)
try:
    sys.path.insert(0, './pylib'); import bs4  # noqa
except ImportError:
    subprocess.check_call([sys.executable, '-m', 'pip', 'install', '--quiet', '--target', './pylib', 'beautifulsoup4'])
for d in ('pages/spells', 'pages/beasts', 'enemies', 'places'): os.makedirs(d, exist_ok=True)
for step in ('scrape.py', 'fetch_pages.py', 'fetch_enemies.py', 'fetch_places.py', 'build_data.py', 'enrich.py'):
    print('==>', step, flush=True)
    subprocess.check_call([sys.executable, step], env={**os.environ, 'PYTHONPATH': './pylib'})
print('Done: data/codex-data.json rebuilt. Review report.txt for entries the wiki left ambiguous.')
