"""Leakage-safe features: every fighter's history rebuilt fight by fight.

Walks the events in order. Every fight on a card gets its features from each fighter's
state *before* that card, and the card's results are applied only once every fight on it
has its features. So fighters on the same card never see each other's results from that
night (nor, in the 1990s tournaments, their own earlier fights that night).

Per-minute rates and percentages are shrunk toward the weight-class average (empirical
Bayes): a fighter with little cage time looks like an average fighter in their division
until there's enough to go on. The class averages also come only from earlier fights.
"""

import math
from dataclasses import dataclass, field

import numpy as np
import pandas as pd

from .glicko import Glicko2, GlickoParams

# Each fighter's numbers in a fight, as a vector. "seconds" is the fight's length.
STAT_FIELDS = ["seconds", "kd", "sig", "sig_att", "head", "body", "leg", "distance", "clinch", "ground",
               "td", "td_att", "sub", "ctrl"]
_I = {name: i for i, name in enumerate(STAT_FIELDS)}
_STAT_COLUMNS = ["Knockdowns", "SigStrikesLanded", "SigStrikesAttempted", "SigHeadStrikesLanded",
                 "SigBodyStrikesLanded", "SigLegStrikesLanded", "SigDistanceStrikesLanded",
                 "SigClinchStrikesLanded", "SigGroundStrikesLanded", "TakedownsLanded", "TakedownsAttempted",
                 "SubmissionsAttempted", "ControlSeconds"]

# Weight limits, for weight-class changes and missed weight. Catch Weight and Open Weight
# fights have no stored limit.
LIMITS = {"Flyweight": 125, "Bantamweight": 135, "Featherweight": 145, "Lightweight": 155,
          "Welterweight": 170, "Middleweight": 185, "Light Heavyweight": 205, "Heavyweight": 265,
          "Women's Strawweight": 115, "Women's Flyweight": 125, "Women's Bantamweight": 135,
          "Women's Featherweight": 145}

# Every per-fighter feature. The models use differences (fighter A's minus fighter B's).
PER_FIGHTER = {
    "rating": "Glicko rating after their last fight",
    "rd": "Glicko rating deviation after their last fight",
    "age": "Age on the day of the fight",
    "height": "Height",
    "reach": "Reach",
    "southpaw": "Stance (southpaw 1, switch 0.5, orthodox 0)",
    "slpm": "Significant strikes landed per minute",
    "sapm": "Significant strikes absorbed per minute",
    "str_acc": "Significant strike accuracy",
    "str_def": "Significant strike defense",
    "kd15": "Knockdowns landed per 15 minutes",
    "kd_against15": "Knockdowns suffered per 15 minutes",
    "td15": "Takedowns per 15 minutes",
    "td_acc": "Takedown accuracy",
    "td_def": "Takedown defense",
    "sub15": "Submission attempts per 15 minutes",
    "ctrl": "Net control time (share of fight time)",
    "distance_share": "Share of significant strikes landed at distance",
    "ground_share": "Share of significant strikes landed on the ground",
    "head_share": "Share of significant strikes landed to the head",
    "finish_rate": "KO/TKO and submission wins per UFC fight",
    "ko_loss_rate": "KO/TKO losses per UFC fight",
    "round_share": "Share of rounds won on the judges' scorecards",
    "last3": "Recent form (last three results)",
    "log_layoff": "Time since their last UFC fight",
    "log_fights": "Number of UFC fights",
    "log_minutes": "Total UFC cage time",
    "off_ko_loss": "Coming off a KO/TKO loss",
    "five_round": "Five-round fights so far",
    "class_change": "Moving up (+1) or down (-1) a weight class",
    "past_misses": "Missed weight before (per UFC fight)",
}
CONTEXT = {"scheduled_rounds": "Scheduled rounds", "title_fight": "Title fight"}


