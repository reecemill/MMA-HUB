"""Train the fight prediction model and export it for the website.

    python ml/train.py [path/to/mma.db] [--dry-run]     # default: data/mma.db

Reads the SQLite snapshot (see SnapshotExporter.cs) of the UFC.com data, walks every
UFC fight in date order, and builds each fighter's features only from fights *before*
the one being predicted, so the model never sees a result it's asked to predict.
Striking and grappling rates come from UFC.com's stats for each fight, accumulated
fight by fight; the career totals UFC.com shows aren't used, since they already
include the fights being predicted.

Compares logistic regression, gradient boosting and a small neural network on
the validation years, then reports every family once on the test years.

Unless --dry-run, writes src/Web/Data/model.json (weights and test results) and
src/Web/Data/fighter_profiles.json (each fighter's latest features). The site
predicts upcoming and dream fights from those two files.
"""

import json
import math
import sqlite3
import sys
from dataclasses import dataclass, fields
from pathlib import Path

import numpy as np
import pandas as pd
from sklearn.ensemble import HistGradientBoostingClassifier
from sklearn.linear_model import LogisticRegression
from sklearn.metrics import accuracy_score, brier_score_loss, log_loss
from sklearn.neural_network import MLPClassifier
from sklearn.pipeline import make_pipeline
from sklearn.preprocessing import StandardScaler

ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / "src" / "Web" / "Data"

# Held-out periods: choose settings on validation, report once on test.
VALIDATION_START = "2022-01-01"
TEST_START = "2024-01-01"

ELO_START = 1500.0
# A debutant starts at the average Elo of fighters active in this many years before their
# debut. Fighters who are cut leave with low ratings, so the active roster sits above
# ELO_START, and starting newcomers at 1500 underrated every one of them.
ACTIVE_YEARS = 2
# On top of that, Elo points per unit of pre-UFC win rate above or below the average debutant's,
# so an unbeaten prospect starts higher than a fighter with several losses. Chosen on validation
# with K; 0 is the plain roster average. A full grid (0, 250, 500, 1000) on 2026-11-22 picked 500
# for every K (validation log loss 0.6474 -> 0.6465 at K=128) but took an hour to run, so only
# 0 (the comparison, and BEFORE's setting) and 500 are kept.
DEBUT_SCALES = (0, 500)
KO_METHODS = {"KO/TKO", "TKO - Doctor's Stoppage"}
SUB_METHODS = {"Submission"}

# Every feature is (fighter A's value) - (fighter B's value). Labels are shown on the
# site next to each prediction's biggest reasons.
FEATURES = {
    "elo": "Elo rating",
    "log_fights": "UFC experience",
    "win_pct": "UFC win rate",
    "last3": "Recent form (last 3)",
    "streak": "Current streak",
    "ko_rate": "KO/TKO wins per fight",
    "sub_rate": "Submission wins per fight",
    "finished_rate": "Times finished per fight",
    "log_layoff": "Time since last fight",
    "age": "Age",
    "height": "Height",
    "reach": "Reach",
    "southpaw": "Southpaw stance",
    # From UFC.com's per-fight stats, over the fighter's earlier UFC fights.
    "slpm": "Strikes landed per minute",
    "sapm": "Strikes absorbed per minute",
    "str_acc": "Striking accuracy",
    "str_def": "Striking defense",
    "kd15": "Knockdowns per 15 minutes",
    "td15": "Takedowns per 15 minutes",
    "td_acc": "Takedown accuracy",
    "td_def": "Takedown defense",
    "ctrl": "Net control time",
    "sub15": "Submission attempts per 15 minutes",
    # Matchups: one fighter's attack against the other's defense, minus the reverse.
    "strike_matchup": "Striking matchup",
    "grapple_matchup": "Grappling matchup",
}
PAIR_FEATURES = {"strike_matchup", "grapple_matchup"}

