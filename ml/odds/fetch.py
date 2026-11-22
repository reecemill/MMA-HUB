"""Downloads BestFightOdds event pages for UFC events, once each, into ml/odds/raw/.

An archived event page shows every sportsbook's last line before the fight (the closing
line), plus prop markets (method of victory, over/under rounds). Pages are saved gzipped
and never fetched again, so the parser can be rerun offline. robots.txt allows everything
except /cnadm/; requests are spaced out so the crawl stays light.
"""

import gzip
import re
import time
import urllib.request
from pathlib import Path

RAW = Path(__file__).resolve().parent / "raw"
SITE = "https://www.bestfightodds.com"
HEADERS = {"User-Agent": "Mozilla/5.0 (personal research script; one request every few seconds)"}
DELAY_SECONDS = 3.0


def get(url):
    with urllib.request.urlopen(urllib.request.Request(url, headers=HEADERS), timeout=60) as r:
        return r.read()


def ufc_events(since):
    """(slug, lastmod) for every UFC event page in the sitemap modified on or after `since`.
    lastmod is about the event date for past events; placeholders for future ones also match."""
    xml = get(f"{SITE}/sitemap-events.xml").decode()
    events = re.findall(r"<loc>[^<]*/events/(ufc[^<]*)</loc><lastmod>([^<]*)</lastmod>", xml)
    return [(slug, mod) for slug, mod in events if mod >= since]


def path_for(slug):
    return RAW / f"{slug}.html.gz"


def fetch(since="2023-12-25", until=None):
    RAW.mkdir(parents=True, exist_ok=True)
    events = [(s, m) for s, m in ufc_events(since) if until is None or m <= until]
    todo = [s for s, _ in events if not path_for(s).exists()]
    print(f"{len(events)} UFC event pages since {since}; {len(todo)} to download", flush=True)
    for i, slug in enumerate(todo, 1):
        time.sleep(DELAY_SECONDS)
        try:
            html = get(f"{SITE}/events/{slug}")
        except Exception as e:  # noqa: BLE001 - log and keep going; a rerun retries it
            print(f"  {slug}: {e}", flush=True)
            continue
        path_for(slug).write_bytes(gzip.compress(html))
        if i % 20 == 0 or i == len(todo):
            print(f"  {i}/{len(todo)}", flush=True)


def read(slug):
    return gzip.decompress(path_for(slug).read_bytes()).decode("utf-8", "replace")
