"""The features for a fight may only use what was known before it.

    ml/.venv/bin/python -m pytest ml/v2/tests

Builds features from the real database, then rebuilds them from changed data and checks
that nothing a fight shouldn't know about can move its features.
"""

import re
from pathlib import Path

import numpy as np
import pandas as pd
import pytest

from ml.v2 import data, features
from ml.v2.data import Dataset
from ml.v2.glicko import GlickoParams


@pytest.fixture(scope="module")
def dataset():
    return data.load()


@pytest.fixture(scope="module")
def params(dataset):
    # Fixed settings for every build, as in training: only the data changes.
    return features.FeatureParams(GlickoParams(), features.estimate_shrinkage(dataset, "2023-01-01"))


@pytest.fixture(scope="module")
def full(dataset, params):
    return features.build(dataset, params).rows.set_index("fight_id")


def feature_columns(rows):
    return [c for c in rows.columns if c.startswith(("a_", "b_"))]


def assert_same_features(rows, expected):
    rows = rows.set_index("fight_id") if "fight_id" in rows.columns else rows
    cols = feature_columns(expected)
    pd.testing.assert_frame_equal(rows.loc[expected.index, cols], expected[cols], check_exact=True)


def events_by_rank(dataset):
    return dataset.fights.drop_duplicates("EventId")[["EventId", "event_rank", "Date"]].sort_values("event_rank")


def test_features_for_a_fight_ignore_every_later_fight(dataset, params, full):
    """Cut the data after an event: every fight up to it gets exactly the same features."""
    events = events_by_rank(dataset)
    cuts = list(events[events.Date >= "2005-01-01"].sample(4, random_state=0).EventId) + [events.EventId.iloc[30]]
    for cut in cuts:
        rows = features.build(dataset.until(cut), params).rows
        assert len(rows) > 0
        assert_same_features(rows, full.loc[rows.fight_id])


def test_removing_a_later_event_changes_nothing_before_it(dataset, params, full):
    events = events_by_rank(dataset)
    removed = events[events.Date.dt.year == 2016].EventId.iloc[5]
    removed_rank = events.set_index("EventId").event_rank[removed]
    rows = features.build(dataset.without_events([removed]), params).rows.set_index("fight_id")

    before = full[full.index.isin(dataset.fights[dataset.fights.event_rank < removed_rank].Id)]
    assert_same_features(rows, before)

    # The check can see a difference when there is one: fights after the removed card do change.
    after = full.index.intersection(rows.index).difference(before.index)
    cols = feature_columns(full)
    changed = ~np.isclose(rows.loc[after, cols].to_numpy(float), full.loc[after, cols].to_numpy(float), equal_nan=True)
    assert changed.any()


def scrambled(dataset, event_id):
    """The same data with every result and stat on one card changed."""
    f = dataset.fights.copy()
    on_card = f.EventId == event_id
    flip = {"red": "blue", "blue": "red", "draw": "red", "nc": "blue"}
    f.loc[on_card, "result"] = f.loc[on_card, "result"].map(flip)
    f.loc[on_card, "WinnerFighterId"] = np.where(f.loc[on_card, "result"] == "red", f.loc[on_card, "RedId"], f.loc[on_card, "BlueId"])
    f.loc[on_card, "method_group"] = "ko"
    f.loc[on_card, "DurationSeconds"] = 17.0
    stats = dataset.stats.copy()
    card_fights = set(f.loc[on_card, "Id"])
    numeric = [c for c in stats.columns if c not in ("FightId", "FighterId")]
    stats.loc[stats.FightId.isin(card_fights), numeric] = stats.loc[stats.FightId.isin(card_fights), numeric] * 3 + 5
    return Dataset(f, dataset.fighters, stats, dataset.round_scores)


def test_fighters_on_the_same_card_dont_see_each_others_results(dataset, params, full):
    events = events_by_rank(dataset)
    for event_id in (events[events.Date.dt.year == 2019].EventId.iloc[10], events.EventId.iloc[0]):
        rows = features.build(scrambled(dataset, event_id).until(event_id), params).rows
        card = rows[rows.event_id == event_id]
        assert len(card) > 0
        assert_same_features(card, full.loc[card.fight_id])


def test_a_tournament_fighter_goes_into_every_fight_that_night_as_they_started_it(dataset, full):
    """UFC 1-era fighters fought up to four times in a night; each fight sees the night's starting state."""
    f = dataset.fights
    first_event = f.EventId.iloc[0]
    night = full[full.event_id == first_event]
    sides = pd.concat([
        night[["red_id"] + [c for c in night.columns if c.startswith("a_")]].set_axis(
            ["fighter"] + [c[2:] for c in night.columns if c.startswith("a_")], axis=1),
        night[["blue_id"] + [c for c in night.columns if c.startswith("b_")]].set_axis(
            ["fighter"] + [c[2:] for c in night.columns if c.startswith("b_")], axis=1)])
    repeated = sides[sides.fighter.duplicated(keep=False)]
    assert len(repeated) > 0, "UFC 1 should have fighters with more than one fight"
    for _, group in repeated.groupby("fighter"):
        values = group.drop(columns="fighter")
        assert (values.nunique(dropna=False) == 1).all()
    # And that starting state is a debut: nobody on UFC 1 has a UFC fight or a rating yet.
    assert (sides.log_fights == 0).all() and (sides.rating == 1500).all() and sides.log_layoff.isna().all()


def test_the_loader_never_reads_leaky_columns():
    sql = " ".join(data.QUERIES.values())
    for leaky in ("UfcFighterProfiles", "UfcRankings", '"Wins"', '"Losses"', '"Draws"', '"NoContests"', '"WeightLbs"'):
        assert leaky not in sql
    # The fight's weight class is known beforehand; the fighter's (from their latest fight) isn't.
    assert "WeightClass" not in data.QUERIES["fighters"]


def test_only_the_loader_queries_the_database():
    here = Path(data.__file__).parent
    for path in here.glob("*.py"):
        if path.name not in ("data.py", "db.py"):
            assert not re.search(r'"Ufc[A-Za-z]+"', path.read_text()), f"{path.name} reads a Ufc table directly"
