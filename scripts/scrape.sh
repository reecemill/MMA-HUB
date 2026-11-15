#!/bin/zsh
# One scraper run, as the weekly job does it (see schedule-scrape.sh). Downloads only
# what's new from UFC.com, then rebuilds the tables. Output is appended to
# src/Scraper/logs/scraper.log.
set -u

repo="${0:A:h:h}"
log_dir="$repo/src/Scraper/logs"
mkdir -p "$log_dir"
exec >>"$log_dir/scraper.log" 2>&1

echo "=== $(date '+%Y-%m-%d %H:%M:%S') scrape started"
cd "$repo/src/Scraper" || exit 1

# caffeinate -i keeps the Mac from idle-sleeping until the run finishes.
/usr/bin/caffeinate -i /usr/local/share/dotnet/dotnet run
code=$?

echo "=== $(date '+%Y-%m-%d %H:%M:%S') scrape finished (exit $code)"
exit $code
