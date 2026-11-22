"""Step 4: the method and round models (`python -m ml.v2 method-round`).

Same protocol as the winner model: settings chosen on 2023 with models trained through
2022, then refit through 2023 and scored once on 2024-01-01 onward. Feature building
uses the saved winner model's Glicko and shrinkage settings, and the winner
probabilities that keep method consistent with the pick come from that model, so the
three models describe the same fight.

Benchmarks on the test fights: base rates; the winner model times base rates (how much
the method model adds beyond knowing who wins); and, where BestFightOdds closing props
exist (ml/odds/out/closing_lines.csv), the betting market.
"""

import json
from pathlib import Path

import lightgbm as lgb
import numpy as np
import pandas as pd

from . import data, features, models
from .calibration import Temperature
from .features import FeatureParams, Shrinkage
from .glicko import GlickoParams
from .outcome import CLASSES, Method, Round, ending_class, method_labels, multiclass_logloss, round_logloss
from .pipeline import MODELS, TEST_START, VALID_START, between, eligible, label, log

ROOT = Path(__file__).resolve().parents[2]
ODDS = ROOT / "ml" / "odds" / "out" / "closing_lines.csv"

EXTRA = ["ko_rate", "sub_rate", "ko_loss_rate", "sub_loss_rate", "dec_rate"]
PAIRS = EXTRA + ["kd15", "kd_against15", "sub15", "td15", "slpm", "sapm", "str_acc", "str_def", "age", "rd",
                 "log_fights", "rating"]
ROUND_ABSDIFFS = ["rating", "age", "reach", "slpm", "sapm", "kd15", "sub15", "td15", "ctrl"]
GRID = [{"num_leaves": leaves, "min_data_in_leaf": leaf} for leaves in (7, 15) for leaf in (50, 150)]


def _load_winner():
    model_dir = max(MODELS.glob("v2-*"))
    spec = json.loads((model_dir / "feature_spec.json").read_text())
    temp = Temperature()
    temp.a = json.loads((model_dir / "calibration.json").read_text())["a"]
    return model_dir, spec, temp


def _winner_p(spec, temp, train, rows, booster_file=None):
    cfg = {k: spec["lightgbm"][k] for k in ("num_leaves", "min_data_in_leaf")}
    w = models.Winner(spec["features"], spec["context"], cfg, spec["lightgbm"]["rounds"])
    if booster_file:
        w.booster = lgb.Booster(model_file=str(booster_file))
    else:
        w.fit(train, label(train))
    return temp.apply(w.predict(rows))


def _base_method(train):
    """Symmetric class frequencies: P(A by KO) = P(B by KO) = half the share of KO wins."""
    share = pd.Series(method_labels(train) % 3).value_counts(normalize=True).reindex([0, 1, 2]).to_numpy()
    return np.concatenate([share, share]) / 2


def _base_round(train):
    """Share of fights ending in each round / decision, separately for 3- and 5-round fights."""
    out = {}
    for s in (3, 5):
        t = train[train.scheduled_rounds == s]
        y = ending_class(t)
        p = np.zeros(6)
        for k in range(s):
            p[k] = (y == k).mean()
        p[5] = (y == s).mean()
        out[s] = p
    return out


def _market(rows):
    """Market method (6, CLASSES order) and round (6: rounds 1-5 then decision) probabilities,
    vig removed by normalizing, for test fights where every needed prop is priced."""
    if not ODDS.exists():
        return None, None
    lines = pd.read_csv(ODDS)
    wide = lines.pivot_table(index="fight_id", columns="line", values="p", aggfunc="first")
    m_cols = ["RED wins by TKO/KO", "RED wins by submission", "RED wins by decision",
              "BLUE wins by TKO/KO", "BLUE wins by submission", "BLUE wins by decision"]
    method = {}
    rnd = {}
    for r in rows.itertuples():
        if r.fight_id not in wide.index:
            continue
        w = wide.loc[r.fight_id]
        if all(c in w and pd.notna(w[c]) for c in m_cols):
            v = w[m_cols].to_numpy(float)
            method[r.fight_id] = v / v.sum()
        s = int(r.scheduled_rounds)
        need = [(f"RED wins in round {k}", f"BLUE wins in round {k}") for k in range(1, s + 1)]
        if all(a in w and b in w and pd.notna(w[a]) and pd.notna(w[b]) for a, b in need) and \
                pd.notna(w.get("RED wins by decision")) and pd.notna(w.get("BLUE wins by decision")):
            v = np.zeros(6)
            for k, (a, b) in enumerate(need):
                v[k] = w[a] + w[b]
            v[5] = w["RED wins by decision"] + w["BLUE wins by decision"]
            rnd[r.fight_id] = v / v.sum()
    return method, rnd


