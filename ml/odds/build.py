"""Matches the saved BestFightOdds matchups to UFC.com fights and writes the market's
closing consensus per fight to ml/odds/out/closing_lines.csv.

One row per (fight, line): the line label with the fighter's name replaced by RED or
BLUE ("RED wins by TKO/KO", "Over 2½ rounds", "ML" for the moneyline on red...), the
median implied probability across sportsbooks (vig still in), and how many books priced it.
Removing the vig is left to whoever uses a market, since it depends on which lines make
up the market.
"""

import difflib
import re
import unicodedata
from pathlib import Path

import numpy as np
import pandas as pd

from ..v2 import data
from . import fetch, parse

OUT = Path(__file__).resolve().parent / "out"
SINCE = pd.Timestamp("2023-12-25")
MONTHS = {m: i for i, m in enumerate(["january", "february", "march", "april", "may", "june", "july",
                                       "august", "september", "october", "november", "december"], 1)}


def norm(name):
    s = unicodedata.normalize("NFKD", str(name)).encode("ascii", "ignore").decode().lower()
    return " ".join(re.sub(r"[^a-z ]", " ", s).split())


def page_month_day(page):
    """The page's 'April 14th' header as (month, day), or None."""
    m = re.search(r'table-header-date">\s*([A-Za-z]+) (\d+)', page)
    return (MONTHS.get(m.group(1).lower()), int(m.group(2))) if m else None


def _score(a, b):
    """How alike two normalized names are; word order and middle names don't count against."""
    if a == b:
        return 1.0
    ta, tb = set(a.split()), set(b.split())
    overlap = len(ta & tb) / min(len(ta), len(tb)) if ta and tb else 0
    return max(difflib.SequenceMatcher(None, a, b).ratio(), overlap * 0.95)


def build():
    ds = data.load()
    f = ds.fights[(ds.fights.Date >= SINCE)].copy()
    slugs = sorted(p.name[:-len(".html.gz")] for p in fetch.RAW.glob("*.html.gz"))
    out, unmatched = match(ds, f, ((slug, fetch.read(slug)) for slug in slugs))
    OUT.mkdir(exist_ok=True)
    out.to_csv(OUT / "closing_lines.csv", index=False)
    pd.DataFrame(unmatched).to_csv(OUT / "unmatched.csv", index=False)

    decided = f[f.finished]
    have = set(out.fight_id) if not out.empty else set()
    print(f"{len(slugs)} pages; {len(have)} UFC.com fights matched; {len(unmatched)} BFO matchups unmatched")
    print(f"Coverage of finished UFC.com fights since {SINCE:%Y-%m-%d}: {decided.Id.isin(have).mean():.1%} "
          f"({decided.Id.isin(have).sum()}/{len(decided)})")
    missing = decided[~decided.Id.isin(have)]
    if len(missing):
        print("Missing by year:", missing.Date.dt.year.value_counts().sort_index().to_dict())
    return out


def match(ds, f, pages):
    """Matches the matchups on BFO pages, given as (slug, html) pairs, to the UFC.com fights
    in `f`. Returns (consensus lines as in closing_lines.csv, unmatched matchups)."""
    names = {pid: norm(f"{r.FirstName or ''} {r.LastName or ''}") for pid, r in ds.fighters.iterrows()}
    ids_in_window = sorted(set(f.RedId) | set(f.BlueId))
    by_pair = {}
    for r in f.itertuples():
        by_pair.setdefault(frozenset((r.RedId, r.BlueId)), []).append(r)

    def best_fighter(name):
        n = norm(name)
        score, pid = max((_score(n, names[p]), p) for p in ids_in_window)
        return pid if score >= 0.8 else None

    cache = {}
    rows, unmatched = [], []
    for slug, page in pages:
        lines = parse.parse(page, slug)
        if lines.empty:
            continue
        md = page_month_day(page)
        for (m, s1, n1, s2, n2), g in lines.groupby(["matchup", "f1_slug", "f1_name", "f2_slug", "f2_name"]):
            a = cache.setdefault(n1, best_fighter(n1))
            b = cache.setdefault(n2, best_fighter(n2))
            fights = by_pair.get(frozenset((a, b)), []) if a and b and a != b else []
            if md and len(fights) > 1:
                fights = sorted(fights, key=lambda r: abs((r.Date.month - md[0]) * 31 + r.Date.day - md[1]))
            if not fights:
                unmatched.append({"slug": slug, "f1": n1, "f2": n2, "a": a, "b": b})
                continue
            fight = fights[0]
            f1_red = a == fight.RedId
            rows.append(_consensus(g, fight, f1_red, n1, n2, f"{slug}#{m}"))
    out = pd.concat(rows, ignore_index=True) if rows else pd.DataFrame()
    # The same fight can sit on two pages (a card's page and a placeholder), or twice on one
    # page; keep the listing whose moneyline had the most books.
    if not out.empty:
        depth = out[out.line == "ML"].set_index(["fight_id", "slug"]).n_books
        best = depth.groupby(level=0).idxmax().map(lambda t: t[1])
        out = out[out.slug == out.fight_id.map(best)]
    return out, unmatched


def _consensus(g, fight, f1_red, n1, n2, slug):
    """Median implied probability per line, labels in RED/BLUE terms."""
    s1, s2 = norm(n1).split()[-1], norm(n2).split()[-1]
    first, second = ("RED", "BLUE") if f1_red else ("BLUE", "RED")

    def relabel(line):
        if line == "ML1":
            return f"{first} ML"
        if line == "ML2":
            return f"{second} ML"
        m = re.match(r"^(Not )?(.+?) (wins .*|inside distance|by decision)$", line)
        if m:
            who = norm(m.group(2))
            # Prop rows use the fighter's surname as BFO writes it; compare with both names.
            c1 = _score(who, s1) if who not in norm(n1) else 1.0
            c2 = _score(who, s2) if who not in norm(n2) else 1.0
            if max(c1, c2) >= 0.8 and c1 != c2:
                side = first if c1 > c2 else second
                return f"{m.group(1) or ''}{side} {m.group(3)}"
            return None
        return line

    g = g.assign(label=g.line.map(relabel), p=parse.implied(g.odds).to_numpy()).dropna(subset=["label"])
    agg = g.groupby("label").agg(p=("p", "median"), n_books=("p", "size")).reset_index()
    agg = agg.rename(columns={"label": "line"})
    # "RED ML" is the moneyline on red; keep both sides so the vig can be removed.
    agg["line"] = agg.line.replace({"RED ML": "ML", "BLUE ML": "ML_BLUE"})
    agg.insert(0, "slug", slug)  # page slug + "#" + matchup number on the page
    agg.insert(0, "date", fight.Date)
    agg.insert(0, "fight_id", fight.Id)
    return agg
