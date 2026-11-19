"""Export the parity dataset from the previous tool (business rules BR-SCO-70).

Reads every job from the previous tool's SQLite database, runs the previous
tool's own scoring engine on it (so the expected values match its final code,
not whatever was stored when the job was collected) and writes one JSON line
per job.

The output holds third-party job text and may hold personal data. Write it
outside this repository. Usage:

    python3 -I tools/parity/export_previous_tool.py \
        --backend PATH_TO_PREVIOUS_TOOL/backend \
        --database PATH_TO/job-watcher.sqlite3 \
        --output ~/.rolehound/parity/jobs.jsonl

Then run the tests with ROLEHOUND_PARITY_FIXTURE pointing at the output file.
"""

from __future__ import annotations

import argparse
import json
import sqlite3
import sys
from pathlib import Path

REPOSITORY_ROOT = Path(__file__).resolve().parents[2]


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--backend", required=True, type=Path, help="previous tool backend folder")
    parser.add_argument("--database", required=True, type=Path, help="previous tool SQLite file")
    parser.add_argument("--output", required=True, type=Path, help="JSON lines file to write")
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    output = args.output.expanduser().resolve()
    if output.is_relative_to(REPOSITORY_ROOT):
        print("Refusing to write inside the repository: the data is not public.", file=sys.stderr)
        return 1

    sys.path.insert(0, str(args.backend.expanduser().resolve()))
    from watcher.scoring import PROFILE, classify  # noqa: PLC0415  (path set at runtime)

    stack_groups = {"core", "adjacent"}
    min_score = 25

    # Read-only: the previous tool's data must not change.
    uri = f"file:{args.database.expanduser().resolve()}?mode=ro"
    connection = sqlite3.connect(uri, uri=True)
    rows = connection.execute(
        "SELECT id, title, description, company_name, location FROM jobs ORDER BY id"
    )

    output.parent.mkdir(parents=True, exist_ok=True)
    count = 0
    with output.open("w", encoding="utf-8") as file:
        for job_id, title, description, company, location in rows:
            result = classify(title or "", description or "", company or "", location or "", PROFILE)
            highlighted = result.score >= min_score and bool(stack_groups & set(result.groups))
            record = {
                "id": str(job_id),
                "title": title or "",
                "description": description or "",
                "company": company or "",
                "location": location or "",
                "score": result.score,
                "tags": result.tags,
                "groups": result.groups,
                "seniority": result.seniority,
                "isHighlighted": highlighted,
            }
            file.write(json.dumps(record, ensure_ascii=False) + "\n")
            count += 1

    connection.close()
    print(f"Exported {count} jobs to {output}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
