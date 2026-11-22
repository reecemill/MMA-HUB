"""The closing betting line as a benchmark for the winner model (and targets for Step 4).

Compares, fight by fight on the test fights that have a closing moneyline:
the site's model (v1, its held-out picks), v2 (LightGBM, saved model in ml/models/), and
the market (median implied probability across books, vig removed by normalizing the
two sides). Writes ml/odds/out/benchmark.md.
"""

import json
from pathlib import Path

import lightgbm as lgb
import numpy as np
import pandas as pd
from scipy.stats import binomtest

from ..v2 import data, features, models
from ..v2.calibration import Temperature
from ..v2.features import FeatureParams, Shrinkage
from ..v2.glicko import GlickoParams
from .build import OUT

ROOT = Path(__file__).resolve().parents[2]
TEST_START = pd.Timestamp("2024-01-01")
METHODS = [("RED", "TKO/KO"), ("RED", "submission"), ("RED", "decision"),
           ("BLUE", "TKO/KO"), ("BLUE", "submission"), ("BLUE", "decision")]


def v2_test_predictions(ds):
    """The saved v2 model's calibrated P(red wins) for every decided fight from 2024 on."""
    model_dir = max((ROOT / "ml/models").glob("v2-*"))
    spec = json.loads((model_dir / "feature_spec.json").read_text())
    params = FeatureParams(GlickoParams(**spec["glicko"]), Shrinkage(**spec["shrinkage"]))
    rows = features.build(ds, params).rows
    test = rows[rows.finished & rows.result.isin(["red", "blue"]) & (rows.date >= TEST_START)]
    w = models.Winner(spec["features"], spec["context"], rounds=spec["lightgbm"]["rounds"])
    w.booster = lgb.Booster(model_file=str(model_dir / "winner_eval.txt"))
    t = Temperature()
    t.a = json.loads((model_dir / "calibration.json").read_text())["a"]
    return pd.DataFrame({"fight_id": test.fight_id.to_numpy(), "date": test.date.to_numpy(),
                         "result": test.result.to_numpy(), "method": test.method.to_numpy(),
                         "weight_class": test.weight_class.to_numpy(), "v2": t.apply(w.predict(test))})


def market_winner(lines):
    wide = lines[lines.line.isin(["ML", "ML_BLUE"])].pivot(index="fight_id", columns="line", values="p").dropna()
    return (wide.ML / (wide.ML + wide.ML_BLUE)).rename("market")


def market_method(lines):
    """The market's 6-way (winner x KO/sub/decision) probabilities, normalized, where all six are priced."""
    wanted = {f"{side} wins by {how}": f"{side.lower()}_{how}" for side, how in METHODS}
    m = lines[lines.line.isin(wanted)].assign(col=lambda d: d.line.map(wanted))
    wide = m.pivot(index="fight_id", columns="col", values="p").dropna()
    return wide.div(wide.sum(axis=1), axis=0)


def _metrics(y, p):
    p = np.clip(p, 1e-6, 1 - 1e-6)
    return {"accuracy": float(((p > .5) == (y == 1)).mean()),
            "log_loss": float(-(y * np.log(p) + (1 - y) * np.log(1 - p)).mean()),
            "brier": float(((p - y) ** 2).mean())}


def _paired(y, p_new, p_old, draws=5000, seed=0):
    """new - old: accuracy (points), log loss; with 95% bootstrap intervals and McNemar p."""
    rng = np.random.default_rng(seed)
    idx = rng.integers(0, len(y), (draws, len(y)))
    rn, ro = (p_new > .5) == (y == 1), (p_old > .5) == (y == 1)
    ll = lambda p: -(y * np.log(p) + (1 - y) * np.log(1 - p))  # noqa: E731
    out = {}
    for name, a, b in (("accuracy", rn * 100.0, ro * 100.0), ("log_loss", ll(p_new), ll(p_old))):
        d = a[idx].mean(1) - b[idx].mean(1)
        out[name] = (a.mean() - b.mean(), *np.percentile(d, [2.5, 97.5]))
    n_new, n_old = int((rn & ~ro).sum()), int((~rn & ro).sum())
    out["mcnemar_p"] = binomtest(n_new, n_new + n_old, .5).pvalue if n_new + n_old else 1.0
    out["disagree"] = (n_new, n_old)
    return out


