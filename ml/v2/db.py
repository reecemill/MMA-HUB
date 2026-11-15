"""The connection to the scraper's Postgres database.

Uses MMA_DB if it's set (any libpq connection string), otherwise the connection string
the scraper itself uses, from its .NET user-secrets file.
"""

import json
import os
from pathlib import Path

import psycopg
from psycopg.conninfo import make_conninfo

# src/Scraper/Scraper.csproj's UserSecretsId.
USER_SECRETS = Path.home() / ".microsoft" / "usersecrets" / "738bc26b-62d7-42fe-9bb4-41d44c0967ba" / "secrets.json"

# .NET connection string keys -> libpq keys.
_KEYS = {"host": "host", "server": "host", "port": "port", "database": "dbname",
         "username": "user", "user id": "user", "password": "password"}


def connection_string():
    if os.environ.get("MMA_DB"):
        return os.environ["MMA_DB"]
    # The file starts with a byte-order mark, hence utf-8-sig.
    secrets = json.loads(USER_SECRETS.read_text(encoding="utf-8-sig"))
    parts = {}
    for item in secrets["ConnectionStrings:MmaDb"].split(";"):
        key, _, value = item.partition("=")
        if key.strip().lower() in _KEYS:
            parts[_KEYS[key.strip().lower()]] = value.strip()
    return make_conninfo(**parts)


def connect():
    return psycopg.connect(connection_string())
