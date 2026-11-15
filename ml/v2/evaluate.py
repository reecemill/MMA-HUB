"""Scores, comparisons and the reliability plot for win probabilities."""

import matplotlib

matplotlib.use("Agg")
import matplotlib.pyplot as plt  # noqa: E402
import numpy as np  # noqa: E402
from sklearn.metrics import brier_score_loss, log_loss  # noqa: E402

BUCKETS = [(0.5, 0.6), (0.6, 0.7), (0.7, 0.8), (0.8, 1.0001)]


def scores(y, p):
    y, p = np.asarray(y, float), np.asarray(p, float)
    return {"log_loss": float(log_loss(y, p, labels=[0, 1])), "brier": float(brier_score_loss(y, p)),
            "accuracy": float(((p > 0.5) == (y == 1)).mean()), "fights": int(len(y))}


def paired_bootstrap(y, p_new, p_old, draws=2000, seed=0):
    """Mean and 95% interval of (new - old) for log loss and accuracy, resampling fights."""
    rng = np.random.default_rng(seed)
    y = np.asarray(y, float)
    idx = rng.integers(0, len(y), (draws, len(y)))
    out = {}
    right_new, right_old = (p_new > 0.5) == y, (p_old > 0.5) == y
    d = right_new[idx].mean(axis=1) - right_old[idx].mean(axis=1)
    out["accuracy"] = [float(right_new.mean() - right_old.mean()), *map(float, np.percentile(d, [2.5, 97.5]))]
    loss = lambda p: -(y * np.log(np.clip(p, 1e-12, 1)) + (1 - y) * np.log(np.clip(1 - p, 1e-12, 1)))  # noqa: E731
    ln, lo = loss(np.asarray(p_new)), loss(np.asarray(p_old))
    d = ln[idx].mean(axis=1) - lo[idx].mean(axis=1)
    out["log_loss"] = [float(ln.mean() - lo.mean()), *map(float, np.percentile(d, [2.5, 97.5]))]
    return out


def favorite_buckets(y, p):
    """How often the model's favorite won, by how strongly it was favored."""
    y, p = np.asarray(y, float), np.asarray(p, float)
    fav = np.maximum(p, 1 - p)
    right = (p > 0.5) == (y == 1)
    out = []
    for lo, hi in BUCKETS:
        m = (fav >= lo) & (fav < hi)
        if m.sum():
            out.append({"from": lo, "to": min(hi, 1.0), "fights": int(m.sum()),
                        "predicted": float(fav[m].mean()), "favorite_won": float(right[m].mean())})
    return out


def one_orientation(y, seed=0):
    """A random side of each fight as fighter A, so the plot doesn't sit on one half."""
    return np.random.default_rng(seed).random(len(y)) < 0.5


def reliability(y, p, bins=10):
    edges = np.linspace(0, 1, bins + 1)
    which = np.clip(np.digitize(p, edges) - 1, 0, bins - 1)
    return [(float(p[which == b].mean()), float(y[which == b].mean()), int((which == b).sum()))
            for b in range(bins) if (which == b).any()]


def plot_reliability(y, curves, path, title):
    """curves: name -> P(fighter A wins) for the same fights."""
    flip = one_orientation(y)
    y_shown = np.where(flip, 1 - y, y)
    fig, (top, bottom) = plt.subplots(2, 1, figsize=(6.5, 7.5), gridspec_kw={"height_ratios": [3, 1]}, sharex=True)
    top.plot([0, 1], [0, 1], color="#999", lw=1, ls="--", label="Perfectly calibrated")
    for name, p in curves.items():
        p_shown = np.where(flip, 1 - p, p)
        pts = reliability(y_shown, p_shown)
        top.plot([a for a, _, _ in pts], [b for _, b, _ in pts], marker="o", ms=4, lw=1.5, label=name)
        bottom.hist(p_shown, bins=20, range=(0, 1), histtype="step", lw=1.2, label=name)
    top.set_ylabel("Share of fights fighter A won")
    top.set_title(title)
    top.legend(loc="upper left", fontsize=8)
    top.set_xlim(0, 1)
    top.set_ylim(0, 1)
    bottom.set_xlabel("Predicted chance fighter A wins")
    bottom.set_ylabel("Fights")
    fig.tight_layout()
    fig.savefig(path, dpi=130)
    plt.close(fig)
