"""Publish the committed code and the data snapshot to a Hugging Face Space.

    pip install huggingface_hub
    hf auth login                                   # once, with a write token
    python deploy/push_to_space.py <user>/<space>   # e.g. rmill/mma-hub

Uploads the files in the last commit (uncommitted changes are left out) plus
data/mma.db, which isn't in git, with deploy/huggingface/README.md as the Space's
README, which holds its settings. Creates the Space the first time. Files in the
Space that aren't in the upload are deleted, so it always matches the code.
"""

import shutil
import subprocess
import sys
import tarfile
import tempfile
from io import BytesIO
from pathlib import Path

from huggingface_hub import create_repo, upload_folder


def main():
    if len(sys.argv) != 2 or "/" not in sys.argv[1]:
        sys.exit(__doc__)
    space = sys.argv[1]
    root = Path(subprocess.check_output(["git", "rev-parse", "--show-toplevel"], text=True).strip())
    commit = subprocess.check_output(["git", "-C", root, "rev-parse", "--short", "HEAD"], text=True).strip()
    snapshot = root / "data" / "mma.db"
    if not snapshot.exists():
        sys.exit("No data/mma.db. Make it first: cd src/Web && dotnet run -- export-sqlite ../../data/mma.db")

    with tempfile.TemporaryDirectory() as folder:
        archive = subprocess.check_output(["git", "-C", root, "archive", "HEAD"])
        with tarfile.open(fileobj=BytesIO(archive)) as tar:
            tar.extractall(folder, filter="data")
        (Path(folder) / "data").mkdir(exist_ok=True)
        shutil.copy(snapshot, Path(folder) / "data" / "mma.db")
        shutil.copy(root / "deploy" / "huggingface" / "README.md", Path(folder) / "README.md")

        create_repo(space, repo_type="space", space_sdk="docker", exist_ok=True)
        upload_folder(repo_id=space, repo_type="space", folder_path=folder,
                      delete_patterns="*", commit_message=f"Deploy {commit}")
    print(f"Pushed {commit}. The Space is rebuilding: https://huggingface.co/spaces/{space}")


if __name__ == "__main__":
    main()
