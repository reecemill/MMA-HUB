"""python -m ml.v2 <command>, from the top of the repo.

    features   build the features and print a summary
    winner     choose, train and test the winner models; writes ml/models/<version>/
"""

import argparse

from . import pipeline


def main():
    parser = argparse.ArgumentParser(prog="python -m ml.v2", description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("command", choices=["features", "winner"])
    args = parser.parse_args()
    {"features": pipeline.features_summary, "winner": pipeline.winner}[args.command]()


if __name__ == "__main__":
    main()
