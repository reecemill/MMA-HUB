"""Train the fight prediction model and export it for the website.

    python ml/download_ufcstats.py          # once: per-fight stats into data/ufcstats/
    python ml/train.py [path/to/mma.db] [--dry-run]     # default: data/mma.db

Reads the SQLite snapshot (see SnapshotExporter.cs), walks every UFC bout in date
order, and builds each fighter's features only from fights *before* the one being
predicted, so the model never sees a result it's asked to predict. Fighters'
career stats from the API aren't used for the same reason: they're current
totals that already include the fights being predicted. Striking and grappling
rates come instead from ufcstats.com's per-fight stats, accumulated fight by fight.

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
from datetime import datetime
from pathlib import Path

import numpy as np
import pandas as pd
from sklearn.ensemble import HistGradientBoostingClassifier
from sklearn.linear_model import LogisticRegression
from sklearn.metrics import accuracy_score, brier_score_loss, log_loss
from sklearn.neural_network import MLPClassifier
from sklearn.pipeline import make_pipeline
from sklearn.preprocessing import StandardScaler

import ufcstats

ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / "src" / "Web" / "Data"
UFCSTATS = ROOT / "data" / "ufcstats"

# Held-out periods: choose settings on validation, report once on test.
VALIDATION_START = "2022-01-01"
TEST_START = "2024-01-01"

ELO_START = 1500.0
KO_METHODS = {"KO/TKO", "TKO - Doctor's Stoppage"}
SUB_METHODS = {"SUB", "Submission"}

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
    # From ufcstats.com's per-fight stats, over the fighter's earlier UFC fights.
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
BEFORE = {"k": 128, "features": _CURRENT}
# Gradient boosting and the network only try the feature sets with per-fight stats.
NONLINEAR_SETS = ["+ striking + grappling", "+ striking + grappling + age", "+ stats + matchups + age"]
# Pseudo-counts that pull a fighter's rates toward the league average until there's data.
PRIOR_MINUTES, PRIOR_STRIKES, PRIOR_TAKEDOWNS = 15.0, 20.0, 5.0


def load(db_path):
    con = sqlite3.connect(db_path)
    bouts = pd.read_sql(
        """select b.Id, b.Fighter1Id, b.Fighter2Id, b.WinnerFighterId, b.Fighter1Outcome,
                  b.Method, b.Status, b.WeightClass, e.EventDate, e.Status as EventStatus,
                  e.Title as EventTitle, e.Slug as EventSlug
           from Bouts b join Events e on e.Id = b.EventId
           where b.Fighter1Id is not null and b.Fighter2Id is not null""",
        con, parse_dates=["EventDate"])
    fighters = pd.read_sql(
        "select Id, Slug, Name, Division, Age, HeightInches, ReachInches, Stance, UfcStatsId from Fighters", con)
    con.close()
    # Within a date, the API's bout order is arbitrary; sort by id so runs are repeatable.
    bouts = bouts.sort_values(["EventDate", "Id"], kind="stable").reset_index(drop=True)
    return bouts, fighters


def physical_attributes(fighters, snapshot_date, name_of, birth_of):
    """Height, reach, stance, and age reference per fighter, with gaps filled, plus the
    fighter's ufcstats.com name (to find their fight stats) and date of birth.

    Missing or zero height gets the median for the fighter's division; missing reach
    is set to height (the typical ratio is close to 1).
    """
    f = fighters.copy()
    f["height"] = pd.to_numeric(f["HeightInches"], errors="coerce").where(lambda h: h > 48)
    f["reach"] = pd.to_numeric(f["ReachInches"], errors="coerce").where(lambda r: r > 48)
    f["height"] = f["height"].fillna(f.groupby("Division")["height"].transform("median"))
    f["height"] = f["height"].fillna(f["height"].median())
    f["reach"] = f["reach"].fillna(f["height"])
    stance = f["Stance"].fillna("")
    f["southpaw"] = np.select([stance == "Southpaw", stance == "Switch"], [1.0, 0.5], 0.0)
    # Age as of the snapshot. For retired fighters UFC.com often stopped updating it, so
    # age at older fights comes out too young; the model only keeps age if it helps.
    f["age_ref"] = pd.to_numeric(f["Age"], errors="coerce").fillna(f["Age"].median())
    f["stats_name"] = f["UfcStatsId"].map(name_of)
    f["birth"] = pd.to_datetime(f["UfcStatsId"].map(birth_of))
    return f.set_index("Id")[["Slug", "Name", "height", "reach", "southpaw", "age_ref", "stats_name", "birth"]]


class FighterHistory:
    """What's known about one fighter from their UFC bouts so far."""

    def __init__(self):
        self.elo = ELO_START
        self.fights = 0
        self.wins = 0
        self.losses = 0
        self.results = []  # 1 win, 0.5 draw, 0 loss
        self.ko_wins = 0
        self.sub_wins = 0
        self.finished = 0
        self.last_fight = None
        self.own = ufcstats.FightStats()       # totals over fights with per-fight stats
        self.against = ufcstats.FightStats()   # what opponents did in those fights

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

    Age comes from the date of birth when ufcstats.com has it; otherwise from UFC.com's
    age, which is stale for retired fighters.
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


