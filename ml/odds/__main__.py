"""python -m ml.odds <command>, from the top of the repo.

    fetch       download BestFightOdds UFC event pages since late 2023 (once each) to ml/odds/raw/
    build       match them to UFC.com fights; writes ml/odds/out/closing_lines.csv
    benchmark   compare v1, v2 and the closing line on the test fights; writes ml/odds/out/benchmark.md
    upcoming    current lines on scheduled fights; writes src/Web/Data/market_lines.json for the site
"""

import argparse

from . import benchmark, build, fetch, upcoming


def main():
    parser = argparse.ArgumentParser(prog="python -m ml.odds", description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("command", choices=["fetch", "build", "benchmark", "upcoming"])
    args = parser.parse_args()
    {"fetch": fetch.fetch, "build": build.build, "benchmark": benchmark.benchmark,
     "upcoming": upcoming.upcoming}[args.command]()


if __name__ == "__main__":
    main()
