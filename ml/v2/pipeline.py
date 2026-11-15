"""The training steps behind `python -m ml.v2` (see __main__.py).

Splits are by date only: settings are chosen on 2023 with models trained through 2022,
the calibrator is fit on out-of-year predictions from 2016-2023, and the test is
2024-01-01 onward, scored once with models trained through 2023 (the same protocol as the
site's current model, whose held-out picks are compared on the same fights).
"""

import json
import time
from pathlib import Path

import numpy as np
import pandas as pd

from . import calibration, data, evaluate, features, models
from .features import FeatureParams
from .glicko import GlickoParams

ROOT = Path(__file__).resolve().parents[2]
MODELS = ROOT / "ml" / "models"
CURRENT_MODEL_PICKS = ROOT / "src" / "Web" / "Data" / "past_predictions.json"

VALID_START = pd.Timestamp("2023-01-01")
TEST_START = pd.Timestamp("2024-01-01")

# Tau (how fast volatility can change) made no measurable difference, so it stays at 0.5.
GLICKO_GRID = [GlickoParams(rd0=rd, period_days=d) for rd in (200, 275, 350) for d in (15, 30, 60, 120)]
GLICKO_TUNING = ("2010-01-01", VALID_START)

# The first year the weights learn from. Fighter histories always go back to UFC 1.
SINCE = [pd.Timestamp("2005-01-01"), pd.Timestamp("2010-01-01"), pd.Timestamp("2015-01-01")]

CORE = ["rating", "rd", "age", "reach", "southpaw", "slpm", "sapm", "str_acc", "str_def", "kd15",
        "kd_against15", "td15", "td_acc", "td_def", "sub15", "ctrl", "finish_rate", "last3", "log_layoff",
        "log_fights"]
CANDIDATES = ["height", "distance_share", "ground_share", "head_share", "ko_loss_rate", "round_share",
              "log_minutes", "off_ko_loss", "five_round", "class_change", "past_misses"]
CONTEXT = list(features.CONTEXT)
LGB_GRID = [{"num_leaves": leaves, "min_data_in_leaf": leaf_size} for leaves in (7, 15) for leaf_size in (50, 150)]
# A candidate feature joins only if it improves 2023 log loss by at least this much.
MIN_GAIN = 0.001
CALIBRATION_YEARS = range(2016, 2024)


def log(message):
    print(message, flush=True)


def prepare():
    """Loads the data, fixes the hyperparameters that come from early data, builds features."""
    t = time.time()
    ds = data.load()
    shrink = features.estimate_shrinkage(ds, VALID_START)
    tuned = features.tune_glicko(ds, *GLICKO_TUNING, GLICKO_GRID)
    params = FeatureParams(tuned[0][1], shrink)
    feats = features.build(ds, params)
    last_result = ds.fights.loc[ds.fights.finished, "Date"].max()
    version = f"v2-{last_result:%Y%m%d}-{ds.fingerprint()}"
    log(f"Loaded {len(ds.fights)} fights and built features in {time.time() - t:.0f}s ({version})")
    return ds, feats, params, tuned, version


def eligible(rows):
    """Fights the winner and method models learn from: a winner by KO/TKO, submission or decision."""
    return rows.finished & rows.result.isin(["red", "blue"]) & rows.method.isin(["ko", "sub", "dec"])


def between(rows, start, end):
    return rows[(rows.date >= start) & (rows.date < end)]


def label(rows):
    return (rows.result == "red").to_numpy(float)


def features_summary():
    ds, feats, params, tuned, version = prepare()
    rows = feats.rows
    log("Glicko settings by 2010-2022 log loss (after one scale factor): " + ", ".join(
        f"rd0={p.rd0:g} period={p.period_days:g}d: {loss:.4f}" for loss, p in tuned[:5]))
    log(f"Shrinkage: {params.shrink}")
    done = rows[rows.finished]
    log(f"{len(done)} finished fights, {eligible(rows).sum()} usable for training, {(~rows.finished).sum()} upcoming")


