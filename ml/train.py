"""Train the fight prediction model and export it for the website.

    python ml/train.py [path/to/mma.db]     # default: data/mma.db

Reads the SQLite snapshot (see SnapshotExporter.cs), walks every UFC bout in date
order, and builds each fighter's features only from fights *before* the one being
predicted, so the model never sees a result it's asked to predict. Fighters'
career stats from the API aren't used for the same reason: they're current
totals that already include the fights being predicted.

Writes src/Web/Data/model.json (weights and test results) and
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
from sklearn.linear_model import LogisticRegression
from sklearn.metrics import accuracy_score, brier_score_loss, log_loss

ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / "src" / "Web" / "Data"

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
}

_RECORD = ["elo", "win_pct", "log_fights", "last3", "streak"]
_FINISHING = ["ko_rate", "sub_rate", "finished_rate"]
FEATURE_SETS = {
    "win rate only": ["win_pct"],
    "record": _RECORD,
    "record + finishing": _RECORD + _FINISHING,
    "record + finishing + layoff": _RECORD + _FINISHING + ["log_layoff"],
    "everything": list(FEATURES),
}


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
        "select Id, Slug, Name, Division, Age, HeightInches, ReachInches, Stance from Fighters", con)
    con.close()
    # Within a date, the API's bout order is arbitrary; sort by id so runs are repeatable.
    bouts = bouts.sort_values(["EventDate", "Id"], kind="stable").reset_index(drop=True)
    return bouts, fighters


def physical_attributes(fighters, snapshot_date):
    """Height, reach, stance, and age reference per fighter, with gaps filled.

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
    return f.set_index("Id")[["Slug", "Name", "height", "reach", "southpaw", "age_ref"]]


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

    def state(self):
        """The part of the features that doesn't depend on the fight date."""
        n = self.fights
        last = self.results[-3:]
        return {
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


def dated_features(state, last_fight, age_ref, snapshot_date, date):
    """Adds the features that depend on when the fight happens."""
    days = (date - last_fight).days if last_fight is not None else 0
    age = age_ref - (snapshot_date - date).days / 365.25
    return {**state, "log_layoff": math.log1p(max(days, 0)), "age": age}


def fighter_vector(history, phys, snapshot_date, date):
    x = dated_features(history.state(), history.last_fight, phys.age_ref, snapshot_date, date)
    x.update(height=phys.height, reach=phys.reach, southpaw=phys.southpaw)
    return x


def walk_bouts(bouts, phys, snapshot_date, k):
    """Features for every completed bout (from history before it), and final histories."""
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
            xa = fighter_vector(ha, phys.loc[a_id], snapshot_date, b.EventDate)
            xb = fighter_vector(hb, phys.loc[b_id], snapshot_date, b.EventDate)
            rows.append({"date": b.EventDate, "bout": b.Id, "y": score_a,
                         **{f: xa[f] - xb[f] for f in FEATURES}})

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


def scores(y, p):
    return {"accuracy": accuracy_score(y, p > 0.5), "log_loss": log_loss(y, p, labels=[0, 1]),
            "brier": brier_score_loss(y, p), "bouts": int(len(y))}


def main():
    db_path = Path(sys.argv[1]) if len(sys.argv) > 1 else ROOT / "data" / "mma.db"
    bouts, fighters = load(db_path)
    last_result = bouts.loc[bouts.Status == "completed", "EventDate"].max()
    # Fighter ages were recorded when the scraper ran, just after the last results.
    snapshot_date = last_result
    phys = physical_attributes(fighters, snapshot_date)

    # --- Choose settings on the validation years -------------------------------------
    # Height, reach, and age made held-out predictions worse in every combination tried:
    # ages are stale for retired fighters and 40% of reaches are missing. They're kept
    # as a candidate so that stays checked each time the data changes.
    print("Validation (train before 2022, test 2022-2023):")
    best = None
    c = 1.0
    for k in (32, 64, 128):
        data, _ = walk_bouts(bouts, phys, snapshot_date, k)
        train = data[data.date < VALIDATION_START]
        valid = one_side(data[(data.date >= VALIDATION_START) & (data.date < TEST_START)])
        for name, feats in FEATURE_SETS.items():
            s = scores(valid.y, Model(feats, c).fit(train).predict(valid))
            print(f"  K={k:<4} {name:<26} accuracy {s['accuracy']:.3f}  log loss {s['log_loss']:.4f}")
            if best is None or s["log_loss"] < best[0]:
                best = (s["log_loss"], k, name, feats)
    _, k, name, feats = best
    print(f"Chose K={k}, features: {name}")

    # --- Report once on the test years -----------------------------------------------
    data, histories = walk_bouts(bouts, phys, snapshot_date, k)
    train = data[data.date < TEST_START]
    test = one_side(data[data.date >= TEST_START])
    model = Model(feats, c).fit(train)
    p = model.predict(test)
    elo_only = Model(["elo"], c).fit(train).predict(test)
    # Baseline with no model: pick whoever has the better UFC win rate (coin flip on ties).
    rate = test["win_pct"].to_numpy()
    rng = np.random.default_rng(1)
    rule = np.where(rate > 0, 1.0, np.where(rate < 0, 0.0, rng.random(len(rate)) < 0.5))

    results = {
        "model": scores(test.y, p),
        "elo_only": scores(test.y, elo_only),
        "better_win_rate": {"accuracy": float((rule == test.y).mean()), "bouts": int(len(test))},
    }
    # How often the favorite wins when the model is confident vs. close to a coin flip.
    buckets = []
    for lo, hi in ((0.5, 0.6), (0.6, 0.7), (0.7, 0.8), (0.8, 1.01)):
        fav = np.maximum(p, 1 - p)
        mask = (fav >= lo) & (fav < hi)
        if mask.sum():
            buckets.append({"from": lo, "to": min(hi, 1.0), "bouts": int(mask.sum()),
                            "predicted": float(fav[mask].mean()),
                            "actual": float(((p > 0.5) == (test.y == 1))[mask].mean())})
    print(f"\nTest ({TEST_START} to {last_result:%Y-%m-%d}, {len(test)} bouts):")
    for label, s in results.items():
        print(f"  {label:<16} accuracy {s['accuracy']:.3f}"
              + (f"  log loss {s['log_loss']:.4f}" if "log_loss" in s else ""))
    print("  Calibration (favorite's predicted vs actual win rate):")
    for b in buckets:
        print(f"    {b['from']:.0%}-{b['to']:.0%}: {b['bouts']:>4} bouts, "
              f"predicted {b['predicted']:.3f}, actual {b['actual']:.3f}")

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
            **{key: round(v, 5) for key, v in h.state().items()},
            "height": round(float(row.height), 2), "reach": round(float(row.reach), 2),
            "southpaw": float(row.southpaw), "ageRef": float(row.age_ref),
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
                            snapshot_date, b.EventDate)
        xb = fighter_vector(histories.get(b.Fighter2Id, FighterHistory()), phys.loc[b.Fighter2Id],
                            snapshot_date, b.EventDate)
        row = pd.DataFrame([{f: xa[f] - xb[f] for f in FEATURES}])
        check.append({"bout": b.Id, "fighter1": phys.loc[b.Fighter1Id].Slug,
                      "fighter2": phys.loc[b.Fighter2Id].Slug,
                      "p1": round(float(final.predict(row)[0]), 4)})
    (ROOT / "ml" / "reference_predictions.json").write_text(json.dumps(check, indent=1))
    print(f"\nWrote {OUT / 'model.json'}, fighter_profiles.json ({len(profiles)} fighters), "
          f"and {len(check)} reference predictions")


if __name__ == "__main__":
    main()