@dataclass(frozen=True)
class Shrinkage:
    """How much evidence each shrunk stat needs before it moves away from the class average.

    Rates are in pseudo-minutes of cage time; percentages in pseudo-attempts. Estimated by
    `estimate_shrinkage` from fights before the validation years, then held fixed.
    """
    minutes: dict = field(default_factory=dict)   # kd, sig, sig_against, td, sub, ctrl -> pseudo-minutes
    attempts: dict = field(default_factory=dict)  # sig_acc, td_acc, distance, ground, head -> pseudo-attempts


@dataclass(frozen=True)
class FeatureParams:
    glicko: GlickoParams
    shrink: Shrinkage


class FighterState:
    """Everything known about one fighter from their UFC fights so far."""
    __slots__ = ("fights", "results", "ko_wins", "sub_wins", "ko_losses", "last_date", "off_ko_loss",
                 "cage_seconds", "own", "against", "rounds_won", "rounds_scored", "five_round",
                 "last_class", "misses")

    def __init__(self):
        self.fights = 0
        self.results = []           # 1 win, 0.5 draw, 0 loss; no contests aren't results
        self.ko_wins = self.sub_wins = self.ko_losses = 0
        self.last_date = None
        self.off_ko_loss = 0.0
        self.cage_seconds = 0.0     # every finished fight, with stats or not
        self.own = np.zeros(len(STAT_FIELDS))      # totals over fights with official stats
        self.against = np.zeros(len(STAT_FIELDS))  # what opponents did in those fights
        self.rounds_won = self.rounds_scored = 0
        self.five_round = 0
        self.last_class = None
        self.misses = 0


@dataclass
class Features:
    """One row per fight (finished or upcoming): meta columns, then a_<feature> for the
    red corner and b_<feature> for the blue corner, each from before the fight."""
    rows: pd.DataFrame
    states: dict        # fighter id -> FighterState after the last event walked
    glicko: Glicko2


def estimate_shrinkage(dataset, before):
    """Method-of-moments empirical Bayes: how spread out fighters really are, net of noise.

    For a rate (counts over minutes) the prior strength is mean / between-fighter variance,
    in minutes; for a percentage it's p(1-p) / between-fighter variance - 1, in attempts.
    Uses career totals from fights before `before` only.
    """
    f = dataset.fights
    early = f[f.finished & (f.Date < pd.Timestamp(before)) & (f.OfficialStats == True)]  # noqa: E712
    s = dataset.stats.merge(early[["Id", "DurationSeconds"]], left_on="FightId", right_on="Id")
    opp = s[["FightId", "FighterId", "SigStrikesLanded"]].rename(
        columns={"FighterId": "OppId", "SigStrikesLanded": "SigAgainst"})
    s = s.merge(opp, on="FightId").query("FighterId != OppId")
    t = s.groupby("FighterId").sum(numeric_only=True)
    t["minutes"] = t.DurationSeconds / 60
    t = t[t.minutes >= 5]

    def rate_strength(count):
        x, m = t[count].to_numpy(float), t.minutes.to_numpy(float)
        mean = x.sum() / m.sum()
        between = (np.sum(m * (x / m - mean) ** 2) - (len(m) - 1) * mean) / (m.sum() - np.sum(m ** 2) / m.sum())
        return float(np.clip(mean / max(between, 1e-9), 1, 300))

    def share_strength(hits, tries):
        keep = t[tries] > 0
        y, n = t.loc[keep, hits].to_numpy(float), t.loc[keep, tries].to_numpy(float)
        p = y.sum() / n.sum()
        between = (np.sum(n * (y / n - p) ** 2) - (len(n) - 1) * p * (1 - p)) / (n.sum() - np.sum(n ** 2) / n.sum())
        return float(np.clip(p * (1 - p) / max(between, 1e-9) - 1, 1, 3000))

    t["ControlMinutes"] = t.ControlSeconds / 60
    return Shrinkage(
        minutes={"kd": rate_strength("Knockdowns"), "sig": rate_strength("SigStrikesLanded"),
                 "sig_against": rate_strength("SigAgainst"), "td": rate_strength("TakedownsLanded"),
                 "sub": rate_strength("SubmissionsAttempted"), "ctrl": rate_strength("ControlMinutes")},
        attempts={"sig_acc": share_strength("SigStrikesLanded", "SigStrikesAttempted"),
                  "td_acc": share_strength("TakedownsLanded", "TakedownsAttempted"),
                  "distance": share_strength("SigDistanceStrikesLanded", "SigStrikesLanded"),
                  "ground": share_strength("SigGroundStrikesLanded", "SigStrikesLanded"),
                  "head": share_strength("SigHeadStrikesLanded", "SigStrikesLanded")})