def benchmark():
    lines = pd.read_csv(OUT / "closing_lines.csv")
    ds = data.load()
    v2 = v2_test_predictions(ds)
    v1 = json.loads((ROOT / "src/Web/Data/past_predictions.json").read_text())["fighter1WinProbability"]
    v2["v1"] = v2.fight_id.astype(str).map(v1)
    df = v2.join(market_winner(lines), on="fight_id")
    n_all = len(df)
    df = df.dropna(subset=["v1", "market"])
    y = (df.result == "red").to_numpy(float)
    preds = {"Market (closing line)": df.market.to_numpy(), "v1 (site model)": df.v1.to_numpy(),
             "v2 (LightGBM)": df.v2.to_numpy()}

    out = [f"# Betting-market benchmark", "",
           f"{len(df)} of {n_all} decided test fights (2024-01-01 to {df.date.max():%Y-%m-%d}) have a closing "
           f"moneyline and a v1 pick. Market = median implied probability across sportsbooks, vig removed.", "",
           "| | Accuracy | Log loss | Brier |", "|---|---|---|---|"]
    for name, p in preds.items():
        s = _metrics(y, p)
        out.append(f"| {name} | {s['accuracy']:.1%} | {s['log_loss']:.4f} | {s['brier']:.4f} |")
    out += ["", "Paired comparisons (model minus market; negative log loss = model better):", ""]
    for name in ("v1 (site model)", "v2 (LightGBM)"):
        c = _paired(y, preds[name], preds["Market (closing line)"])
        a, ll = c["accuracy"], c["log_loss"]
        out.append(f"- {name}: accuracy {a[0]:+.1f} pts [{a[1]:+.1f}, {a[2]:+.1f}], log loss {ll[0]:+.4f} "
                   f"[{ll[1]:+.4f}, {ll[2]:+.4f}]; on disagreements model right {c['disagree'][0]}, market right "
                   f"{c['disagree'][1]} (McNemar p={c['mcnemar_p']:.3f})")

    same = np.sign(df.v1 - .5) == np.sign(df.market - .5)
    out += ["", f"v1 picks the market favorite in {same.mean():.1%} of fights. When they disagree "
                f"({(~same).sum()} fights) v1 is right {((df.v1 > .5) == (y == 1))[~same].mean():.1%} of the time.", ""]

    # Does the model add anything the market doesn't have? Logistic blend on logits, fit 2024, scored 2025+.
    logit = lambda p: np.log(np.clip(p, 1e-6, 1 - 1e-6) / (1 - np.clip(p, 1e-6, 1 - 1e-6)))  # noqa: E731
    from sklearn.linear_model import LogisticRegression
    fit = (df.date < "2025-01-01").to_numpy()
    for name, col in (("v1", "v1"), ("v2", "v2")) if 50 <= fit.sum() <= len(fit) - 50 else ():
        x = np.column_stack([logit(df.market), logit(df[col])])
        xs, ys = np.vstack([x[fit], -x[fit]]), np.concatenate([y[fit], 1 - y[fit]])
        lr = LogisticRegression(fit_intercept=False, C=1e6).fit(xs, ys)
        blend = lr.predict_proba(x[~fit])[:, 1]
        mk, bl = _metrics(y[~fit], df.market.to_numpy()[~fit]), _metrics(y[~fit], blend)
        out.append(f"- Market + {name} blend (weights fit on 2024: market {lr.coef_[0, 0]:.2f}, {name} "
                   f"{lr.coef_[0, 1]:.2f}), scored on {(~fit).sum()} fights from 2025: log loss "
                   f"{bl['log_loss']:.4f} vs market alone {mk['log_loss']:.4f}")

    fav = np.maximum(df.market, 1 - df.market)
    bins = pd.cut(fav, [.5, .6, .7, .8, 1.0], include_lowest=True)
    right = ((df.market > .5) == (y == 1))
    tab = pd.DataFrame({"bin": bins, "said": fav, "won": right}).groupby("bin", observed=True).agg(
        fights=("won", "size"), said=("said", "mean"), won=("won", "mean"))
    out += ["", "Market calibration (favorite's implied chance vs how often it won):", "",
            "| Implied | Fights | Said | Won |", "|---|---|---|---|"]
    out += [f"| {b} | {int(r.fights)} | {r.said:.1%} | {r.won:.1%} |" for b, r in tab.iterrows()]

    # Step 4 targets: the market's method and decision prices on decided KO/sub/decision fights.
    mm = market_method(lines)
    dm = df[df.method.isin(["ko", "sub", "dec"])].join(mm, on="fight_id", how="inner")
    if len(dm):
        how = {"ko": "TKO/KO", "sub": "submission", "dec": "decision"}
        truth = (dm.result.str.lower() + "_" + dm.method.map(how)).to_numpy()
        cols = [f"{s.lower()}_{h}" for s, h in METHODS]
        p_true = dm[cols].to_numpy()[np.arange(len(dm)), [cols.index(t) for t in truth]]
        base = pd.Series(truth).value_counts(normalize=True)
        p_base = base.reindex(truth).to_numpy()
        p_dec = dm[["red_decision", "blue_decision"]].sum(axis=1).to_numpy()
        went = (dm.method == "dec").to_numpy(float)
        out += ["", "## Targets for Step 4 (method)", "",
                f"{len(dm)} test fights ending in KO/TKO, submission or decision with all six method props priced.", "",
                f"- Market 6-way log loss {-np.log(np.clip(p_true, 1e-6, 1)).mean():.4f} "
                f"(no-skill base rates: {-np.log(p_base).mean():.4f})",
                f"- Market top pick right {(dm[cols].to_numpy().argmax(1) == [cols.index(t) for t in truth]).mean():.1%}",
                f"- Goes to decision: market log loss "
                f"{-(went * np.log(p_dec) + (1 - went) * np.log(1 - p_dec)).mean():.4f}, Brier {((p_dec - went) ** 2).mean():.4f} "
                f"(base rate {went.mean():.1%}: log loss "
                f"{-(went * np.log(went.mean()) + (1 - went) * np.log(1 - went.mean())).mean():.4f})"]
    report = "\n".join(out) + "\n"
    (OUT / "benchmark.md").write_text(report)
    df.to_csv(OUT / "test_predictions.csv", index=False)
    print(report)
