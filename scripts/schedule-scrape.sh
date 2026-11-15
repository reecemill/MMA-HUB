#!/bin/zsh
# Runs scripts/scrape.sh every Monday at 12:00 AM via launchd.
#   scripts/schedule-scrape.sh install     set up (or update) the weekly job
#   scripts/schedule-scrape.sh uninstall   remove it
#   scripts/schedule-scrape.sh run         run the job now, in the background
#   scripts/schedule-scrape.sh status      last exit code and the end of the log
#
# If the Mac is asleep at midnight, launchd runs the job when it wakes. If it's shut
# down, that week is skipped. Postgres.app has to be running for the tables to update.
set -eu

label="com.mmasite.scraper"
plist="$HOME/Library/LaunchAgents/$label.plist"
repo="${0:A:h:h}"
target="gui/$(id -u)"

case "${1:-}" in
  install)
    mkdir -p "$repo/src/Scraper/logs"
    cat >"$plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<!-- Written by $repo/scripts/schedule-scrape.sh -->
<plist version="1.0">
<dict>
    <key>Label</key>
    <string>$label</string>
    <key>ProgramArguments</key>
    <array>
        <string>$repo/scripts/scrape.sh</string>
    </array>
    <key>EnvironmentVariables</key>
    <dict>
        <key>DOTNET_CLI_TELEMETRY_OPTOUT</key>
        <string>1</string>
        <key>DOTNET_NOLOGO</key>
        <string>1</string>
    </dict>
    <!-- Weekday 1 is Monday. -->
    <key>StartCalendarInterval</key>
    <dict>
        <key>Weekday</key>
        <integer>1</integer>
        <key>Hour</key>
        <integer>0</integer>
        <key>Minute</key>
        <integer>0</integer>
    </dict>
    <key>StandardErrorPath</key>
    <string>$repo/src/Scraper/logs/launchd.log</string>
</dict>
</plist>
EOF
    launchctl bootout "$target/$label" 2>/dev/null || true
    launchctl bootstrap "$target" "$plist"
    echo "Scheduled: every Monday at 12:00 AM. Log: src/Scraper/logs/scraper.log"
    ;;
  uninstall)
    launchctl bootout "$target/$label" 2>/dev/null || true
    rm -f "$plist"
    echo "Removed the weekly scrape."
    ;;
  run)
    launchctl kickstart "$target/$label"
    echo "Started. Follow it with: tail -f src/Scraper/logs/scraper.log"
    ;;
  status)
    launchctl print "$target/$label" | grep -E "state =|last exit code" || true
    tail -n 15 "$repo/src/Scraper/logs/scraper.log" 2>/dev/null || echo "No runs logged yet."
    ;;
  *)
    sed -n '2,9p' "$0" | sed 's/^# \{0,1\}//'
    exit 1
    ;;
esac