class _League:
    """Running totals of every fighter-fight with stats, per weight class, for the priors."""
    MIN_FIGHTS = 50  # below this many fighter-fights, a class borrows the all-class average

    def __init__(self):
        self.totals = {}
        self.counts = {}

    def add(self, weight_class, vector):
        for key in (weight_class, "all"):
            self.totals[key] = self.totals.get(key, 0) + vector
            self.counts[key] = self.counts.get(key, 0) + 1

    def prior(self, weight_class):
        key = weight_class if self.counts.get(weight_class, 0) >= self.MIN_FIGHTS else "all"
        v = self.totals.get(key)
        if v is None or v[_I["seconds"]] == 0:
            return None
        minutes = v[_I["seconds"]] / 60

        def share(a, b):
            return v[_I[a]] / v[_I[b]] if v[_I[b]] else np.nan

        return {"kd": v[_I["kd"]] / minutes, "sig": v[_I["sig"]] / minutes, "td": v[_I["td"]] / minutes,
                "sub": v[_I["sub"]] / minutes, "sig_acc": share("sig", "sig_att"), "td_acc": share("td", "td_att"),
                "distance": share("distance", "sig"), "ground": share("ground", "sig"), "head": share("head", "sig")}


def _stance_code(stance):
    return {"Southpaw": 1.0, "Switch": 0.5, "Orthodox": 0.0}.get(stance, np.nan)


def _positive(x):
    return float(x) if pd.notna(x) and x > 48 else np.nan


def fighter_features(st, info, rating, rd, prior, shrink, date, weight_class):
    """One fighter's features going into a fight, from their state before the card."""
    km, ka = shrink.minutes, shrink.attempts
    own, opp = st.own, st.against
    minutes = own[_I["seconds"]] / 60

    def rate(count, key, mean):
        return (count + km[key] * mean) / (minutes + km[key])

    def share(hits, tries, key, mean):
        return (hits + ka[key] * mean) / (tries + ka[key])

    if prior is None:
        stats = dict.fromkeys(["slpm", "sapm", "str_acc", "str_def", "kd15", "kd_against15", "td15", "td_acc",
                               "td_def", "sub15", "ctrl", "distance_share", "ground_share", "head_share"], np.nan)
    else:
        p = prior
        stats = {
            "slpm": rate(own[_I["sig"]], "sig", p["sig"]),
            "sapm": rate(opp[_I["sig"]], "sig_against", p["sig"]),
            "str_acc": share(own[_I["sig"]], own[_I["sig_att"]], "sig_acc", p["sig_acc"]),
            "str_def": 1 - share(opp[_I["sig"]], opp[_I["sig_att"]], "sig_acc", p["sig_acc"]),
            "kd15": 15 * rate(own[_I["kd"]], "kd", p["kd"]),
            "kd_against15": 15 * rate(opp[_I["kd"]], "kd", p["kd"]),
            "td15": 15 * rate(own[_I["td"]], "td", p["td"]),
            "td_acc": share(own[_I["td"]], own[_I["td_att"]], "td_acc", p["td_acc"]),
            "td_def": 1 - share(opp[_I["td"]], opp[_I["td_att"]], "td_acc", p["td_acc"]),
            "sub15": 15 * rate(own[_I["sub"]], "sub", p["sub"]),
            # The league average of net control is zero by construction.
            "ctrl": (own[_I["ctrl"]] - opp[_I["ctrl"]]) / 60 / (minutes + km["ctrl"]),
            "distance_share": share(own[_I["distance"]], own[_I["sig"]], "distance", p["distance"]),
            "ground_share": share(own[_I["ground"]], own[_I["sig"]], "ground", p["ground"]),
            "head_share": share(own[_I["head"]], own[_I["sig"]], "head", p["head"]),
        }

    n = st.fights
    last3 = st.results[-3:]
    limit_now, limit_before = LIMITS.get(weight_class), LIMITS.get(st.last_class)
    dob = info.DateOfBirth if info is not None else pd.NaT
    return {
        "rating": rating,
        "rd": rd,
        "age": (date - dob).days / 365.25 if pd.notna(dob) else np.nan,
        "height": _positive(info.HeightInches) if info is not None else np.nan,
        "reach": _positive(info.ReachInches) if info is not None else np.nan,
        "southpaw": _stance_code(info.Stance) if info is not None else np.nan,
        **stats,
        "finish_rate": (st.ko_wins + st.sub_wins) / (n + 1),
        "ko_loss_rate": st.ko_losses / (n + 1),
        # Smoothed toward an even split, so a fighter with few scored rounds sits near 50%.
        "round_share": (st.rounds_won + 1.5) / (st.rounds_scored + 3),
        "last3": (sum(last3) + 1) / (len(last3) + 2),
        "log_layoff": math.log1p(max((date - st.last_date).days, 0)) if st.last_date is not None else np.nan,
        "log_fights": math.log1p(n),
        "log_minutes": math.log1p(st.cage_seconds / 60),
        "off_ko_loss": st.off_ko_loss,
        "five_round": math.log1p(st.five_round),
        "class_change": float(np.sign(limit_now - limit_before)) if limit_now and limit_before else np.nan,
        "past_misses": st.misses / (n + 1),
    }


