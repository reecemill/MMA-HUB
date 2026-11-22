"""python -m ml.v2 <command>, from the top of the repo.

    features   build the features and print a summary
    winner     choose, train and test the winner models; writes ml/models/<version>/
    method-round   train and test the method and round models (needs `winner` first);
                   writes ml/models/<version>/method_round/
"""

import argparse

from . import method_round, pipeline


def main():
    parser = argparse.ArgumentParser(prog="python -m ml.v2", description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("command", choices=["features", "winner", "method-round"])
    args = parser.parse_args()
    {"features": pipeline.features_summary, "winner": pipeline.winner,
     "method-round": method_round.method_round}[args.command]()


if __name__ == "__main__":
    main()