def league_priors(bouts, phys, fights, before):
    """League-average rates, from the per-fight stats of bouts before `before`."""
    total = ufcstats.FightStats()
    for b in bouts[(bouts.Status == "completed") & (bouts.EventDate < before)].itertuples():
        if b.Fighter1Id not in phys.index or b.Fighter2Id not in phys.index:
            continue
        found = ufcstats.lookup(fights, b.EventDate.normalize(),
                                phys.at[b.Fighter1Id, "stats_name"], phys.at[b.Fighter2Id, "stats_name"])
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


def walk_bouts(bouts, phys, snapshot_date, k, fights, priors):
    """Features for every completed bout (from history before it), and final histories.

    A bout's own per-fight stats are added to each fighter's totals only after its
    features are recorded, like its result.
    """
    histories = {}
    rows = []
    for b in bouts.itertuples():
        if b.Status != "completed":
            continue
        a_id, b_id = b.Fighter1Id, b.Fighter2Id
        if a_id not in phys.index or b_id not in phys.index:
            continue
        ha = histories.setdefault(a_id, FighterHistory())
        hb = histories.setdefault(b_id, FighterHistory())

        if b.Fighter1Outcome == "draw":
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
            rows.append({"date": b.EventDate, "bout": b.Id, "y": score_a, **pair_features(xa, xb)})

        found = ufcstats.lookup(fights, b.EventDate.normalize(),
                                phys.at[a_id, "stats_name"], phys.at[b_id, "stats_name"])
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
    return pd.DataFrame(rows), histories


def symmetric(df):
    """Every bout both ways round, so the model can't learn which slot the winner is in.

    (The API lists the loser first in nearly every bout.) With no intercept and
    difference features, P(A beats B) is then exactly 1 - P(B beats A).
    """
    flipped = df.copy()
    flipped[list(FEATURES)] = -flipped[list(FEATURES)]
    flipped["y"] = 1 - flipped["y"]
    return pd.concat([df, flipped], ignore_index=True)


def one_side(df):
    """Each bout once, with a random fighter as A, for fair accuracy numbers."""
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
    trained on every bout both ways round, and predicting the average of P(A beats B)
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


