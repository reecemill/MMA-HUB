# MMA-HUB

One stop shop for all things UFC: every fighter and event since UFC 1, and a model that predicts fights, including upcoming bouts, recent ones (picked before they happened), and dream matchups between any two fighters.

**Live demo: [rmill-mma-hub.hf.space](https://rmill-mma-hub.hf.space)**

## What's in it

- **Fighters**: records, physical stats, career striking and grappling numbers, and every UFC fight.
- **Events**: every card with results, and the model's pick for each bout.
- **Dream Fight**: pick any two fighters, past or present, and see the model's prediction, the factors behind it, and a side-by-side comparison.
- **How It Works**: the model's test results, what went into it, and what was left out.

## Prediction model

A logistic regression on each fighter's UFC history before the fight:

- **Record**: an Elo rating, win rate, last three results, streak, number of UFC fights, and time since their last fight.
- **Finishing**: knockouts and submissions per fight, and how often they've been finished.
- **Striking**: significant strikes landed and absorbed per minute, accuracy, defense, and knockdowns.
- **Grappling**: takedowns per 15 minutes, takedown accuracy and defense, net control time, and submission attempts.
- **Age** on the day of the fight, from date of birth.

Everything is computed from fights before the one being predicted, so the model never sees a result it's asked to predict. The striking and grappling rates are built fight by fight from ufcstats.com's per-fight stats (via [a public export](https://github.com/Greco1899/scrape_ufc_stats), matched to 98.9% of bouts), and pulled toward the league average for fighters with little UFC fight time. UFC.com's career stats aren't used, because they're current totals that include the fights being predicted.

The settings (model, feature set, Elo K-factor, and the first year the weights learn from) were chosen on 2022–2023. The model's weights learn from fights since 2010: including earlier fights made predictions worse on the validation years, probably because the sport has changed so much since its early days. Those fights still count toward every fighter's record, Elo rating, and stats. The model was then trained on 2010–2023 and tested once on the 1,300 fights from January 2024 to July 2026:

| Method | Picks the winner | Log loss |
|---|---|---|
| This model | 64.6% | 0.633 |
| The model before per-fight stats (record, Elo, finishing; every year) | 59.8% | 0.665 |
| Whoever has the better UFC win rate | 60.2% | – |
| Whoever has the higher Elo rating | 58.2% | 0.676 |
| Coin flip | 50.0% | 0.693 |

It picks 4.8 percentage points more winners than the old model (95% paired-bootstrap interval 1.9 to 7.5) and 4.4 more than backing the better record (1.4 to 7.4), with a lower log loss. On 2024–2026 its probabilities are a little cautious (when it gives the favorite 60–70%, the favorite wins 67% of the time), but not on the validation years, so no correction was fitted to the test years.

Gradient boosting (63.8%, log loss 0.641) and a small neural network (63.8%, 0.636) had the same choices of features and training years and were chosen the same way. Both were worse than the logistic regression on log loss (the network edged it on the validation years by 0.0004, well within noise), so the site keeps the model whose reasons for each pick can be shown exactly. Height, reach, stance, and matchup terms (one fighter's attack against the other's defense) made predictions worse on the validation years and were left out.

### Data problems found along the way

The first version picked 71% of winners, which was too good to be true. The Cito API lists most recent events twice under different slugs (`ufc-322` and `ufc-322-della-maddalena-vs-makhachev`), and some fights twice on one card or on an unrelated later card, so a fight's own result leaked into the history used to predict it. The scraper now marks these (`src/Scraper/EventDeduplicator.cs`) and the site and model skip them. The API also lists the loser first in almost every bout, so each bout is used both ways round in training.

## How it's built

```
src/Scraper/   .NET console app: pulls fighters, events, and bouts from the Cito API into Postgres
src/Web/       ASP.NET Core Razor Pages site
ml/train.py    compares and tests the models; writes src/Web/Data/*.json for the site
ml/ufcstats.py per-fight stats from the ufcstats.com export, matched to our bouts
deploy/        Hugging Face Space setup
```

The site reads Postgres locally. The public demo reads a read-only SQLite snapshot of the same data, so it needs no database server. The model is trained in Python; the site only combines the exported weights with each fighter's exported features, so the feature code lives in one place. The site's predictions match the Python model's on every upcoming bout.

## Running it

Needs the .NET 10 SDK, Postgres, and a [Cito API](https://citoapi.com) key.

```bash
# Scraper: set secrets once, create the database, then scrape (slow: one request per fighter and event)
cd src/Scraper
dotnet user-secrets set "ConnectionStrings:MmaDb" "Host=localhost;Database=mma;Username=...;Password=..."
dotnet user-secrets set "CitoApi:ApiKey" "..."
dotnet ef database update
dotnet run                    # or: dotnet run -- --dedupe-only  (re-mark duplicates, no API calls)

# Web
cd ../Web
dotnet user-secrets set "ConnectionStrings:MmaDb" "Host=localhost;Database=mma;Username=...;Password=..."
dotnet run                    # http://localhost:5072

# Model: export a snapshot, download the per-fight stats, then train
dotnet run -- export-sqlite ../../data/mma.db
cd ../..
python3 -m venv ml/.venv && ml/.venv/bin/pip install -r ml/requirements.txt
ml/.venv/bin/python ml/download_ufcstats.py     # into data/ufcstats/, pinned to a commit of the export
ml/.venv/bin/python ml/train.py                 # --dry-run: print the comparison without writing files
```

## Public demo

The demo is a Docker Space on Hugging Face running the site against `data/mma.db`. To update it after changing code, data, or the model:

```bash
docker build -t mma-hub . && docker run -p 7860:7860 mma-hub   # optional: check it locally
pip install huggingface_hub && hf auth login
python deploy/push_to_space.py rmill/mma-hub
```

The data is a snapshot, so "upcoming" means upcoming when it was collected; the site's footer shows the date.

## Credits

Data from the [Cito API](https://citoapi.com), which collects it from UFC.com. The model's per-fight stats are from [ufcstats.com](http://ufcstats.com), via the CSV export at [Greco1899/scrape_ufc_stats](https://github.com/Greco1899/scrape_ufc_stats); they're downloaded when training and not included here. Not affiliated with the UFC.
