"""Loads what the model may use from the scraper's UFC.com tables.

The queries below are the only place the model reads the database, and they select
only things known before each fight or that never change. Left out on purpose (the
leakage tests check this): UfcFighterProfiles and UfcRankings, which are as of when
they were fetched, and UfcFighter's record, weight class and weight, which come from
the fighter's latest fight.
"""

import hashlib
import re
from dataclasses import dataclass

import numpy as np
import pandas as pd

from . import db

QUERIES = {
    "events": 'select "Id", "StartTime", "Date" from "UfcEvents"',
    "fights": '''select "Id", "EventId", "FightOrder", "Status", "WeightClass", "IsTitleFight",
                        "ScheduledRounds", "RuleSet", "WinnerFighterId", "Method", "EndingRound",
                        "EndingTime", "DurationSeconds", "OfficialStats"
                 from "UfcFights"''',
    "corners": '''select "FightId", "FighterId", "Corner", "Outcome", "WeighIn"::float8 as "WeighIn"
                  from "UfcFightFighters"''',
    "fighters": '''select "Id", "Slug", "FirstName", "LastName", "DateOfBirth",
                          "HeightInches"::float8 as "HeightInches", "ReachInches"::float8 as "ReachInches", "Stance"
                   from "UfcFighters"''',
    "stats": '''select "FightId", "FighterId", "Knockdowns", "SigStrikesLanded", "SigStrikesAttempted",
                       "SigHeadStrikesLanded", "SigBodyStrikesLanded", "SigLegStrikesLanded",
                       "SigDistanceStrikesLanded", "SigClinchStrikesLanded", "SigGroundStrikesLanded",
                       "TakedownsLanded", "TakedownsAttempted", "SubmissionsAttempted", "ControlSeconds"
                from "UfcFightStats"''',
    "round_scores": 'select "FightId", "JudgeId", "Round", "FighterId", "Score" from "UfcRoundScores"',
}

# UfcFight.Method -> the groups the model predicts. Anything else is "other".
METHOD_GROUPS = {
    "KO/TKO": "ko", "TKO - Doctor's Stoppage": "ko",
    "Submission": "sub",
    "Decision - Unanimous": "dec", "Decision - Split": "dec", "Decision - Majority": "dec",
}

# The two rule sets every modern fight uses; earlier ones (one long round, overtime) are
# left out of the round model.
MODERN_RULE_SETS = {"3 Rnd (5-5-5)", "5 Rnd (5-5-5-5-5)"}


def _query(conn, sql):
    with conn.cursor() as cur:
        cur.execute(sql)
        return pd.DataFrame(cur.fetchall(), columns=[d.name for d in cur.description])


def _clock_seconds(text):
    m = re.match(r"\s*(\d+):(\d{1,2})\s*$", str(text))
    return int(m.group(1)) * 60 + int(m.group(2)) if m else np.nan


@dataclass
class Dataset:
    """Every UFC fight with both corners, in the order the model walks them.

    fights: one row per fight, sorted by event (start time, then id) and, within an
        event, chronologically (FightOrder counts down to the main event, then id).
    fighters: static fields only, indexed by fighter id.
    stats: (fight id, fighter id) -> that fighter's numbers in that fight.
    round_scores: fight id -> rows of (judge, round, fighter, score).
    """
    fights: pd.DataFrame
    fighters: pd.DataFrame
    stats: pd.DataFrame
    round_scores: pd.DataFrame

    def until(self, event_id):
        """Only the events up to and including this one (as if it were the latest)."""
        last = self.fights.loc[self.fights.EventId == event_id, "event_rank"].max()
        return self._keep(self.fights.event_rank <= last)

    def without_events(self, event_ids):
        return self._keep(~self.fights.EventId.isin(event_ids))

    def _keep(self, mask):
        fights = self.fights[mask]
        ids = set(fights.Id)
        return Dataset(fights.reset_index(drop=True), self.fighters,
                       self.stats[self.stats.FightId.isin(ids)], self.round_scores[self.round_scores.FightId.isin(ids)])

    def fingerprint(self):
        """A short hash of everything loaded, saved with each model to tie it to its data."""
        h = hashlib.sha256()
        for frame in (self.fights, self.fighters.reset_index(), self.stats, self.round_scores):
            h.update(pd.util.hash_pandas_object(frame, index=False).values.tobytes())
        return h.hexdigest()[:8]


def load(conn=None):
    own = conn is None
    conn = conn or db.connect()
    try:
        frames = {name: _query(conn, sql) for name, sql in QUERIES.items()}
    finally:
        if own:
            conn.close()
    return prepare(frames)


def prepare(frames):
    events = frames["events"].dropna(subset=["StartTime"]).copy()
    events["StartTime"] = pd.to_datetime(events.StartTime, utc=True)
    events["Date"] = pd.to_datetime(events.Date)
    events = events.sort_values(["StartTime", "Id"]).reset_index(drop=True)
    events["event_rank"] = np.arange(len(events))

    corners = frames["corners"]
    red = corners[corners.Corner == "Red"].set_index("FightId")
    blue = corners[corners.Corner == "Blue"].set_index("FightId")

    f = frames["fights"].merge(events.rename(columns={"Id": "EventId"}), on="EventId")
    f = f[f.Id.isin(red.index) & f.Id.isin(blue.index)].copy()
    f["RedId"] = f.Id.map(red.FighterId).astype(int)
    f["BlueId"] = f.Id.map(blue.FighterId).astype(int)
    f["RedOutcome"] = f.Id.map(red.Outcome)
    f["BlueOutcome"] = f.Id.map(blue.Outcome)
    f["RedWeighIn"] = f.Id.map(red.WeighIn).astype(float)
    f["BlueWeighIn"] = f.Id.map(blue.WeighIn).astype(float)

    # "Over" is how UFC.com marks a few 1990s fights that are final.
    f["finished"] = f.Status.isin(["Final", "Over"])
    f["method_group"] = f.Method.map(METHOD_GROUPS).where(f.Method.notna(), None)
    f.loc[f.finished & f.method_group.isna(), "method_group"] = "other"
    # A no contest (including an overturned result) has no winner, whatever WinnerFighterId says.
    f["result"] = np.select(
        [~f.finished, f.RedOutcome == "No Contest", f.RedOutcome == "Draw",
         f.WinnerFighterId == f.RedId, f.WinnerFighterId == f.BlueId],
        [None, "nc", "draw", "red", "blue"], default="nc")
    f["modern"] = f.RuleSet.isin(MODERN_RULE_SETS)

    # Every finished fight has a duration today; the fallback assumes five-minute rounds.
    f["DurationSeconds"] = f.DurationSeconds.astype(float)
    missing = f.finished & f.DurationSeconds.isna() & f.EndingRound.notna()
    f["duration_estimated"] = missing
    if missing.any():
        f.loc[missing, "DurationSeconds"] = ((f.loc[missing, "EndingRound"] - 1) * 300
                                             + f.loc[missing, "EndingTime"].map(_clock_seconds)).astype(float)

    f = f.sort_values(["event_rank", "FightOrder", "Id"], ascending=[True, False, True]).reset_index(drop=True)

    fighters = frames["fighters"].set_index("Id")
    fighters["DateOfBirth"] = pd.to_datetime(fighters.DateOfBirth)
    stats = frames["stats"][frames["stats"].FightId.isin(set(f.Id))].reset_index(drop=True)
    scores = frames["round_scores"][frames["round_scores"].FightId.isin(set(f.Id))].reset_index(drop=True)
    return Dataset(f, fighters, stats, scores)