def _fit_score(train, valid, feats, context, cfg):
    model = models.Winner(feats, context, cfg).fit(train, label(train), valid=(valid, label(valid)))
    return evaluate.scores(label(valid), model.predict(valid))["log_loss"], model


def winner():
    ds, feats, params, tuned, version = prepare()
    out = MODELS / version
    out.mkdir(parents=True, exist_ok=True)
    rows = feats.rows
    usable = rows[eligible(rows)]
    valid = between(usable, VALID_START, TEST_START)
    test = between(rows[rows.finished & rows.result.isin(["red", "blue"])], TEST_START, pd.Timestamp.max)
    log(f"Validation (2023): {len(valid)} fights. Test (2024-01-01 on): {len(test)} decided fights.")

    # --- Settings on 2023, models trained through 2022 ---------------------------------
    trials = []
    best = None
    for since in SINCE:
        train = between(usable, since, VALID_START)
        for cfg in LGB_GRID:
            loss, model = _fit_score(train, valid, CORE, [], cfg)
            trials.append({"stage": "settings", "since": f"{since:%Y}", **cfg, "features": "core",
                           "rounds": model.rounds, "valid_log_loss": loss})
            log(f"  since {since:%Y} {cfg} rounds={model.rounds:<4} valid log loss {loss:.4f}")
            if best is None or loss < best["loss"]:
                best = {"loss": loss, "since": since, "cfg": cfg}
    since, cfg = best["since"], best["cfg"]
    train = between(usable, since, VALID_START)

    chosen, context, base = list(CORE), [], best["loss"]
    remaining = CANDIDATES + CONTEXT
    while remaining:
        results = []
        for cand in remaining:
            f_, c_ = (chosen, context + [cand]) if cand in CONTEXT else (chosen + [cand], context)
            loss, _ = _fit_score(train, valid, f_, c_, cfg)
            results.append((loss, cand))
            trials.append({"stage": "add feature", "since": f"{since:%Y}", **cfg, "features": f"+{cand}",
                           "rounds": None, "valid_log_loss": loss})
        loss, cand = min(results)
        log(f"  best addition: {cand} ({base:.4f} -> {loss:.4f})")
        if base - loss < MIN_GAIN:
            break
        (context if cand in CONTEXT else chosen).append(cand)
        remaining.remove(cand)
        base = loss
    valid_loss, tuned_model = _fit_score(train, valid, chosen, context, cfg)
    rounds = tuned_model.rounds

    baseline_loss, baseline_since = min(
        (evaluate.scores(label(valid), models.Baseline().fit(t, label(t)).predict(valid))["log_loss"], s)
        for s in SINCE for t in [between(usable, s, VALID_START)])
    log(f"Chosen: since {since:%Y}, {cfg}, {rounds} rounds, {len(chosen)} features + context {context}; "
        f"valid log loss {valid_loss:.4f} (baseline {baseline_loss:.4f}, since {baseline_since:%Y})")

    # --- Calibration from out-of-year predictions, 2016-2023 --------------------------
    oof_p, oof_y, oof_year = [], [], []
    for year in CALIBRATION_YEARS:
        start, end = pd.Timestamp(f"{year}-01-01"), pd.Timestamp(f"{year + 1}-01-01")
        fit_on = between(usable, since, start)
        m = models.Winner(chosen, context, cfg, rounds).fit(fit_on, label(fit_on))
        held = between(usable, start, end)
        oof_p.append(m.predict(held))
        oof_y.append(label(held))
        oof_year.append(np.full(len(held), year))
    oof_p, oof_y, oof_year = map(np.concatenate, (oof_p, oof_y, oof_year))

    def cross_fit(make):
        """Fit on 2016-2019, score 2020-2023, and the other way round."""
        early = oof_year < 2020
        losses = []
        for fit_mask in (early, ~early):
            cal = make().fit(oof_p[fit_mask], oof_y[fit_mask])
            losses.append(evaluate.scores(oof_y[~fit_mask], cal.apply(oof_p[~fit_mask]))["log_loss"] * (~fit_mask).sum())
        return sum(losses) / len(oof_y)

    raw_cv = evaluate.scores(oof_y, oof_p)["log_loss"]
    temp_cv, iso_cv = cross_fit(calibration.Temperature), cross_fit(calibration.SymmetricIsotonic)
    temperature = calibration.Temperature().fit(oof_p, oof_y)
    isotonic = calibration.SymmetricIsotonic().fit(oof_p, oof_y)
    calibrator = isotonic if iso_cv < temp_cv - 0.001 else temperature
    log(f"Calibration on 2016-2023 out-of-year predictions: raw {raw_cv:.4f}, temperature {temp_cv:.4f} "
        f"(a={temperature.a:.3f}), isotonic {iso_cv:.4f} -> {calibrator.kind}")

    # --- The test, once ---------------------------------------------------------------
    fit_on = between(usable, since, TEST_START)
    final = models.Winner(chosen, context, cfg, rounds).fit(fit_on, label(fit_on))
    base_on = between(usable, baseline_since, TEST_START)
    baseline = models.Baseline().fit(base_on, label(base_on))

    y = label(test)
    raw = final.predict(test)
    preds = {"Baseline (logistic regression)": baseline.predict(test),
             "LightGBM, uncalibrated": raw,
             f"LightGBM, calibrated ({calibrator.kind})": calibrator.apply(raw)}
    other = isotonic if calibrator is temperature else temperature
    extra = {f"LightGBM, {other.kind} (not chosen)": other.apply(raw)}

    current = json.loads(CURRENT_MODEL_PICKS.read_text())["fighter1WinProbability"]
    has_current = test.fight_id.astype(str).isin(current.keys()).to_numpy()
    preds_current = np.array([current.get(str(i), np.nan) for i in test.fight_id])

    results = {name: evaluate.scores(y, p) for name, p in {**preds, **extra}.items()}
    results["Current site model"] = evaluate.scores(y[has_current], preds_current[has_current])
    calibrated = preds[f"LightGBM, calibrated ({calibrator.kind})"]
    comparisons = {
        "LightGBM calibrated vs current site model": evaluate.paired_bootstrap(
            y[has_current], calibrated[has_current], preds_current[has_current]),
        "LightGBM calibrated vs baseline": evaluate.paired_bootstrap(y, calibrated, preds["Baseline (logistic regression)"]),
    }
    buckets = {name: evaluate.favorite_buckets(y, p) for name, p in preds.items()}
    buckets["Current site model"] = evaluate.favorite_buckets(y[has_current], preds_current[has_current])
    symmetric_ok = bool(np.allclose(final.predict(test) + final.predict(_swapped(test)), 1))
    curves = {**preds}
    if has_current.all():
        curves["Current site model"] = preds_current
    evaluate.plot_reliability(y, curves, out / "reliability.png",
                              f"Test fights {TEST_START:%b %Y} to {test.date.max():%b %Y} ({len(test)})")

    # --- Save ---------------------------------------------------------------------------
    final.booster.save_model(str(out / "winner_eval.txt"), num_iteration=rounds)
    spec = {
        "version": version, "data_fingerprint": ds.fingerprint(),
        "splits": {"train_until": f"{VALID_START:%Y-%m-%d}", "valid": "2023", "test_from": f"{TEST_START:%Y-%m-%d}",
                   "learning_since": f"{since:%Y-%m-%d}", "calibration_years": [CALIBRATION_YEARS.start, CALIBRATION_YEARS.stop - 1]},
        "glicko": vars(params.glicko), "shrinkage": {"minutes": params.shrink.minutes, "attempts": params.shrink.attempts},
        "features": chosen, "context": context, "lightgbm": {**models.LGB_BASE, **cfg, "rounds": rounds},
    }
    (out / "feature_spec.json").write_text(json.dumps(spec, indent=2, default=str))
    (out / "calibration.json").write_text(json.dumps(calibrator.to_json()))
    (out / "baseline.json").write_text(json.dumps({"features": models.Baseline.FEATURES, "since": f"{baseline_since:%Y-%m-%d}",
                                                    "scale": baseline.scale.tolist(), "coef": baseline.lr.coef_[0].tolist()}))
    metrics = {"test": results, "comparisons": comparisons, "favorite_buckets": buckets,
               "current_model_coverage": int(has_current.sum()), "symmetric": symmetric_ok,
               "valid_log_loss": valid_loss, "baseline_valid_log_loss": baseline_loss,
               "calibration_cv": {"raw": raw_cv, "temperature": temp_cv, "isotonic": iso_cv},
               "importance": final.importance(),
               "glicko_tuning": [{"rd0": p.rd0, "period_days": p.period_days, "log_loss": loss} for loss, p in tuned]}
    (out / "metrics.json").write_text(json.dumps(metrics, indent=2))
    pd.DataFrame(trials).to_csv(out / "selection.csv", index=False)
    (out / "evaluation.md").write_text(_report(version, spec, metrics, len(test)))
    log(f"\n{(out / 'evaluation.md').read_text()}\nSaved to {out}")
    return out