_RECORD = ["elo", "win_pct", "log_fights", "last3", "streak"]
_FINISHING = ["ko_rate", "sub_rate", "finished_rate"]
_CURRENT = _RECORD + _FINISHING + ["log_layoff"]
_STRIKING = ["slpm", "sapm", "str_acc", "str_def", "kd15"]
_GRAPPLING = ["td15", "td_acc", "td_def", "ctrl", "sub15"]
_MATCHUPS = ["strike_matchup", "grapple_matchup"]
FEATURE_SETS = {
    "win rate only": ["win_pct"],
    "record": _RECORD,
    "record + finishing": _RECORD + _FINISHING,
    "record + finishing + layoff": _CURRENT,
    "+ striking": _CURRENT + _STRIKING,
    "+ grappling": _CURRENT + _GRAPPLING,
    "+ striking + grappling": _CURRENT + _STRIKING + _GRAPPLING,
    "+ striking + grappling + age": _CURRENT + _STRIKING + _GRAPPLING + ["age"],
    "+ striking + grappling + matchups": _CURRENT + _STRIKING + _GRAPPLING + _MATCHUPS,
    "+ stats + matchups + age": _CURRENT + _STRIKING + _GRAPPLING + _MATCHUPS + ["age"],
    "everything": list(FEATURES),
}
# The model the site used before per-fight stats: the baseline every change is measured against.
BEFORE = {"elo": (128, 0), "features": _CURRENT}
# Gradient boosting and the network only try the feature sets with per-fight stats.
NONLINEAR_SETS = ["+ striking + grappling", "+ striking + grappling + age", "+ stats + matchups + age"]
# Pseudo-counts that pull a fighter's rates toward the league average until there's data.
PRIOR_MINUTES, PRIOR_STRIKES, PRIOR_TAKEDOWNS = 15.0, 20.0, 5.0
# The first year a model's weights learn from (None: every year). Fighters' histories always
# use every earlier fight; this only drops early-era bouts from the rows the weights fit.
TRAIN_SINCE = [None, "2005-01-01", "2010-01-01", "2015-01-01"]


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


def load(db_path):
    """Every fight with both corners known, the fighters, and each fighter's stats per
    fight, keyed by (fight id, fighter id)."""
    con = sqlite3.connect(db_path)
    fights = pd.read_sql(
        """select f.Id, e.Date as EventDate, f.Status, f.Method, f.WinnerFighterId, f.DurationSeconds,
                  red.FighterId as Fighter1Id, blue.FighterId as Fighter2Id, red.Outcome as Fighter1Outcome
           from UfcFights f
           join UfcEvents e on e.Id = f.EventId
           join UfcFightFighters red on red.FightId = f.Id and red.Corner = 'Red'
           join UfcFightFighters blue on blue.FightId = f.Id and blue.Corner = 'Blue'
           where e.Date is not null""",
        con, parse_dates=["EventDate"])
    fighters = pd.read_sql(
        """select Id, Slug, FirstName || ' ' || LastName as Name, WeightClass as Division,
                  DateOfBirth, HeightInches, ReachInches, Stance, Wins, Losses
           from UfcFighters""",
        con, parse_dates=["DateOfBirth"])
    rows = pd.read_sql(
        """select s.FightId, s.FighterId, f.DurationSeconds, s.Knockdowns, s.SigStrikesLanded, s.SigStrikesAttempted,
                  s.TakedownsLanded, s.TakedownsAttempted, s.SubmissionsAttempted, s.ControlSeconds
           from UfcFightStats s join UfcFights f on f.Id = s.FightId
           where f.DurationSeconds > 0""", con)
    con.close()

    # UFC.com marks a few 1990s fights "Over" rather than "Final".
    fights["Status"] = np.select([fights.Status.isin(["Final", "Over"]), fights.Status == "Upcoming"],
                                 ["completed", "upcoming"], fights.Status)
    # Within a date, the card's order isn't a time order; sort by id so runs are repeatable.
    fights = fights.sort_values(["EventDate", "Id"], kind="stable").reset_index(drop=True)

    stats = {(r.FightId, r.FighterId): FightStats(
        r.DurationSeconds / 60, r.SigStrikesLanded, r.SigStrikesAttempted, r.Knockdowns,
        r.TakedownsLanded, r.TakedownsAttempted, r.SubmissionsAttempted, r.ControlSeconds)
        for r in rows.itertuples(index=False)}
    return fights, fighters, stats


def lookup(stats, fight_id, fighter_a, fighter_b):
    """Both fighters' stats for a fight, or None if either is missing."""
    a, b = stats.get((fight_id, fighter_a)), stats.get((fight_id, fighter_b))
    return (a, b) if a is not None and b is not None else None


def physical_attributes(fighters, snapshot_date):
    """Height, reach, stance, date of birth, and an age reference per fighter, with gaps filled.

    Missing or zero height gets the median for the fighter's division; missing reach
    is set to height (the typical ratio is close to 1). The age reference is only used
    for the few fighters without a date of birth: the median age at the snapshot.
    """
    f = fighters.copy()
    f["height"] = pd.to_numeric(f["HeightInches"], errors="coerce").where(lambda h: h > 48)
    f["reach"] = pd.to_numeric(f["ReachInches"], errors="coerce").where(lambda r: r > 48)
    f["height"] = f["height"].fillna(f.groupby("Division")["height"].transform("median"))
    f["height"] = f["height"].fillna(f["height"].median())
    f["reach"] = f["reach"].fillna(f["height"])
    stance = f["Stance"].fillna("")
    f["southpaw"] = np.select([stance == "Southpaw", stance == "Switch"], [1.0, 0.5], 0.0)
    f["birth"] = f["DateOfBirth"]
    f["age_ref"] = ((snapshot_date - f["birth"]).dt.days / 365.25).median()
    return f.set_index("Id")[["Slug", "Name", "height", "reach", "southpaw", "age_ref", "birth"]]


