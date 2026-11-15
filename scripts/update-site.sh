#!/bin/zsh
# Updates the public demo end to end:
#   1. scrapes what's new from UFC.com into Postgres
#   2. exports the SQLite snapshot (data/mma.db)
#   3. retrains the model (src/Web/Data/*.json)
#   4. commits every change in the repo and pushes it to GitHub
#   5. deploys the commit and the snapshot to the Hugging Face Space
#
#   scripts/update-site.sh                    commit message "Update data to <today>"
#   scripts/update-site.sh "Some message"     your own commit message
#   scripts/update-site.sh --no-scrape        skip step 1 (e.g. the weekly job just ran)
#   scripts/update-site.sh --yes              don't ask before committing
#
# Needs Postgres.app running and `hf auth login` done once (ml/.venv/bin/hf).
set -eu

space="rmill/mma-hub"
repo="${0:A:h:h}"
scrape=1
confirm=1
message="Update data to $(date '+%Y-%m-%d')"
for arg in "$@"; do
  case "$arg" in
    --no-scrape) scrape=0 ;;
    --yes) confirm=0 ;;
    -*) sed -n '2,14p' "$0" | sed 's/^# \{0,1\}//'; exit 1 ;;
    *) message="$arg" ;;
  esac
done

step() { print -P "\n%B==> $1%b" }
cd "$repo"

if (( scrape )); then
  step "Scraping UFC.com"
  (cd src/Scraper && dotnet run)
fi

step "Exporting the snapshot to data/mma.db"
(cd src/Web && dotnet run -- export-sqlite ../../data/mma.db)

step "Retraining the model"
ml/.venv/bin/python ml/train.py

step "Committing"
git add -A
if git diff --cached --quiet; then
  echo "Nothing changed in the repo; deploying the last commit."
else
  git status --short
  if (( confirm )); then
    read -q "?Commit these as \"$message\"? [y/N] " || { echo; git reset -q; echo "Stopped. Nothing committed or deployed."; exit 1; }
    echo
  fi
  git commit -q -m "$message"
  git log -1 --oneline
fi

step "Pushing to GitHub"
git push

step "Deploying to Hugging Face ($space)"
ml/.venv/bin/python deploy/push_to_space.py "$space"