def paired_bootstrap(y, p_new, p_old, draws=2000):
    """Mean and 95% interval of (new - old) in log loss and in accuracy, resampling test bouts.

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
    bouts, fighters = load(db_path)
    last_result = bouts.loc[bouts.Status == "completed", "EventDate"].max()
    # Fighter ages were recorded when the scraper ran, just after the last results.
    snapshot_date = last_result
    if not (UFCSTATS / "ufc_fight_stats.csv").exists():
        sys.exit("No per-fight stats. Download them first: python ml/download_ufcstats.py")
    fights, name_of, birth_of = ufcstats.load(UFCSTATS)
    phys = physical_attributes(fighters, snapshot_date, name_of, birth_of)
    priors = league_priors(bouts, phys, fights, pd.Timestamp(VALIDATION_START))

    completed = bouts[bouts.Status == "completed"]
    has_stats = np.array([
        a in phys.index and b in phys.index and ufcstats.lookup(
            fights, d.normalize(), phys.at[a, "stats_name"], phys.at[b, "stats_name"]) is not None
        for a, b, d in zip(completed.Fighter1Id, completed.Fighter2Id, completed.EventDate)])
    in_test = (completed.EventDate >= TEST_START).to_numpy()
    print(f"Per-fight stats found for {has_stats.mean():.1%} of {len(completed)} completed bouts "
          f"({has_stats[in_test].mean():.1%} in the test years); dates of birth for "
          f"{phys.birth.notna().mean():.1%} of fighters")

    # --- Choose settings on the validation years -------------------------------------
    # Each family searches Elo K, feature sets and its own settings; the lowest log loss wins.
    # Height and reach stay candidates in "everything" so they keep being checked.
    walks = {k: walk_bouts(bouts, phys, snapshot_date, k, fights, priors) for k in (32, 64, 128)}
    print("\nValidation (train before 2022, test 2022-2023):")
    best = {}
    for family, makers in FAMILIES.items():
        sets = FEATURE_SETS if family == "logistic regression" else {n: FEATURE_SETS[n] for n in NONLINEAR_SETS}
        for k, (data, _) in walks.items():
            train = data[data.date < VALIDATION_START]
            valid = one_side(data[(data.date >= VALIDATION_START) & (data.date < TEST_START)])
            for name, feats in sets.items():
                for make in makers:
                    model = make(feats).fit(train)
                    s = scores(valid.y, model.predict(valid))
                    print(f"  {family:<20} K={k:<4} {name:<34} {model.describe():<48} "
                          f"accuracy {s['accuracy']:.3f}  log loss {s['log_loss']:.4f}")
                    if family not in best or s["log_loss"] < best[family]["log_loss"]:
                        best[family] = {"log_loss": s["log_loss"], "k": k, "name": name, "feats": feats,
                                        "make": make, "describe": model.describe()}
    print("\nBest of each family on validation:")
    for family, b in best.items():
        print(f"  {family:<20} log loss {b['log_loss']:.4f}  K={b['k']}, {b['name']}, {b['describe']}")

    # --- Report once on the test years -----------------------------------------------
    def test_predictions(k, feats, make):
        data = walks[k][0]
        model = make(feats).fit(data[data.date < TEST_START])
        test = one_side(data[data.date >= TEST_START])
        return model, test, model.predict(test)

    family_test = {}
    for family, b in best.items():
        family_model, test, family_test[family] = test_predictions(b["k"], b["feats"], b["make"])
        # P(A beats B) + P(B beats A) must be exactly 1 for every model.
        flipped = test.copy()
        flipped[list(FEATURES)] = -flipped[list(FEATURES)]
        assert np.allclose(family_test[family] + family_model.predict(flipped), 1), family
    _, _, p_before = test_predictions(BEFORE["k"], BEFORE["features"], lambda f: Model(f, 1.0))

    # The site runs logistic regression, so that's what's exported; the other families are
    # reported next to it to show whether a non-linear model would be worth deploying.
    site = best["logistic regression"]
    k, name, feats = site["k"], site["name"], site["feats"]
    data, histories = walks[k]
    train = data[data.date < TEST_START]
    model, test, p = test_predictions(k, feats, site["make"])
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
    print(f"\nTest ({TEST_START} to {last_result:%Y-%m-%d}, {len(test)} bouts), each family's validation pick:")
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
    print(f"\nExported model (logistic regression, K={k}, {name}) on the test years:")
    for label, s in results.items():
        if label not in ("families", "comparisons"):
            print(f"  {label:<20} accuracy {s['accuracy']:.3f}"
                  + (f"  log loss {s['log_loss']:.4f}" if "log_loss" in s else ""))
    print("  Calibration (favorite's predicted vs actual win rate):")
    for b in buckets:
        print(f"    {b['from']:.0%}-{b['to']:.0%}: {b['bouts']:>4} bouts, "
              f"predicted {b['predicted']:.3f}, actual {b['actual']:.3f}")
    if dry_run:
        print("\n--dry-run: nothing written")
        return

    # --- Final model on every bout, exported for the site ----------------------------
    final = Model(feats, c).fit(data)
    coefs = final.lr.coef_[0]
    order = np.argsort(-np.abs(coefs))
    print("\nFinal model weights (per scaled unit):")
    for i in order:
        print(f"  {feats[i]:<14} {coefs[i]:+.3f}")

    OUT.mkdir(parents=True, exist_ok=True)
    model_json = {
        "trainedOn": f"{data.date.min():%Y-%m-%d} to {last_result:%Y-%m-%d}",
        "snapshotDate": f"{snapshot_date:%Y-%m-%d}",
        "trainingBouts": int(len(data)),
        "eloK": k,
        "featureSet": name,
        "features": [{"key": f, "label": FEATURES[f], "scale": float(s), "weight": float(w)}
                     for f, s, w in zip(feats, final.scale, coefs)],
        "test": {"from": TEST_START, "to": f"{last_result:%Y-%m-%d}", **results,
                 "calibration": buckets},
    }
    (OUT / "model.json").write_text(json.dumps(model_json, indent=2))

    profiles = {}
    for fid, row in phys.iterrows():
        h = histories.get(fid, FighterHistory())
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

    # What the test model (trained only on bouts before the test years) picked for each
    # test-year bout before it happened. The site shows these on past events.
    held_out = data[data.date >= TEST_START]
    past = dict(zip(held_out.bout, np.round(model.predict(held_out), 4)))
    (OUT / "past_predictions.json").write_text(json.dumps(
        {"model": "trained on bouts before " + TEST_START, "fighter1WinProbability": past},
        separators=(",", ":")))

    # Reference predictions for the scheduled bouts, to check the site's math against.
    upcoming = bouts[(bouts.Status == "confirmed") & (bouts.EventStatus == "scheduled")]
    check = []
    for b in upcoming.itertuples():
        xa = fighter_vector(histories.get(b.Fighter1Id, FighterHistory()), phys.loc[b.Fighter1Id],
                            snapshot_date, b.EventDate, priors)
        xb = fighter_vector(histories.get(b.Fighter2Id, FighterHistory()), phys.loc[b.Fighter2Id],
                            snapshot_date, b.EventDate, priors)
        row = pd.DataFrame([pair_features(xa, xb)])
        check.append({"bout": b.Id, "date": f"{b.EventDate:%Y-%m-%d}", "fighter1": phys.loc[b.Fighter1Id].Slug,
                      "fighter2": phys.loc[b.Fighter2Id].Slug,
                      "p1": round(float(final.predict(row)[0]), 4)})
    (ROOT / "ml" / "reference_predictions.json").write_text(json.dumps(check, indent=1))
    print(f"\nWrote {OUT / 'model.json'}, fighter_profiles.json ({len(profiles)} fighters), "
          f"and {len(check)} reference predictions")


if __name__ == "__main__":
    main()