class FighterHistory:
    """What's known about one fighter from their UFC fights so far."""

    def __init__(self, elo=ELO_START):
        self.elo = elo
        self.fights = 0
        self.wins = 0
        self.losses = 0
        self.results = []  # 1 win, 0.5 draw, 0 loss
        self.ko_wins = 0
        self.sub_wins = 0
        self.finished = 0
        self.last_fight = None
        self.own = FightStats()       # totals over fights with per-fight stats
        self.against = FightStats()   # what opponents did in those fights

    def add_stats(self, own, against):
        self.own += own
        self.against += against

    def streak(self):
        s = 0
        for r in reversed(self.results):
            if r == 1 and s >= 0:
                s += 1
            elif r == 0 and s <= 0:
                s -= 1
            else:
                break
        return max(-5, min(5, s))

    def state(self, priors):
        """The part of the features that doesn't depend on the fight date."""
        n = self.fights
        last = self.results[-3:]
        o, a, p = self.own, self.against, priors
        minutes = o.minutes + PRIOR_MINUTES

        def per_minute(total, league_rate):
            return (total + PRIOR_MINUTES * league_rate) / minutes

        def ratio(landed, attempted, prior_n, league_ratio):
            return (landed + prior_n * league_ratio) / (attempted + prior_n)

        stats = {
            "slpm": per_minute(o.sig_landed, p["sig_per_min"]),
            "sapm": per_minute(a.sig_landed, p["sig_per_min"]),
            "str_acc": ratio(o.sig_landed, o.sig_attempted, PRIOR_STRIKES, p["sig_acc"]),
            "str_def": 1 - ratio(a.sig_landed, a.sig_attempted, PRIOR_STRIKES, p["sig_acc"]),
            "kd15": 15 * per_minute(o.knockdowns, p["kd_per_min"]),
            "td15": 15 * per_minute(o.td_landed, p["td_per_min"]),
            "td_acc": ratio(o.td_landed, o.td_attempted, PRIOR_TAKEDOWNS, p["td_acc"]),
            "td_def": 1 - ratio(a.td_landed, a.td_attempted, PRIOR_TAKEDOWNS, p["td_acc"]),
            # Share of fight time spent controlling minus being controlled; the league average is 0.
            "ctrl": (o.control_seconds - a.control_seconds) / (60 * minutes),
            "sub15": 15 * per_minute(o.sub_attempts, p["sub_per_min"]),
        }
        return {
            **stats,
            "elo": self.elo,
            "log_fights": math.log1p(n),
            # Smoothed toward 50% so a 1-0 debut record isn't read as a perfect fighter.
            "win_pct": (self.wins + 1) / (n + 2),
            "last3": (sum(last) + 1) / (len(last) + 2),
            "streak": float(self.streak()),
            "ko_rate": self.ko_wins / (n + 1),
            "sub_rate": self.sub_wins / (n + 1),
            "finished_rate": self.finished / (n + 1),
        }


def dated_features(state, last_fight, age_ref, birth, snapshot_date, date):
    """Adds the features that depend on when the fight happens.

    Age comes from the date of birth; the few fighters without one get the median age.
    """
    days = (date - last_fight).days if last_fight is not None else 0
    if pd.notna(birth):
        age = (date - birth).days / 365.25
    else:
        age = age_ref - (snapshot_date - date).days / 365.25
    return {**state, "log_layoff": math.log1p(max(days, 0)), "age": age}


def fighter_vector(history, phys, snapshot_date, date, priors):
    x = dated_features(history.state(priors), history.last_fight, phys.age_ref, phys.birth, snapshot_date, date)
    x.update(height=phys.height, reach=phys.reach, southpaw=phys.southpaw)
    return x


def pair_features(xa, xb):
    """Every feature for A against B: per-fighter differences, plus the matchup terms,
    which are A's attack against B's defense minus the reverse (so they flip sign too)."""
    row = {f: xa[f] - xb[f] for f in FEATURES if f not in PAIR_FEATURES}
    row["strike_matchup"] = xa["slpm"] * (1 - xb["str_def"]) - xb["slpm"] * (1 - xa["str_def"])
    row["grapple_matchup"] = xa["td15"] * (1 - xb["td_def"]) - xb["td15"] * (1 - xa["td_def"])
    return row