def _choose(make, train, valid, score):
    trials = []
    for cfg in GRID:
        m = make(cfg).fit(train, valid=valid)
        loss = score(m)
        trials.append((loss, cfg, m.rounds))
        log(f"  {cfg} rounds={m.rounds:<4} valid {loss:.4f}")
    return min(trials, key=lambda t: t[0])


def method_round():
    model_dir, spec, temp = _load_winner()
    out = model_dir / "method_round"
    out.mkdir(exist_ok=True)
    ds = data.load()
    params = FeatureParams(GlickoParams(**spec["glicko"]), Shrinkage(**spec["shrinkage"]))
    rows = features.build(ds, params).rows
    since = pd.Timestamp(spec["splits"]["learning_since"])
    usable = rows[eligible(rows)]
    train, valid = between(usable, since, VALID_START), between(usable, VALID_START, TEST_START)
    full_train = between(usable, since, TEST_START)
    test = between(usable, TEST_START, pd.Timestamp.max)
    modern = lambda r: r[r.modern & r.scheduled_rounds.isin([3, 5])]  # noqa: E731
    log(f"Method/round: train {len(train)} (from {since:%Y}), valid {len(valid)}, test {len(test)} fights")

    diffs = spec["features"] + [f for f in EXTRA if f not in spec["features"]]

    # --- Method ---------------------------------------------------------------------------
    log("Method model settings (2023 log loss, 6 classes):")
    yv = method_labels(valid)
    loss, cfg_m, rounds_m = _choose(lambda c: Method(diffs, PAIRS, c), train, valid,
                                    lambda m: multiclass_logloss(yv, m.predict(valid)))
    m_valid = Method(diffs, PAIRS, cfg_m, rounds_m).fit(train)
    pw_valid = _winner_p(spec, temp, train, valid)
    raw_v = multiclass_logloss(yv, m_valid.predict(valid))
    tied_v = multiclass_logloss(yv, m_valid.predict(valid, pw_valid))
    tie_to_winner = tied_v <= raw_v + 0.002  # consistency is worth a little; not a lot
    log(f"Method on 2023: raw {raw_v:.4f}, rescaled to the winner model {tied_v:.4f} -> "
        f"{'rescale' if tie_to_winner else 'raw'}")

    # --- Round ----------------------------------------------------------------------------
    log("Round model settings (2023 log loss over rounds + decision):")
    tr_r, va_r = modern(train), modern(valid)
    loss_r, cfg_r, rounds_r = _choose(lambda c: Round(ROUND_ABSDIFFS, PAIRS, c), tr_r, va_r,
                                      lambda m: round_logloss(va_r, m.predict(va_r)))
    r_valid = Round(ROUND_ABSDIFFS, PAIRS, cfg_r, rounds_r).fit(tr_r)
    pm_va_r = m_valid.predict(va_r, _winner_p(spec, temp, train, va_r) if tie_to_winner else None)
    p_fin_v = 1 - pm_va_r[:, 2] - pm_va_r[:, 5]
    raw_rv = round_logloss(va_r, r_valid.predict(va_r))
    tied_rv = round_logloss(va_r, r_valid.predict(va_r, p_fin_v))
    tie_round = tied_rv <= raw_rv + 0.002
    log(f"Round on 2023: raw {raw_rv:.4f}, P(finish) from the method model {tied_rv:.4f} -> "
        f"{'scale' if tie_round else 'raw'}")

    # --- Test, once -----------------------------------------------------------------------
    method = Method(diffs, PAIRS, cfg_m, rounds_m).fit(full_train)
    rnd = Round(ROUND_ABSDIFFS, PAIRS, cfg_r, rounds_r).fit(modern(full_train))
    pw = _winner_p(spec, temp, None, test, model_dir / "winner_eval.txt")
    y = method_labels(test)
    pm_raw = method.predict(test)
    pm_tied = method.predict(test, pw)
    pm = pm_tied if tie_to_winner else pm_raw
    base = _base_method(full_train)
    pm_base = np.tile(base, (len(test), 1))
    a, b = base[:3] / base[:3].sum(), base[3:] / base[3:].sum()
    pm_winner_base = np.hstack([pw[:, None] * a, (1 - pw[:, None]) * b])

    symmetric = bool(np.allclose(method.predict(test), method.predict(_swap(test))[:, [3, 4, 5, 0, 1, 2]]))

    te_r = modern(test)
    idx_r = test.index.get_indexer(te_r.index)
    pr_raw = rnd.predict(te_r)
    pr_tied = rnd.predict(te_r, 1 - pm[idx_r, 2] - pm[idx_r, 5])
    pr = pr_tied if tie_round else pr_raw
    base_r = _base_round(modern(full_train))
    pr_base = np.array([base_r[int(s)] for s in te_r.scheduled_rounds])

    def method_scores(yy, p):
        dec = (yy % 3 == 2).astype(float)
        pdec = p[:, 2] + p[:, 5]
        return {"log_loss": multiclass_logloss(yy, p), "top_pick": float((p.argmax(1) == yy).mean()),
                "method_only_log_loss": multiclass_logloss(yy % 3, p[:, :3] + p[:, 3:]),
                "decision_brier": float(((pdec - dec) ** 2).mean()),
                "decision_log_loss": float(-(dec * np.log(pdec) + (1 - dec) * np.log(1 - pdec)).mean())}

    res = {"method": {}, "round": {}}
    for name, p in {"Base rates": pm_base, "Winner model x base rates": pm_winner_base,
                    "Method model (raw)": pm_raw, "Method model (rescaled to winner)": pm_tied}.items():
        res["method"][name] = method_scores(y, p)
    for name, p in {"Base rates (by scheduled rounds)": pr_base, "Round model (raw)": pr_raw,
                    "Round model (scaled to method P(finish))": pr_tied}.items():
        res["round"][name] = {"log_loss": round_logloss(te_r, p), "fights": len(te_r)}
        for s in (3, 5):
            m = (te_r.scheduled_rounds == s).to_numpy()
            res["round"][name][f"log_loss_{s}"] = round_logloss(te_r[m], p[m])

    # --- Market, on the fights it priced -------------------------------------------------
    mk_m, mk_r = _market(test)
    market = {}
    if mk_m:
        have = test.fight_id.isin(mk_m.keys()).to_numpy()
        pmk = np.vstack([mk_m[f] for f in test.fight_id[have]])
        market["method"] = {"fights": int(have.sum()), "Market": method_scores(y[have], pmk),
                            "Method model": method_scores(y[have], pm[have]),
                            "Winner model x base rates": method_scores(y[have], pm_winner_base[have]),
                            "Base rates": method_scores(y[have], pm_base[have])}
    if mk_r:
        have = te_r.fight_id.isin(mk_r.keys()).to_numpy()
        prk = np.vstack([mk_r[f] for f in te_r.fight_id[have]])
        market["round"] = {"fights": int(have.sum()), "Market": round_logloss(te_r[have], prk),
                           "Round model": round_logloss(te_r[have], pr[have]),
                           "Base rates": round_logloss(te_r[have], pr_base[have])}

    # --- Save -----------------------------------------------------------------------------
    method.booster.save_model(str(out / "method.txt"), num_iteration=method.rounds)
    rnd.booster.save_model(str(out / "round.txt"), num_iteration=rnd.rounds)
    spec_out = {"method": {"diffs": diffs, "pairs": PAIRS, "lightgbm": {**method.params, "rounds": method.rounds},
                           "rescale_to_winner": bool(tie_to_winner), "classes": CLASSES},
                "round": {"absdiffs": ROUND_ABSDIFFS, "pairs": PAIRS, "lightgbm": {**rnd.params, "rounds": rnd.rounds},
                          "scale_to_method_finish": bool(tie_round)},
                "valid": {"method_raw": raw_v, "method_rescaled": tied_v, "round_raw": raw_rv, "round_scaled": tied_rv}}
    (out / "spec.json").write_text(json.dumps(spec_out, indent=2, default=str))
    metrics = {"test": res, "market": market, "symmetric": symmetric, "test_fights": len(test),
               "method_importance": method.importance(), "round_importance": rnd.importance()}
    (out / "metrics.json").write_text(json.dumps(metrics, indent=2))
    report = _report(spec_out, metrics, test)
    (out / "evaluation.md").write_text(report)
    log("\n" + report + f"\nSaved to {out}")


