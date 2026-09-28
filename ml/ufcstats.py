"""Per-fight stats from the ufcstats.com export (see download_ufcstats.py).

The export names fighters rather than giving ids, so fights are looked up by date
and the pair of ufcstats names. Our fighters carry their ufcstats id, which
ufc_fighter_details.csv turns into that exact name, so spellings always agree.
"""

import re
from dataclasses import dataclass, fields
from pathlib import Path

import pandas as pd


@dataclass
class FightStats:
    """One fighter's totals in one fight (knockdowns, strikes, takedowns, control)."""
    minutes: float = 0.0
    sig_landed: float = 0.0
    sig_attempted: float = 0.0
    knockdowns: float = 0.0
    td_landed: float = 0.0
    td_attempted: float = 0.0
    sub_attempts: float = 0.0
    control_seconds: float = 0.0

    def __iadd__(self, other):
        for f in fields(self):
            setattr(self, f.name, getattr(self, f.name) + getattr(other, f.name))
        return self


def _landed_of(value):
    """'29 of 73' -> (29, 73)."""
    m = re.match(r"\s*(\d+)\s+of\s+(\d+)", str(value))
    return (float(m.group(1)), float(m.group(2))) if m else (0.0, 0.0)


def _seconds(value):
    """'4:21' -> 261; '--' -> 0."""
    m = re.match(r"\s*(\d+):(\d+)", str(value))
    return int(m.group(1)) * 60 + int(m.group(2)) if m else 0


def _fight_minutes(last_round, time, time_format):
    """How long a fight lasted, from its last round, the time in it, and the round lengths."""
    lengths = [int(x) for x in re.findall(r"\d+", str(time_format).split("(")[-1])] if "(" in str(time_format) else []
    before = sum(lengths[:max(int(last_round) - 1, 0)])
    return before + _seconds(time) / 60


def _clean(series):
    return series.astype(str).str.strip()


def load(folder):
    """Stats for every fight with them, keyed by (date, frozenset of the two ufcstats names),
    plus maps from ufcstats id to name and to date of birth."""
    folder = Path(folder)
    events = pd.read_csv(folder / "ufc_event_details.csv")
    date_of = dict(zip(_clean(events.EVENT), pd.to_datetime(events.DATE, format="%B %d, %Y")))

    results = pd.read_csv(folder / "ufc_fight_results.csv")
    minutes = {(e, b): _fight_minutes(r, t, f) for e, b, r, t, f in zip(
        _clean(results.EVENT), _clean(results.BOUT), results.ROUND, results.TIME, results["TIME FORMAT"])}

    rounds = (pd.read_csv(folder / "ufc_fight_stats.csv")
              .dropna(subset=["FIGHTER", "ROUND"])
              .rename(columns={"SIG.STR.": "sig", "TD": "td", "KD": "kd", "SUB.ATT": "sub", "CTRL": "ctrl"})
              .fillna({"kd": 0, "sub": 0}))
    fights = {}
    for row in rounds.itertuples(index=False):
        event, bout, name = row.EVENT.strip(), row.BOUT.strip(), row.FIGHTER.strip()
        if (event, bout) not in minutes or event not in date_of:
            continue
        sig, td = _landed_of(row.sig), _landed_of(row.td)
        stats = FightStats(0.0, sig[0], sig[1], float(row.kd), td[0], td[1], float(row.sub), _seconds(row.ctrl))
        key = (date_of[event], frozenset(n.strip() for n in bout.split(" vs. ")))
        per_fighter = fights.setdefault(key, {})
        if name not in per_fighter:
            per_fighter[name] = FightStats(minutes=minutes[(event, bout)])
        per_fighter[name] += stats

    details = pd.read_csv(folder / "ufc_fighter_details.csv")
    ids = details.URL.str.rsplit("/", n=1).str[-1]
    name_of = dict(zip(ids, (details.FIRST.fillna("") + " " + details.LAST.fillna("")).str.strip()))

    tott = pd.read_csv(folder / "ufc_fighter_tott.csv")
    born = pd.to_datetime(tott.DOB, format="%b %d, %Y", errors="coerce")
    birth_of = {i: d for i, d in zip(tott.URL.str.rsplit("/", n=1).str[-1], born) if pd.notna(d)}
    return fights, name_of, birth_of


def lookup(fights, date, name_a, name_b):
    """The stats for a fight between two named fighters on (or a day either side of) a date."""
    pair = frozenset((name_a, name_b))
    for shift in (0, -1, 1):
        found = fights.get((date + pd.Timedelta(days=shift), pair))
        if found and name_a in found and name_b in found:
            return found[name_a], found[name_b]
    return None