def league_priors(fights, stats, before):
    """League-average rates, from the per-fight stats of fights before `before`."""
    total = FightStats()
    for b in fights[(fights.Status == "completed") & (fights.EventDate < before)].itertuples():
        found = lookup(stats, b.Id, b.Fighter1Id, b.Fighter2Id)
        if found:
            total += found[0]
            total += found[1]
    return {
        "sig_per_min": total.sig_landed / total.minutes,
        "sig_acc": total.sig_landed / total.sig_attempted,
        "kd_per_min": total.knockdowns / total.minutes,
        "td_per_min": total.td_landed / total.minutes,
        "td_acc": total.td_landed / total.td_attempted,
        "sub_per_min": total.sub_attempts / total.minutes,
    }


def pre_ufc_records(fighters, fights):
    """Each fighter's record before the UFC: UFC.com's pro record minus their UFC record.

    UFC.com's record is today's, so for a fighter who fought elsewhere after their debut
    (cut and re-signed, say) it also counts those later fights: a small leak, accepted
    because it's exact for everyone who has only fought in the UFC since. Missing or
    impossible (negative) records give None.
    """
    ufc = {}
    for b in fights[fights.Status == "completed"].itertuples():
        for fid in (b.Fighter1Id, b.Fighter2Id):
            w, l = ufc.get(fid, (0, 0))
            won = b.WinnerFighterId == fid
            lost = b.WinnerFighterId in (b.Fighter1Id, b.Fighter2Id) and not won
            ufc[fid] = (w + won, l + lost)
    out = {}
    for r in fighters.itertuples():
        w, l = ufc.get(r.Id, (0, 0))
        if pd.isna(r.Wins) or pd.isna(r.Losses) or r.Wins < w or r.Losses < l:
            out[r.Id] = None
        else:
            out[r.Id] = (int(r.Wins) - w, int(r.Losses) - l)
    return out


class DebutSeeder:
    """Starting Elo for a fighter's first UFC fight: the active roster's average, adjusted by
    their pre-UFC win rate (smoothed toward 50%) against the average of earlier debutants'."""

    def __init__(self, pre_records, scale):
        self.pre = pre_records
        self.scale = scale
        self.total = 0.0
        self.count = 0

    def win_rate(self, fid):
        rec = self.pre.get(fid)
        return None if rec is None else (rec[0] + 1) / (rec[0] + rec[1] + 2)

    def elo(self, histories, date, fid):
        start = roster_elo(histories, date)
        rate = self.win_rate(fid)
        if rate is not None and self.count:
            start += self.scale * (rate - self.total / self.count)
        return start

    def seed(self, histories, date, fid):
        """A new fighter's history; the average then includes them, for later debutants."""
        h = FighterHistory(self.elo(histories, date, fid))
        rate = self.win_rate(fid)
        if rate is not None:
            self.total += rate
            self.count += 1
        return h


def roster_elo(histories, date):
    """The average Elo of fighters whose last fight was within ACTIVE_YEARS before `date`
    (ELO_START if there are none yet). Only uses fights already walked, so it's known
    before any fight on `date` is predicted."""
    since = date - pd.DateOffset(years=ACTIVE_YEARS)
    active = [h.elo for h in histories.values() if h.last_fight is not None and h.last_fight >= since]
    return float(np.mean(active)) if active else ELO_START


