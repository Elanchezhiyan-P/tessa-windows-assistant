"""Prints the saved chat transcript from wincompanion.db as a JSON array (Role, Text, Time), for the PowerShell test scripts."""
import json
import os
import sqlite3
import sys

db = os.path.join(os.environ["APPDATA"], "WinCompanion", "wincompanion.db")
if not os.path.exists(db):
    print("[]")
    sys.exit(0)

connection = sqlite3.connect(f"file:{db}?mode=ro", uri=True, timeout=5)
rows = connection.execute("SELECT role, text, time FROM messages ORDER BY id").fetchall()
print(json.dumps([{"Role": r, "Text": t, "Time": ts} for r, t, ts in rows]))