def _round_winners(rows):
    """{fighter id: rounds won} for one fight: each round goes to whoever most judges scored higher."""
    won = {}
    for _, by_round in rows.groupby("Round"):
        votes = {}
        for _, judge in by_round.groupby("JudgeId"):
            if len(judge) != 2:
                continue
            (fa, sa), (fb, sb) = zip(judge.FighterId, judge.Score)
            if pd.notna(sa) and pd.notna(sb) and sa != sb:
                winner = fa if sa > sb else fb
                votes[winner] = votes.get(winner, 0) + 1
        if votes:
            best = max(votes.values())
            leaders = [f for f, v in votes.items() if v == best]
            if len(leaders) == 1:
                won[leaders[0]] = won.get(leaders[0], 0) + 1
    return won, rows.Round.nunique()


def build(dataset, params):
    """Features for every fight in `dataset`, each from that fight's past only."""
    stats = {(r.FightId, r.FighterId): np.array([0.0, *[getattr(r, c) for c in _STAT_COLUMNS]], dtype=float)
             for r in dataset.stats.itertuples(index=False)}
    rounds = {fid: _round_winners(g) for fid, g in dataset.round_scores.groupby("FightId")}
    fighters = dataset.fighters
    states, league, glicko = {}, _League(), Glicko2(params.glicko)

    def state(pid):
        if pid not in states:
            states[pid] = FighterState()
        return states[pid]

    rows = []
    for _, card in dataset.fights.groupby("event_rank", sort=True):
        date = card.Date.iloc[0]
        # 1. Every fight on the card, from the state before the card.
        for f in card.itertuples(index=False):
            prior = league.prior(f.WeightClass)
            row = {"fight_id": f.Id, "event_id": f.EventId, "date": date, "red_id": f.RedId, "blue_id": f.BlueId,
                   "finished": f.finished, "result": f.result, "method": f.method_group,
                   "ending_round": f.EndingRound, "scheduled_rounds": f.ScheduledRounds, "modern": f.modern,
                   "title_fight": float(f.IsTitleFight), "weight_class": f.WeightClass}
            for side, pid in (("a", f.RedId), ("b", f.BlueId)):
                rating, rd = glicko.state(pid)
                info = fighters.loc[pid] if pid in fighters.index else None
                for key, value in fighter_features(state(pid), info, rating, rd, prior, params.shrink,
                                                   date, f.WeightClass).items():
                    row[f"{side}_{key}"] = value
            rows.append(row)

        # 2. Then the card's results.
        games = []
        for f in card[card.finished].itertuples(index=False):
            red, blue = state(f.RedId), state(f.BlueId)
            score = {"red": 1.0, "blue": 0.0, "draw": 0.5}.get(f.result)
            if score is not None:
                games.append((f.RedId, f.BlueId, score))
            both = stats.get((f.Id, f.RedId)), stats.get((f.Id, f.BlueId))
            # Unofficial stats wait until UFC.com marks them official; the result still counts.
            has_stats = f.OfficialStats == True and both[0] is not None and both[1] is not None and f.DurationSeconds > 0  # noqa: E712
            won, scored = rounds.get(f.Id, ({}, 0))
            limit = LIMITS.get(f.WeightClass)
            for st, pid, mine, theirs, weigh_in, me_score in (
                    (red, f.RedId, both[0], both[1], f.RedWeighIn, score),
                    (blue, f.BlueId, both[1], both[0], f.BlueWeighIn, None if score is None else 1 - score)):
                st.fights += 1
                st.last_date = date
                st.cage_seconds += f.DurationSeconds if pd.notna(f.DurationSeconds) else 0
                if me_score is not None:
                    st.results.append(me_score)
                won_it, lost_it = me_score == 1.0, me_score == 0.0
                st.ko_wins += won_it and f.method_group == "ko"
                st.sub_wins += won_it and f.method_group == "sub"
                st.ko_losses += lost_it and f.method_group == "ko"
                st.off_ko_loss = float(lost_it and f.method_group == "ko")
                st.five_round += f.ScheduledRounds == 5
                st.last_class = f.WeightClass
                if limit and pd.notna(weigh_in) and weigh_in > limit + (0 if f.IsTitleFight else 1):
                    st.misses += 1
                if scored:
                    st.rounds_won += won.get(pid, 0)
                    st.rounds_scored += scored
                if has_stats:
                    mine = mine.copy()
                    theirs = theirs.copy()
                    mine[_I["seconds"]] = theirs[_I["seconds"]] = f.DurationSeconds
                    st.own += mine
                    st.against += theirs
            if has_stats:
                for v in both:
                    v = v.copy()
                    v[_I["seconds"]] = f.DurationSeconds
                    league.add(f.WeightClass, v)
        glicko.rate_event(date, games)

    return Features(pd.DataFrame(rows), states, glicko)