def walk_fights(fights, phys, snapshot_date, k, stats, priors, seeder):
    """Features for every completed fight (from history before it), and final histories.

    `seeder` (a DebutSeeder) gives each fighter's starting Elo; it's returned too, so
    fighters still to debut can be seeded the same way.

    A fight's own stats are added to each fighter's totals only after its features are
    recorded, like its result.
    """
    histories = {}
    rows = []
    seen = set()
    for b in fights.itertuples():
        if b.Status != "completed":
            continue
        assert b.Id not in seen, f"fight {b.Id} appears twice"
        seen.add(b.Id)
        a_id, b_id = b.Fighter1Id, b.Fighter2Id
        if a_id not in phys.index or b_id not in phys.index:
            continue
        for fid in (a_id, b_id):
            if fid not in histories:
                histories[fid] = seeder.seed(histories, b.EventDate, fid)
        ha, hb = histories[a_id], histories[b_id]

        if b.Fighter1Outcome == "Draw":
            score_a = 0.5
        elif b.WinnerFighterId == a_id:
            score_a = 1.0
        elif b.WinnerFighterId == b_id:
            score_a = 0.0
        else:
            continue  # no contest, overturned, or no recorded result

        if score_a != 0.5:
            xa = fighter_vector(ha, phys.loc[a_id], snapshot_date, b.EventDate, priors)
            xb = fighter_vector(hb, phys.loc[b_id], snapshot_date, b.EventDate, priors)
            rows.append({"date": b.EventDate, "fight": int(b.Id), "y": score_a, **pair_features(xa, xb)})

        found = lookup(stats, b.Id, a_id, b_id)
        if found:
            ha.add_stats(found[0], found[1])
            hb.add_stats(found[1], found[0])

        expected_a = 1 / (1 + 10 ** ((hb.elo - ha.elo) / 400))
        ha.elo += k * (score_a - expected_a)
        hb.elo += k * ((1 - score_a) - (1 - expected_a))
        for h, s in ((ha, score_a), (hb, 1 - score_a)):
            h.fights += 1
            h.wins += s == 1
            h.losses += s == 0
            h.results.append(s)
            h.last_fight = b.EventDate
        winner, loser = (ha, hb) if score_a == 1 else (hb, ha)
        if score_a != 0.5:
            winner.ko_wins += b.Method in KO_METHODS
            winner.sub_wins += b.Method in SUB_METHODS
            loser.finished += b.Method in KO_METHODS | SUB_METHODS
    return pd.DataFrame(rows), histories, seeder


def symmetric(df):
    """Every fight both ways round, so the model can't learn which slot the winner is in.

    (UFC.com lists the red corner first, usually the champion or the favorite.) With no
    intercept and difference features, P(A beats B) is then exactly 1 - P(B beats A).
    """
    flipped = df.copy()
    flipped[list(FEATURES)] = -flipped[list(FEATURES)]
    flipped["y"] = 1 - flipped["y"]
    return pd.concat([df, flipped], ignore_index=True)


def one_side(df):
    """Each fight once, with a random fighter as A, for fair accuracy numbers."""
    rng = np.random.default_rng(0)
    flip = rng.random(len(df)) < 0.5
    out = df.copy()
    out.loc[flip, list(FEATURES)] = -out.loc[flip, list(FEATURES)]
    out.loc[flip, "y"] = 1 - out.loc[flip, "y"]
    return out


class Model:
    """Logistic regression on scaled difference features, with no intercept."""

    def __init__(self, features, c=1.0):
        self.features = features
        self.c = c

    def fit(self, df):
        sym = symmetric(df)
        x = sym[self.features].to_numpy()
        self.scale = np.sqrt((x ** 2).mean(axis=0))  # the mean of a difference is 0
        self.lr = LogisticRegression(C=self.c, fit_intercept=False, max_iter=2000)
        self.lr.fit(x / self.scale, sym["y"])
        return self

    def predict(self, df):
        return self.lr.predict_proba(df[self.features].to_numpy() / self.scale)[:, 1]

    def describe(self):
        return f"C={self.c}"


class SymmetricModel:
    """A model that isn't symmetric by construction (trees, a network), made symmetric:
    trained on every fight both ways round, and predicting the average of P(A beats B)
    and 1 - P(B beats A), so the two always add to 1."""

    def __init__(self, features, make, settings):
        self.features = features
        self.make = make
        self.settings = settings

    def fit(self, df):
        sym = symmetric(df)
        self.model = self.make(**self.settings).fit(sym[self.features].to_numpy(), sym["y"])
        return self

    def predict(self, df):
        x = df[self.features].to_numpy()
        return (self.model.predict_proba(x)[:, 1] + 1 - self.model.predict_proba(-x)[:, 1]) / 2

    def describe(self):
        return ", ".join(f"{k}={v}" for k, v in self.settings.items())


def boosted_trees(**settings):
    return HistGradientBoostingClassifier(min_samples_leaf=40, l2_regularization=1.0, random_state=0, **settings)


def neural_network(**settings):
    return make_pipeline(StandardScaler(), MLPClassifier(early_stopping=True, max_iter=500, random_state=0, **settings))


# Each family's settings to search on the validation years.
FAMILIES = {
    "logistic regression": [lambda feats: Model(feats, 1.0)],
    "gradient boosting": [
        (lambda s: lambda feats: SymmetricModel(feats, boosted_trees, s))(s)
        for s in ({"learning_rate": 0.05, "max_leaf_nodes": 15, "max_iter": 200},
                  {"learning_rate": 0.05, "max_leaf_nodes": 31, "max_iter": 300},
                  {"learning_rate": 0.1, "max_leaf_nodes": 7, "max_iter": 150})],
    "neural network": [
        (lambda s: lambda feats: SymmetricModel(feats, neural_network, s))(s)
        for s in ({"hidden_layer_sizes": (16,), "alpha": 1e-2},
                  {"hidden_layer_sizes": (32,), "alpha": 1e-2},
                  {"hidden_layer_sizes": (32, 16), "alpha": 1e-2},
                  {"hidden_layer_sizes": (32, 16), "alpha": 1e-1})],
}


