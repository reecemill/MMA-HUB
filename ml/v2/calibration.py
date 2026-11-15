"""Calibrators for a symmetric win probability. Both keep P(A) + P(B) = 1."""

import numpy as np
from sklearn.isotonic import IsotonicRegression
from sklearn.linear_model import LogisticRegression

_CLIP = 1e-6


def _logit(p):
    p = np.clip(p, _CLIP, 1 - _CLIP)
    return np.log(p / (1 - p))


class Temperature:
    """p -> sigmoid(a * logit(p)): one number that sharpens (a > 1) or softens (a < 1)
    every prediction. With no intercept it can't favor either corner."""
    kind = "temperature"

    def fit(self, p, y):
        z = _logit(np.asarray(p))
        x = np.concatenate([z, -z])[:, None]
        t = np.concatenate([y, 1 - y])
        self.a = float(LogisticRegression(fit_intercept=False, C=1e6).fit(x, t).coef_[0, 0])
        return self

    def apply(self, p):
        return 1 / (1 + np.exp(-self.a * _logit(np.asarray(p))))

    def to_json(self):
        return {"kind": self.kind, "a": self.a}


class SymmetricIsotonic:
    """A monotone curve fit on both orientations, applied as (f(p) + 1 - f(1 - p)) / 2."""
    kind = "isotonic"

    def fit(self, p, y):
        p = np.asarray(p)
        self.iso = IsotonicRegression(y_min=0, y_max=1, out_of_bounds="clip").fit(
            np.concatenate([p, 1 - p]), np.concatenate([y, 1 - y]))
        return self

    def apply(self, p):
        p = np.asarray(p)
        return np.clip((self.iso.predict(p) + 1 - self.iso.predict(1 - p)) / 2, _CLIP, 1 - _CLIP)

    def to_json(self):
        return {"kind": self.kind, "x": self.iso.X_thresholds_.tolist(), "y": self.iso.y_thresholds_.tolist()}