def tune_glicko(dataset, start, end, grid):
    """Glicko settings ranked by how much their pre-fight predictions know about decided
    fights in [start, end). Every prediction is made before its fight, so this is out of
    sample even though no model is fit.

    Glicko's own probabilities are overconfident for MMA (outcomes are noisier than its
    curve assumes; the best fixed rescaling of the logit is about 0.5), so settings are
    compared on log loss after one fitted scale factor. The models only use the rating
    difference, so its information is what matters, not its raw scale.
    """
    from sklearn.linear_model import LogisticRegression

    f = dataset.fights
    lo, hi = pd.Timestamp(start), pd.Timestamp(end)
    results = []
    for params in grid:
        g = Glicko2(params)
        ps, ys = [], []
        for _, card in f.groupby("event_rank", sort=True):
            date = card.Date.iloc[0]
            games = []
            for r in card[card.finished].itertuples(index=False):
                score = {"red": 1.0, "blue": 0.0, "draw": 0.5}.get(r.result)
                if score is None:
                    continue
                if score != 0.5 and lo <= date < hi:
                    ps.append(g.expected(r.RedId, r.BlueId, date))
                    ys.append(score)
                games.append((r.RedId, r.BlueId, score))
            g.rate_event(date, games)
        p, y = np.clip(np.array(ps), 1e-6, 1 - 1e-6), np.array(ys)
        z = np.log(p / (1 - p))
        scale = LogisticRegression(fit_intercept=False, C=1e6).fit(
            np.concatenate([z, -z])[:, None], np.concatenate([y, 1 - y])).coef_[0, 0]
        q = 1 / (1 + np.exp(-scale * z))
        results.append((float(-np.mean(y * np.log(q) + (1 - y) * np.log(1 - q))), params))
    results.sort(key=lambda x: x[0])
    return results
