"""How a fight ends: the method model (who wins and how) and the round model (when).

Method: one 6-class LightGBM over (A by KO/TKO, A by submission, A by decision, the same
for B). Like the winner model it sees every fight both ways round (differences change
sign, pair totals and context don't, A's classes swap with B's) and is symmetrized, so
P(A by KO) for (A, B) is exactly P(B by KO) for (B, A). Each side's three classes can then
be rescaled to sum to the calibrated winner probability, so the method never disagrees
with the pick.

Round: a discrete-time hazard model on modern rule sets (3 or 5 five-minute rounds). One
row per round a fight reached, labelled 1 if it was finished in that round. Features are
orientation-free (absolute differences, pair totals), plus the round number and the
scheduled rounds. P(ends in round r) = h_r * prod_{j<r} (1 - h_j); P(decision) is what's
left. The finish-by-round shape can be scaled to the method model's P(finish), so every
output agrees on how likely a decision is.
"""

import lightgbm as lgb
import numpy as np

from .features import LIMITS
from .models import LGB_BASE

CLASSES = ["a_ko", "a_sub", "a_dec", "b_ko", "b_sub", "b_dec"]
SWAP = [3, 4, 5, 0, 1, 2]
METHOD_INDEX = {"ko": 0, "sub": 1, "dec": 2}


def method_labels(rows):
    """Class index for each fight from red's (A's) point of view."""
    base = rows.method.map(METHOD_INDEX).to_numpy()
    return np.where(rows.result.to_numpy() == "red", base, base + 3).astype(int)


def context_columns(rows):
    """Fight-level columns that don't depend on corner: scheduled rounds, title fight, weight limit, women's."""
    limit = rows.weight_class.map(LIMITS).astype(float).to_numpy()
    women = rows.weight_class.str.startswith("Women").to_numpy(float)
    return [rows.scheduled_rounds.to_numpy(float), rows.title_fight.to_numpy(float), limit, women]


CONTEXT_NAMES = ["scheduled_rounds", "title_fight", "weight_limit", "women"]


def method_design(rows, diffs, pairs):
    cols = [rows[f"a_{f}"].to_numpy(float) - rows[f"b_{f}"].to_numpy(float) for f in diffs]
    cols += [rows[f"a_{f}"].to_numpy(float) + rows[f"b_{f}"].to_numpy(float) for f in pairs]
    cols += context_columns(rows)
    return np.column_stack(cols)


def _multiclass_logloss(y, p):
    return float(-np.log(np.clip(p[np.arange(len(y)), y], 1e-12, 1)).mean())


class Method:
    def __init__(self, diffs, pairs, params=None, rounds=None):
        self.diffs, self.pairs = list(diffs), list(pairs)
        self.params = {**LGB_BASE, "objective": "multiclass", "num_class": 6, "metric": "multi_logloss",
                       **(params or {})}
        self.rounds = rounds

    @property
    def names(self):
        return [f"diff_{f}" for f in self.diffs] + [f"pair_{f}" for f in self.pairs] + CONTEXT_NAMES

    def _flip(self, x):
        out = x.copy()
        out[:, :len(self.diffs)] *= -1
        return out

    def _both_ways(self, rows):
        x, y = method_design(rows, self.diffs, self.pairs), method_labels(rows)
        return np.vstack([x, self._flip(x)]), np.concatenate([y, np.asarray(SWAP)[y]])

    def fit(self, rows, valid=None):
        xs, ys = self._both_ways(rows)
        train = lgb.Dataset(xs, ys, feature_name=self.names, free_raw_data=False)
        if valid is not None:
            xv, yv = self._both_ways(valid)
            self.booster = lgb.train(self.params, train, num_boost_round=4000,
                                     valid_sets=[lgb.Dataset(xv, yv, reference=train)],
                                     callbacks=[lgb.early_stopping(200, verbose=False)])
            self.rounds = self.booster.best_iteration
        else:
            self.booster = lgb.train(self.params, train, num_boost_round=self.rounds)
        return self

    def predict(self, rows, p_a_wins=None):
        """(n, 6) probabilities in CLASSES order. With p_a_wins, A's three classes are rescaled
        to sum to it (and B's to 1 - it)."""
        x = method_design(rows, self.diffs, self.pairs)
        p = (self.booster.predict(x, num_iteration=self.rounds)
             + self.booster.predict(self._flip(x), num_iteration=self.rounds)[:, SWAP]) / 2
        if p_a_wins is not None:
            p_a_wins = np.asarray(p_a_wins, float)[:, None]
            a, b = p[:, :3], p[:, 3:]
            p = np.hstack([a / a.sum(1, keepdims=True) * p_a_wins, b / b.sum(1, keepdims=True) * (1 - p_a_wins)])
        return p

    def importance(self):
        gain = self.booster.feature_importance("gain", iteration=self.rounds)
        return dict(sorted(zip(self.names, gain / gain.sum()), key=lambda kv: -kv[1]))


