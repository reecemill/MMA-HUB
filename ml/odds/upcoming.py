"""The betting market's current line on upcoming UFC fights, for the site.

Downloads every UFC event page linked from the BestFightOdds front page (the cards with
odds open), matches them to scheduled UFC.com fights, and writes the median price across
sportsbooks, vig removed, to src/Web/Data/market_lines.json as P(red corner wins).

Unlike `fetch`, which saves each past event once for its closing line, these pages are
downloaded again on every run, since the lines keep moving until the fight. They're saved
to ml/odds/upcoming/ (overwritten each run), not ml/odds/raw/, so a page saved before an
event can't stand in for its closing line later.
"""

import gzip
import json
import re
import time
from pathlib import Path

import pandas as pd

from ..v2 import data
from . import build, fetch

PAGES = Path(__file__).resolve().parent / "upcoming"
SITE_DATA = Path(__file__).resolve().parents[2] / "src" / "Web" / "Data" / "market_lines.json"


def upcoming():
    front = fetch.get(fetch.SITE + "/").decode("utf-8", "replace")
    slugs = sorted(set(re.findall(r'href="/events/(ufc[^"]+)"', front)))
    PAGES.mkdir(exist_ok=True)
    for old in PAGES.glob("*.html.gz"):
        old.unlink()
    pages = []
    for slug in slugs:
        time.sleep(fetch.DELAY_SECONDS)
        try:
            page = fetch.get(f"{fetch.SITE}/events/{slug}")
        except Exception as e:  # noqa: BLE001 - one bad page shouldn't stop the rest
            print(f"  {slug}: {e}")
            continue
        (PAGES / f"{slug}.html.gz").write_bytes(gzip.compress(page))
        pages.append((slug, page.decode("utf-8", "replace")))

    ds = data.load()
    scheduled = ds.fights[~ds.fights.finished & (ds.fights.Date >= pd.Timestamp.today().normalize())]
    lines, unmatched = build.match(ds, scheduled, pages)
    probabilities, books = {}, {}
    if not lines.empty:
        ml = lines[lines.line.isin(["ML", "ML_BLUE"])].pivot(index="fight_id", columns="line")
        ml = ml.dropna()
        for fight_id, row in ml.iterrows():
            red, blue = row[("p", "ML")], row[("p", "ML_BLUE")]
            probabilities[str(int(fight_id))] = round(float(red / (red + blue)), 4)
            books[str(int(fight_id))] = int(min(row[("n_books", "ML")], row[("n_books", "ML_BLUE")]))
    SITE_DATA.write_text(json.dumps({
        "source": "BestFightOdds",
        "fetched": f"{pd.Timestamp.today():%Y-%m-%d}",
        "fighter1WinProbability": probabilities,
        "books": books,
    }, indent=1))
    print(f"{len(pages)} event pages; market lines for {len(probabilities)} of {len(scheduled)} scheduled fights "
          f"({len(unmatched)} BFO matchups unmatched); wrote {SITE_DATA}")
