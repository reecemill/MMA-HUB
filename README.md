# MMA-HUB

One stop shop for all things UFC: every fighter and event since UFC 1, and a model that predicts fights, including upcoming bouts, recent ones (picked before they happened), and dream matchups between any two fighters.

**Live demo: [rmill-mma-hub.hf.space](https://rmill-mma-hub.hf.space)**

## What's in it

- **Fighters**: records, physical stats, career striking and grappling numbers built up from every UFC fight, and the full UFC fight history.
- **Events**: every card with results, how each finish happened, judges' scorecards, and the model's pick for each fight.
- **Dream Fight**: pick any two fighters, past or present, and see the model's prediction, the factors behind it, and a side-by-side comparison.
- **How It Works**: the model's test results, what went into it, and what was left out.

## Prediction model

A logistic regression on each fighter's UFC history before the fight:

- **Record**: an Elo rating, win rate, last three results, streak, number of UFC fights, and time since their last fight.
- **Finishing**: knockouts and submissions per fight, and how often they've been finished.
- **Striking**: significant strikes landed and absorbed per minute, accuracy, defense, and knockdowns.
- **Grappling**: takedowns per 15 minutes, takedown accuracy and defense, net control time, and submission attempts.
- **Age** on the day of the fight, from date of birth.

Everything is computed from fights before the one being predicted, so the model never sees a result it's asked to predict. The striking and grappling rates are built fight by fight from UFC.com's stats for each fight (available for 99.8% of fights), and pulled toward the league average for fighters with little UFC fight time. UFC.com's career totals aren't used, because they include the fights being predicted.

The settings (model, feature set, Elo K-factor, and the first year the weights learn from) were chosen on 2022–2023. The model's weights learn from fights since 2010: including earlier fights made predictions worse on the validation years, probably because the sport has changed so much since its early days. Those fights still count toward every fighter's record, Elo rating, and stats. The model was then trained on 2010–2023 and tested once on the 1,514 fights from January 2024 to November 2026:

| Method | Picks the winner | Log loss |
|---|---|---|
| This model | 63.8% | 0.636 |
| The model before per-fight stats (record, Elo, finishing; every year) | 59.8% | 0.667 |
| Whoever has the better UFC win rate | 60.1% | – |
| Whoever has the higher Elo rating | 57.7% | 0.678 |
| Coin flip | 50.0% | 0.693 |

It picks 4.0 percentage points more winners than the old model (95% paired-bootstrap interval 1.5 to 6.5) and 3.7 more than backing the better record (1.1 to 6.4), with a lower log loss. Its probabilities are a little cautious: when it gives the favorite 60–70%, the favorite wins 66% of the time.

Gradient boosting (62.7%, log loss 0.645) and a small neural network (63.3%, 0.638) had the same choices of features and training years and were chosen the same way. Neither beat the logistic regression on log loss (the network was within noise of it), so the site keeps the model whose reasons for each pick can be shown exactly. Height, reach, stance, and matchup terms (one fighter's attack against the other's defense) made predictions worse on the validation years and were left out.

### Data problems found along the way

The first version picked 71% of winners, which was too good to be true. The data then came from a third-party API that listed most recent events twice and some fights twice, so a fight's own result leaked into the history used to predict it. The project now reads UFC.com directly, where each fight appears once, and the training script checks that no fight is counted twice. UFC.com lists the red corner first, which is usually the champion or the favorite, so each fight is used both ways round in training.

## How it's built

```
src/Scraper/   .NET console app: downloads UFC.com's events, fights, stats, fighters and rankings into Postgres
src/Web/       ASP.NET Core Razor Pages site
ml/train.py    compares and tests the models; writes src/Web/Data/*.json for the site
deploy/        Hugging Face Space setup
```

The scraper reads the JSON that UFC.com's own event pages load: every card, result, judges' scorecard and play-by-play, and each fight's stats in total and round by round, with strikes split by position and target and control time by position. Fighter bios come from the athlete pages and the rankings from ufc.com/rankings, at the one page per 15 seconds that UFC.com's robots.txt asks for. Every response is saved under `data/ufc-com/`, and the database tables are rebuilt from those files on each run, so a run that stops carries on where it left off.

The site reads Postgres locally. The public demo reads a read-only SQLite snapshot of the same data, so it needs no database server. The model is trained in Python; the site only combines the exported weights with each fighter's exported features, so the feature code lives in one place. The site's predictions match the Python model's on every upcoming fight.

## Running it

Needs the .NET 10 SDK and Postgres. No API key.

```bash
# Scraper: set the connection string once, create the database, then scrape. The first run
# downloads every event and fight (2-3 hours; keep the machine awake). Later runs only
# fetch upcoming events, new results, and the pages of fighters who've just fought.
cd src/Scraper
dotnet user-secrets set "ConnectionStrings:MmaDb" "Host=localhost;Database=mma;Username=...;Password=..."
dotnet ef database update
dotnet run
dotnet run -- --offline          # rebuild the tables from the saved files, no downloads
dotnet run -- --all-athletes     # also every fighter's ufc.com page (about 12 hours the first time)

# Weekly scrape (macOS): runs the scraper every Monday at 12:00 AM; a Mac that's asleep
# then runs it on waking. Keep the repo out of ~/Desktop, ~/Documents and ~/Downloads:
# macOS doesn't let scheduled jobs read those folders.
cd ../..
scripts/schedule-scrape.sh install   # also: uninstall, run (now), status
cd src/Scraper

# Web
cd ../Web
dotnet user-secrets set "ConnectionStrings:MmaDb" "Host=localhost;Database=mma;Username=...;Password=..."
dotnet run                    # http://localhost:5072

# Model: export a snapshot, then train
dotnet run -- export-sqlite ../../data/mma.db
cd ../..
python3 -m venv ml/.venv && ml/.venv/bin/pip install -r ml/requirements.txt
ml/.venv/bin/python ml/train.py                 # --dry-run: print the comparison without writing files
```

## Public demo

The demo is a Docker Space on Hugging Face running the site against `data/mma.db`. To scrape, export, retrain, commit everything, push to GitHub and deploy in one go:

```bash
scripts/update-site.sh              # --no-scrape, --yes, or a commit message
```

Or step by step, after changing code, data, or the model:

```bash
docker build -t mma-hub . && docker run -p 7860:7860 mma-hub   # optional: check it locally
pip install huggingface_hub && hf auth login
python deploy/push_to_space.py rmill/mma-hub
```

The data is a snapshot, so "upcoming" means upcoming when it was collected; the site's footer shows the date.

## Credits

Data from [UFC.com](https://www.ufc.com). Not affiliated with the UFC.
