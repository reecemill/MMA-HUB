"""Download per-fight UFC stats from the public ufcstats.com export on GitHub.

    python ml/download_ufcstats.py [commit]     # default: the pinned commit below

github.com/Greco1899/scrape_ufc_stats publishes ufcstats.com's round-by-round stats
for every UFC bout as CSV files, updated daily. The files are saved to
data/ufcstats/, which isn't in git; train.py reads them from there. Pinning a
commit keeps training runs repeatable; pass a newer one to update.
"""

import sys
from pathlib import Path
from urllib.request import urlopen

ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / "data" / "ufcstats"
REPO = "Greco1899/scrape_ufc_stats"
COMMIT = "b93901a2ab58d7c9880797451c7ef0ef5e03ce1a"  # 2026-09-27
FILES = ["ufc_event_details.csv", "ufc_fight_results.csv", "ufc_fight_stats.csv",
         "ufc_fighter_details.csv", "ufc_fighter_tott.csv"]


def main():
    commit = sys.argv[1] if len(sys.argv) > 1 else COMMIT
    OUT.mkdir(parents=True, exist_ok=True)
    for name in FILES:
        with urlopen(f"https://raw.githubusercontent.com/{REPO}/{commit}/{name}", timeout=120) as r:
            data = r.read()
        (OUT / name).write_bytes(data)
        print(f"saved {name} ({len(data) / 1e6:.1f} MB)")
    (OUT / "COMMIT").write_text(commit + "\n")


if __name__ == "__main__":
    main()