def scores(y, p):
    return {"accuracy": accuracy_score(y, p > 0.5), "log_loss": log_loss(y, p, labels=[0, 1]),
            "brier": brier_score_loss(y, p), "bouts": int(len(y))}


def learning_rows(data, since, before):
    """The fights a model's weights are fitted on: from `since` (or the start) up to `before`."""
    rows = data[data.date < before]
    return rows if since is None else rows[rows.date >= since]


def paired_bootstrap(y, p_new, p_old, draws=2000):
    """Mean and 95% interval of (new - old) in log loss and in accuracy, resampling test fights.

    `p_old` may be hard 0/1 picks (a rule), in which case only accuracy is compared.
    """
    rng = np.random.default_rng(0)
    y = np.asarray(y, dtype=float)
    idx = rng.integers(0, len(y), (draws, len(y)))
    out = {}
    correct_new, correct_old = (p_new > 0.5) == y, (p_old > 0.5) == y
    d = (correct_new[idx].mean(axis=1) - correct_old[idx].mean(axis=1))
    out["accuracy"] = (correct_new.mean() - correct_old.mean(), *np.percentile(d, [2.5, 97.5]))
    if 0 < p_old.min() and p_old.max() < 1:
        loss = lambda p: -(y * np.log(p) + (1 - y) * np.log(1 - p))
        loss_new, loss_old = loss(p_new), loss(p_old)
        d = loss_new[idx].mean(axis=1) - loss_old[idx].mean(axis=1)
        out["log_loss"] = (loss_new.mean() - loss_old.mean(), *np.percentile(d, [2.5, 97.5]))
    return out


