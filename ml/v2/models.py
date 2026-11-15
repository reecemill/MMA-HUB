"""Winner models. Both see every fight both ways round and predict symmetrically, so
P(A beats B) is always exactly 1 - P(B beats A): the red corner (usually the favorite
or champion) can't be learned as a shortcut."""

import lightgbm as lgb
import numpy as np
from sklearn.linear_model import LogisticRegression

SEED = 0
LGB_BASE = {"objective": "binary", "metric": "binary_logloss", "learning_rate": 0.03,
            "feature_fraction": 0.8, "bagging_fraction": 0.8, "bagging_freq": 1, "lambda_l2": 1.0,
            "seed": SEED, "deterministic": True, "force_row_wise": True, "num_threads": 4, "verbose": -1}


def design(rows, features, context=()):
    """Fighter A's value minus fighter B's for each feature, then the context columns as they are."""
    cols = [rows[f"a_{f}"].to_numpy(float) - rows[f"b_{f}"].to_numpy(float) for f in features]
    cols += [rows[c].to_numpy(float) for c in context]
    return np.column_stack(cols) if cols else np.empty((len(rows), 0))


def flipped(x, n_diff):
    """The same fights with A and B swapped: differences change sign, context doesn't."""
    out = x.copy()
    out[:, :n_diff] *= -1
    return out


def both_ways(x, y, n_diff):
    return np.vstack([x, flipped(x, n_diff)]), np.concatenate([y, 1 - y])


class Baseline:
    """Logistic regression with no intercept on four differences: rating, age, reach, UFC fights."""
    FEATURES = ["rating", "age", "reach", "log_fights"]

    def fit(self, rows, y):
        x = np.nan_to_num(design(rows, self.FEATURES))  # a missing difference counts as none
        xs, ys = both_ways(x, y, x.shape[1])
        self.scale = np.sqrt((xs ** 2).mean(axis=0))
        self.scale[self.scale == 0] = 1
        self.lr = LogisticRegression(fit_intercept=False, C=1.0, max_iter=1000).fit(xs / self.scale, ys)
        return self

    def predict(self, rows):
        return self.lr.predict_proba(np.nan_to_num(design(rows, self.FEATURES)) / self.scale)[:, 1]


class Winner:
    """LightGBM on the differences (missing values allowed), symmetrized:
    p = (f(x) + 1 - f(swapped x)) / 2."""

    def __init__(self, features, context=(), params=None, rounds=None):
        self.features, self.context = list(features), list(context)
        self.params = {**LGB_BASE, **(params or {})}
        self.rounds = rounds

    @property
    def names(self):
        return [f"diff_{f}" for f in self.features] + list(self.context)

    def fit(self, rows, y, valid=None):
        """With valid=(rows, y), stops when validation log loss stops improving."""
        n = len(self.features)
        xs, ys = both_ways(design(rows, self.features, self.context), y, n)
        train = lgb.Dataset(xs, ys, feature_name=self.names, free_raw_data=False)
        if valid is not None:
            xv, yv = both_ways(design(valid[0], self.features, self.context), valid[1], n)
            check = lgb.Dataset(xv, yv, reference=train)
            self.booster = lgb.train(self.params, train, num_boost_round=4000, valid_sets=[check],
                                     callbacks=[lgb.early_stopping(200, verbose=False)])
            self.rounds = self.booster.best_iteration
        else:
            self.booster = lgb.train(self.params, train, num_boost_round=self.rounds)
        return self

    def raw(self, x):
        return self.booster.predict(x, num_iteration=self.rounds)

    def predict(self, rows):
        x = design(rows, self.features, self.context)
        return (self.raw(x) + 1 - self.raw(flipped(x, len(self.features)))) / 2

    def importance(self):
        gain = self.booster.feature_importance("gain", iteration=self.rounds)
        return dict(sorted(zip(self.names, gain / gain.sum()), key=lambda kv: -kv[1]))