def _swapped(rows):
    """The same fights with the corners swapped, for the symmetry check."""
    swapped = rows.copy()
    a_cols = [c for c in rows.columns if c.startswith("a_")]
    for a in a_cols:
        b = "b_" + a[2:]
        swapped[a], swapped[b] = rows[b].to_numpy(), rows[a].to_numpy()
    return swapped


def _report(version, spec, m, n_test):
    def pct(x):
        return f"{100 * x:.1f}%"

    lines = [f"# Winner model evaluation ({version})", "",
             f"Test: {n_test} decided UFC fights from {spec['splits']['test_from']} (models trained on fights before then; "
             f"settings chosen on 2023 with models trained through 2022). Learning from {spec['splits']['learning_since'][:4]} on.", "",
             "| Model | Accuracy | Log loss | Brier |", "|---|---|---|---|"]
    for name, s in m["test"].items():
        lines.append(f"| {name} | {pct(s['accuracy'])} | {s['log_loss']:.4f} | {s['brier']:.4f} |")
    lines += ["", "Paired bootstrap, new minus old, 95% interval (negative log loss is better):", ""]
    for name, c in m["comparisons"].items():
        lines.append(f"- {name}: accuracy {100 * c['accuracy'][0]:+.1f} points [{100 * c['accuracy'][1]:+.1f}, "
                     f"{100 * c['accuracy'][2]:+.1f}], log loss {c['log_loss'][0]:+.4f} [{c['log_loss'][1]:+.4f}, {c['log_loss'][2]:+.4f}]")
    lines += ["", "How often the favorite won, by how strongly it was favored:", "",
              "| Model | " + " | ".join(f"{int(lo * 100)}-{int(min(hi, 1) * 100)}%" for lo, hi in evaluate.BUCKETS) + " |",
              "|---|" + "---|" * len(evaluate.BUCKETS)]
    for name, rows in m["favorite_buckets"].items():
        cells = {(b["from"]): f"{pct(b['favorite_won'])} won, said {pct(b['predicted'])} ({b['fights']})" for b in rows}
        lines.append(f"| {name} | " + " | ".join(cells.get(lo, "–") for lo, _ in evaluate.BUCKETS) + " |")
    lines += ["", f"Symmetric (P(A) + P(B) = 1 on every test fight): {m['symmetric']}",
              f"Calibration, cross-fit on 2016-2023: raw {m['calibration_cv']['raw']:.4f}, temperature "
              f"{m['calibration_cv']['temperature']:.4f}, isotonic {m['calibration_cv']['isotonic']:.4f}",
              f"Glicko: starting deviation {spec['glicko']['rd0']:g}, {spec['glicko']['period_days']:g}-day inactivity periods "
              f"(2010-2022 log loss of the rating alone, rescaled: {m['glicko_tuning'][0]['log_loss']:.4f})", "",
              "Feature importance (share of gain):", ""]
    lines += [f"- {k}: {v:.3f}" for k, v in list(m["importance"].items())[:15]]
    return "\n".join(lines) + "\n"