def print_comparison(label, comparison):
    parts = [f"{metric} {mean:+.4f} [{lo:+.4f}, {hi:+.4f}]" for metric, (mean, lo, hi) in comparison.items()]
    print(f"    {label:<42} " + "   ".join(parts))


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    dry_run = "--dry-run" in sys.argv
    db_path = Path(args[0]) if args else ROOT / "data" / "mma.db"
    fights, fighters, stats = load(db_path)
    last_result = fights.loc[fights.Status == "completed", "EventDate"].max()
    snapshot_date = last_result
    phys = physical_attributes(fighters, snapshot_date)
    priors = league_priors(fights, stats, pd.Timestamp(VALIDATION_START))

    completed = fights[fights.Status == "completed"]
    has_stats = np.array([lookup(stats, i, a, b) is not None
                          for i, a, b in zip(completed.Id, completed.Fighter1Id, completed.Fighter2Id)])
    in_test = (completed.EventDate >= TEST_START).to_numpy()
    print(f"Per-fight stats found for {has_stats.mean():.1%} of {len(completed)} completed fights "
          f"({has_stats[in_test].mean():.1%} in the test years); dates of birth for "
          f"{phys.birth.notna().mean():.1%} of fighters")

    # --- Choose settings on the validation years -------------------------------------
    # Each family searches Elo K, feature sets, the first year its weights learn from, and its
    # own settings; the lowest log loss wins. Height and reach stay candidates in "everything"
    # so they keep being checked. Each walk is one Elo setting: (K, debut scale).
    pre = pre_ufc_records(fighters, fights)
    walks = {(k, d): walk_fights(fights, phys, snapshot_date, k, stats, priors, DebutSeeder(pre, d))
             for k in (32, 64, 128) for d in DEBUT_SCALES}
    print("\nValidation (train before 2022, test 2022-2023):")
    best = {}
    for family, makers in FAMILIES.items():
        sets = FEATURE_SETS if family == "logistic regression" else {n: FEATURE_SETS[n] for n in NONLINEAR_SETS}
        for (k, debut), (data, _, _) in walks.items():
            valid = one_side(data[(data.date >= VALIDATION_START) & (data.date < TEST_START)])
            for since in TRAIN_SINCE:
                train = learning_rows(data, since, VALIDATION_START)
                for name, feats in sets.items():
                    for make in makers:
                        model = make(feats).fit(train)
                        s = scores(valid.y, model.predict(valid))
                        print(f"  {family:<20} K={k:<4} debut {debut:<5} since {since or 'all':<10} {name:<34} {model.describe():<48} "
                              f"accuracy {s['accuracy']:.3f}  log loss {s['log_loss']:.4f}")
                        if family not in best or s["log_loss"] < best[family]["log_loss"]:
                            best[family] = {"log_loss": s["log_loss"], "elo": (k, debut), "since": since, "name": name,
                                            "feats": feats, "make": make, "describe": model.describe()}
    print("\nBest of each family on validation:")
    for family, b in best.items():
        print(f"  {family:<20} log loss {b['log_loss']:.4f}  K={b['elo'][0]}, debut {b['elo'][1]}, since {b['since'] or 'all'}, "
              f"{b['name']}, {b['describe']}")

    # --- Report once on the test years -----------------------------------------------
    def test_predictions(elo, since, feats, make):
        data = walks[elo][0]
        model = make(feats).fit(learning_rows(data, since, TEST_START))
        test = one_side(data[data.date >= TEST_START])
        return model, test, model.predict(test)

    family_test = {}
    for family, b in best.items():
        family_model, test, family_test[family] = test_predictions(b["elo"], b["since"], b["feats"], b["make"])
        # P(A beats B) + P(B beats A) must be exactly 1 for every model.
        flipped = test.copy()
        flipped[list(FEATURES)] = -flipped[list(FEATURES)]
        assert np.allclose(family_test[family] + family_model.predict(flipped), 1), family
    _, _, p_before = test_predictions(BEFORE["elo"], None, BEFORE["features"], lambda f: Model(f, 1.0))

    # The site runs logistic regression, so that's what's exported; the other families are
    # reported next to it to show whether a non-linear model would be worth deploying.
    site = best["logistic regression"]
    (k, debut), since, name, feats = site["elo"], site["since"], site["name"], site["feats"]
    data, histories, seeder = walks[(k, debut)]
    train = data[data.date < TEST_START]
    model, test, p = test_predictions((k, debut), since, feats, site["make"])
    c = 1.0
    elo_only = Model(["elo"], c).fit(train).predict(test)
    # Baseline with no model: pick whoever has the better UFC win rate (coin flip on ties).
    rate = test["win_pct"].to_numpy()
    rng = np.random.default_rng(1)
    rule = np.where(rate > 0, 1.0, np.where(rate < 0, 0.0, rng.random(len(rate)) < 0.5))

    # New minus old, as [mean, low, high] of a 95% interval; the site's How It Works page
    # words its claims from these, so they stay true when the model is retrained.
    comparisons = {
        "vs before fight stats": paired_bootstrap(test.y, p, p_before),
        "vs better win rate": paired_bootstrap(test.y, p, rule),
        **{f"{family} vs logistic regression": paired_bootstrap(test.y, family_test[family], p)
           for family in family_test if family != "logistic regression"},
    }
    results = {
        "model": scores(test.y, p),
        "before_fight_stats": scores(test.y, p_before),
        "elo_only": scores(test.y, elo_only),
        "better_win_rate": {"accuracy": float((rule == test.y).mean()), "bouts": int(len(test))},
        "families": {family: scores(test.y, pf) for family, pf in family_test.items()},
        "comparisons": {name: {metric: [float(v) for v in interval] for metric, interval in c.items()}
                        for name, c in comparisons.items()},
    }
    print(f"\nTest ({TEST_START} to {last_result:%Y-%m-%d}, {len(test)} fights), each family's validation pick:")
    for family, s in results["families"].items():
        print(f"  {family:<20} accuracy {s['accuracy']:.3f}  log loss {s['log_loss']:.4f}  brier {s['brier']:.4f}")
    s = results["before_fight_stats"]
    print(f"  {'before fight stats':<20} accuracy {s['accuracy']:.3f}  log loss {s['log_loss']:.4f}  brier {s['brier']:.4f}")
    print("  Paired bootstrap, new minus old (negative log loss is better), 95% interval:")
    for label, interval in comparisons.items():
        print_comparison(label if " vs logistic" in label else f"logistic regression {label}", interval)
    # How often the favorite wins when the model is confident vs. close to a coin flip.
    buckets = []
    for lo, hi in ((0.5, 0.6), (0.6, 0.7), (0.7, 0.8), (0.8, 1.01)):
        fav = np.maximum(p, 1 - p)
        mask = (fav >= lo) & (fav < hi)
        if mask.sum():
            buckets.append({"from": lo, "to": min(hi, 1.0), "bouts": int(mask.sum()),
                            "predicted": float(fav[mask].mean()),
                            "actual": float(((p > 0.5) == (test.y == 1))[mask].mean())})
    print(f"\nExported model (logistic regression, K={k}, debut scale {debut}, since {since or 'all'}, {name}) on the test years:")
    for label, s in results.items():
        if label not in ("families", "comparisons"):
            print(f"  {label:<20} accuracy {s['accuracy']:.3f}"
                  + (f"  log loss {s['log_loss']:.4f}" if "log_loss" in s else ""))
    print("  Calibration (favorite's predicted vs actual win rate):")
    for b in buckets:
        print(f"    {b['from']:.0%}-{b['to']:.0%}: {b['bouts']:>4} fights, "
              f"predicted {b['predicted']:.3f}, actual {b['actual']:.3f}")
    if dry_run:
        print("\n--dry-run: nothing written")
        return

    # --- Final model on every fight since the chosen start, exported for the site ------
    final_rows = learning_rows(data, since, last_result + pd.Timedelta(days=1))
    final = Model(feats, c).fit(final_rows)
    coefs = final.lr.coef_[0]
    order = np.argsort(-np.abs(coefs))
    print("\nFinal model weights (per scaled unit):")
    for i in order:
        print(f"  {feats[i]:<14} {coefs[i]:+.3f}")

    OUT.mkdir(parents=True, exist_ok=True)
    model_json = {
        "trainedOn": f"{final_rows.date.min():%Y-%m-%d} to {last_result:%Y-%m-%d}",
        "trainedSince": since,
        "snapshotDate": f"{snapshot_date:%Y-%m-%d}",
        "trainingBouts": int(len(final_rows)),
        "eloK": k,
        "eloDebutScale": debut,
        "featureSet": name,
        "features": [{"key": f, "label": FEATURES[f], "scale": float(s), "weight": float(w)}
                     for f, s, w in zip(feats, final.scale, coefs)],
        "test": {"from": TEST_START, "to": f"{last_result:%Y-%m-%d}", **results,
                 "calibration": buckets},
    }
    (OUT / "model.json").write_text(json.dumps(model_json, indent=2))

    # Fighters with no UFC fight yet (debutants on upcoming cards) start where they would in training.
    def history(fid):
        return histories.get(fid) or FighterHistory(seeder.elo(histories, snapshot_date, fid))

    profiles = {}
    for fid, row in phys.iterrows():
        h = history(fid)
        profiles[row.Slug] = {
            **{key: round(v, 5) for key, v in h.state(priors).items()},
            "height": round(float(row.height), 2), "reach": round(float(row.reach), 2),
            "southpaw": float(row.southpaw), "ageRef": float(row.age_ref),
            "birth": f"{row.birth:%Y-%m-%d}" if pd.notna(row.birth) else None,
            "fights": h.fights, "wins": h.wins, "losses": h.losses,
            "draws": h.fights - h.wins - h.losses,
            "koWins": h.ko_wins, "subWins": h.sub_wins, "timesFinished": h.finished,
            # Most recent first, e.g. "WWL".
            "recent": "".join({1: "W", 0.5: "D", 0: "L"}[r] for r in reversed(h.results[-5:])),
            "lastFight": f"{h.last_fight:%Y-%m-%d}" if h.last_fight is not None else None,
        }
    (OUT / "fighter_profiles.json").write_text(json.dumps(profiles, separators=(",", ":")))

    # What the test model (trained only on fights before the test years) picked for each
    # test-year fight before it happened, as P(red corner wins). The site shows these on
    # past events.
    held_out = data[data.date >= TEST_START]
    past = dict(zip(held_out.fight, np.round(model.predict(held_out), 4)))
    (OUT / "past_predictions.json").write_text(json.dumps(
        {"model": "trained on fights before " + TEST_START, "fighter1WinProbability": past},
        separators=(",", ":")))

    # Reference predictions for the upcoming fights, to check the site's math against.
    upcoming = fights[(fights.Status == "upcoming") & fights.Fighter1Id.isin(phys.index) & fights.Fighter2Id.isin(phys.index)]
    check = []
    for b in upcoming.itertuples():
        xa = fighter_vector(history(b.Fighter1Id), phys.loc[b.Fighter1Id],
                            snapshot_date, b.EventDate, priors)
        xb = fighter_vector(history(b.Fighter2Id), phys.loc[b.Fighter2Id],
                            snapshot_date, b.EventDate, priors)
        row = pd.DataFrame([pair_features(xa, xb)])
        check.append({"fight": int(b.Id), "date": f"{b.EventDate:%Y-%m-%d}", "fighter1": phys.loc[b.Fighter1Id].Slug,
                      "fighter2": phys.loc[b.Fighter2Id].Slug,
                      "p1": round(float(final.predict(row)[0]), 6)})
    (ROOT / "ml" / "reference_predictions.json").write_text(json.dumps(check, indent=1))
    print(f"\nWrote {OUT / 'model.json'}, fighter_profiles.json ({len(profiles)} fighters), "
          f"and {len(check)} reference predictions")


if __name__ == "__main__":
    main()