def _swap(rows):
    swapped = rows.copy()
    for a in [c for c in rows.columns if c.startswith("a_")]:
        b = "b_" + a[2:]
        swapped[a], swapped[b] = rows[b].to_numpy(), rows[a].to_numpy()
    return swapped


def _report(spec, m, test):
    t = m["test"]
    lines = [f"# Method and round models", "",
             f"Test: {m['test_fights']} UFC fights from {TEST_START:%Y-%m-%d} won by KO/TKO, submission or decision "
             f"(round model: the {t['round']['Round model (raw)']['fights']} under 3x5 or 5x5 rules).", "",
             "## Method (6 classes: who wins x KO/TKO, submission, decision)", "",
             "| | Log loss | Top pick right | Method-only log loss | P(decision) log loss | P(decision) Brier |",
             "|---|---|---|---|---|---|"]
    for name, s in t["method"].items():
        lines.append(f"| {name} | {s['log_loss']:.4f} | {s['top_pick']:.1%} | {s['method_only_log_loss']:.4f} | "
                     f"{s['decision_log_loss']:.4f} | {s['decision_brier']:.4f} |")
    lines += ["", f"Chosen on 2023: {'rescaled to the winner model' if spec['method']['rescale_to_winner'] else 'raw'} "
                  f"(raw {spec['valid']['method_raw']:.4f}, rescaled {spec['valid']['method_rescaled']:.4f}). "
                  f"Symmetric: {m['symmetric']}.", "",
              "## Round (rounds 1..S and decision)", "", "| | Log loss | 3-round | 5-round |", "|---|---|---|---|"]
    for name, s in t["round"].items():
        lines.append(f"| {name} | {s['log_loss']:.4f} | {s['log_loss_3']:.4f} | {s['log_loss_5']:.4f} |")
    lines += ["", f"Chosen on 2023: {'scaled to the method model P(finish)' if spec['round']['scale_to_method_finish'] else 'raw'} "
                  f"(raw {spec['valid']['round_raw']:.4f}, scaled {spec['valid']['round_scaled']:.4f})."]
    mk = m["market"]
    if mk.get("method"):
        lines += ["", f"## Against the betting market (closing props, {mk['method']['fights']} fights with all six method props)", "",
                  "| | Method log loss | Top pick right | P(decision) log loss |", "|---|---|---|---|"]
        for name in ("Market", "Method model", "Winner model x base rates", "Base rates"):
            s = mk["method"][name]
            lines.append(f"| {name} | {s['log_loss']:.4f} | {s['top_pick']:.1%} | {s['decision_log_loss']:.4f} |")
    if mk.get("round"):
        r = mk["round"]
        lines += ["", f"Round log loss on {r['fights']} fights with every round prop priced: market {r['Market']:.4f}, "
                      f"round model {r['Round model']:.4f}, base rates {r['Base rates']:.4f}."]
    lines += ["", "Method model, top features (share of gain):", ""]
    lines += [f"- {k}: {v:.3f}" for k, v in list(m["method_importance"].items())[:10]]
    lines += ["", "Round model, top features:", ""]
    lines += [f"- {k}: {v:.3f}" for k, v in list(m["round_importance"].items())[:10]]
    return "\n".join(lines) + "\n"