# --- Round ---------------------------------------------------------------------------------

def round_design(rows, absdiffs, pairs):
    """Per-fight, orientation-free columns (one row per fight)."""
    cols = [np.abs(rows[f"a_{f}"].to_numpy(float) - rows[f"b_{f}"].to_numpy(float)) for f in absdiffs]
    cols += [rows[f"a_{f}"].to_numpy(float) + rows[f"b_{f}"].to_numpy(float) for f in pairs]
    cols += context_columns(rows)
    return np.column_stack(cols)


def ending_class(rows):
    """0..S-1 for a finish in round 1..S, S for a decision (S = scheduled rounds)."""
    s = rows.scheduled_rounds.to_numpy(int)
    finished = rows.method.isin(["ko", "sub"]).to_numpy()
    return np.where(finished, rows.ending_round.to_numpy(float).astype(int) - 1, s)


class Round:
    def __init__(self, absdiffs, pairs, params=None, rounds=None):
        self.absdiffs, self.pairs = list(absdiffs), list(pairs)
        self.params = {**LGB_BASE, **(params or {})}
        self.rounds = rounds

    @property
    def names(self):
        return ([f"absdiff_{f}" for f in self.absdiffs] + [f"pair_{f}" for f in self.pairs]
                + CONTEXT_NAMES + ["round"])

    def _person_rounds(self, rows, with_labels):
        """Expands each fight into one row per round it reached (or, for prediction, every round)."""
        x = round_design(rows, self.absdiffs, self.pairs)
        s = rows.scheduled_rounds.to_numpy(int)
        end = ending_class(rows) if with_labels else s - 1
        xs, ys, fight = [], [], []
        for i in range(len(rows)):
            last = min(end[i], s[i] - 1)  # a decision reaches every round
            for r in range(last + 1):
                xs.append(np.append(x[i], r + 1))
                ys.append(float(with_labels and r == end[i]))
                fight.append(i)
        return np.array(xs), np.array(ys), np.array(fight)

    def fit(self, rows, valid=None):
        xs, ys, _ = self._person_rounds(rows, True)
        train = lgb.Dataset(xs, ys, feature_name=self.names, free_raw_data=False)
        if valid is not None:
            xv, yv, _ = self._person_rounds(valid, True)
            self.booster = lgb.train(self.params, train, num_boost_round=4000,
                                     valid_sets=[lgb.Dataset(xv, yv, reference=train)],
                                     callbacks=[lgb.early_stopping(200, verbose=False)])
            self.rounds = self.booster.best_iteration
        else:
            self.booster = lgb.train(self.params, train, num_boost_round=self.rounds)
        return self

    def predict(self, rows, p_finish=None):
        """(n, 6): P(ends in round 1..5) then P(decision); rounds past the scheduled number are 0.
        With p_finish, the finish rounds are rescaled to sum to it."""
        xs, _, fight = self._person_rounds(rows, False)
        h = self.booster.predict(xs, num_iteration=self.rounds)
        out = np.zeros((len(rows), 6))
        survive = np.ones(len(rows))
        r_of = xs[:, -1].astype(int) - 1
        for hz, i, r in zip(h, fight, r_of):
            out[i, r] = survive[i] * hz
            survive[i] *= 1 - hz
        out[:, 5] = survive
        if p_finish is not None:
            p_finish = np.asarray(p_finish, float)
            fin = out[:, :5].sum(1)
            out[:, :5] *= (p_finish / np.where(fin > 0, fin, 1))[:, None]
            out[:, 5] = 1 - p_finish
        return out

    def importance(self):
        gain = self.booster.feature_importance("gain", iteration=self.rounds)
        return dict(sorted(zip(self.names, gain / gain.sum()), key=lambda kv: -kv[1]))


def round_logloss(rows, p):
    """Log loss over (round 1..S, decision), S = scheduled rounds."""
    y = ending_class(rows)
    s = rows.scheduled_rounds.to_numpy(int)
    col = np.where(y == s, 5, y)
    return float(-np.log(np.clip(p[np.arange(len(rows)), col], 1e-12, 1)).mean())


multiclass_logloss = _multiclass_logloss
