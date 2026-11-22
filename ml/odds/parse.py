"""Reads a saved BestFightOdds event page into one row per (matchup, market line, sportsbook).

The page has two tables: names only (for narrow screens), then the odds. In the odds
table each matchup is two fighter rows (the moneyline), followed by prop rows
(class "pr") that name fighters by surname, e.g. "Pereira wins by TKO/KO". A cell's
data-li attribute is [book id, side, matchup id]; its text is American odds.
"""

import html as htmllib
import re

import pandas as pd

_ROW = re.compile(r"<tr([^>]*)>(.*?)</tr>", re.S)
_TH = re.compile(r"<th[^>]*>(.*?)</th>", re.S)
_TD = re.compile(r"<td([^>]*)>(.*?)</td>", re.S)
_FIGHTER = re.compile(r'href="/fighters/([^"]+)"[^>]*><span[^>]*>([^<]+)<')
_BOOKS = re.compile(r'<th scope="col" data-b="(\d+)">(?:<a[^>]*>|<span[^>]*>)([^<]+)<')
_ODDS = re.compile(r"[+-]\d+")


def _text(fragment):
    return htmllib.unescape(htmllib.unescape(re.sub(r"<[^>]+>", "", fragment))).strip()


def _cells(row_html):
    """[(book_id, american odds)] for the cells that have a price."""
    out = []
    for attrs, body in _TD.findall(row_html):
        li = re.search(r'data-li="\[(\d+),', attrs)
        odds = _ODDS.search(_text(body))
        if li and odds:
            out.append((int(li.group(1)), int(odds.group())))
    return out


def books(page):
    return {int(b): name.strip() for b, name in _BOOKS.findall(page)}


def parse(page, slug):
    """DataFrame: slug, matchup (0-based on the page), f1_slug, f1_name, f2_slug, f2_name,
    line (the row's label; "ML1"/"ML2" for the moneyline), book, odds."""
    start = page.find('class="odds-table"><thead')
    if start < 0:
        return pd.DataFrame()
    table = page[start:page.find("</table>", start)]
    rows, matchup, pending = [], -1, []
    for attrs, body in _ROW.findall(table):
        th = _TH.findall(body)
        if not th:
            continue
        fighter = _FIGHTER.search(th[0])
        if fighter and 'class="pr"' not in attrs:
            pending.append((fighter.group(1), _text(fighter.group(2)), _cells(body)))
            if len(pending) == 2:
                matchup += 1
                (s1, n1, c1), (s2, n2, c2) = pending
                current = {"slug": slug, "matchup": matchup, "f1_slug": s1, "f1_name": n1, "f2_slug": s2, "f2_name": n2}
                for line, cells in (("ML1", c1), ("ML2", c2)):
                    rows += [{**current, "line": line, "book": b, "odds": o} for b, o in cells]
                pending = []
        elif 'class="pr"' in attrs and matchup >= 0 and not pending:
            label = _text(th[0])
            rows += [{**current, "line": label, "book": b, "odds": o} for b, o in _cells(body)]
    return pd.DataFrame(rows)


def implied(odds):
    """American odds -> the bookmaker's implied probability (vig included)."""
    odds = pd.Series(odds, dtype=float)
    return (100 / (odds + 100)).where(odds > 0, -odds / (-odds + 100))
