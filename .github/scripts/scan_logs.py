#!/usr/bin/env python3
"""Judges a Docker Smoke run from the files collect-logs.sh wrote.

The run fails when:
  - an application container logged an ERR or FTL entry that no allowlist pattern explains;
  - any eshop-* container restarted, or is not running (docker-ci Stage 3 measured 0 restarts on a cold start, so a
    restart is a finding, not noise: a crash before its dependencies were ready, or a retry loop giving up).

Application logs are Serilog console output: an entry starts with "[HH:mm:ss LVL] SourceContext" and continues on the
following lines (message, SQL, stack trace). Each ERR/FTL entry is folded into one line, "LVL <text of every line>",
and matched against the allowlist, one regular expression per non-comment line. Every pattern is preceded by a comment
saying why the entry is expected and which finding covers it.

Usage: python3 .github/scripts/scan_logs.py <log dir> <allowlist file>
"""

import re
import sys
from pathlib import Path

APP_CONTAINER = re.compile(r"^eshop-(api-gateway|.+-api(-\d+)?)$")
ENTRY_START = re.compile(r"^\[\d{2}:\d{2}:\d{2} (VRB|DBG|INF|WRN|ERR|FTL)\] ?(.*)$")


def load_allowlist(path):
    patterns = []
    for number, raw in enumerate(Path(path).read_text(encoding="utf-8").splitlines(), 1):
        line = raw.strip()
        if line and not line.startswith("#"):
            patterns.append((number, re.compile(line)))
    return patterns


def entries(log_text):
    """Yield (level, folded text) for every Serilog entry in a container log."""
    level, lines = None, []
    for raw in log_text.splitlines():
        match = ENTRY_START.match(raw)
        if match:
            if level:
                yield level, " ".join(lines)
            level, lines = match.group(1), [match.group(2).strip()]
        elif level:
            lines.append(raw.strip())
    if level:
        yield level, " ".join(lines)


def main(log_dir, allowlist_path):
    log_dir = Path(log_dir)
    allowlist = load_allowlist(allowlist_path)
    used = set()
    problems = []

    table = log_dir / "containers.tsv"
    rows = [line.split("\t") for line in table.read_text(encoding="utf-8").splitlines()[1:] if "\t" in line]
    if not rows:
        problems.append("no containers were collected")
    for name, status, health, restarts, exit_code in rows:
        if status != "running":
            problems.append(f"{name} is {status} (exit code {exit_code})")
        if restarts != "0":
            problems.append(f"{name} restarted {restarts} time(s)")
        if health == "unhealthy":
            problems.append(f"{name} is unhealthy")

    scanned = 0
    for name, *_ in rows:
        if not APP_CONTAINER.match(name):
            continue
        log_file = log_dir / f"{name}.log"
        if not log_file.exists():
            problems.append(f"{name}: no log collected")
            continue
        scanned += 1
        for level, text in entries(log_file.read_text(encoding="utf-8", errors="replace")):
            if level not in ("ERR", "FTL"):
                continue
            folded = f"{level} {text}"
            hit = next((number for number, pattern in allowlist if pattern.search(folded)), None)
            if hit is None:
                problems.append(f"{name}: unexpected {folded[:600]}")
            else:
                used.add(hit)

    print(f"scanned {scanned} application logs, {len(rows)} containers")
    for number, pattern in allowlist:
        state = "matched" if number in used else "unused "
        print(f"  allowlist line {number} {state}: {pattern.pattern[:100]}")
    if problems:
        print(f"\n{len(problems)} problem(s):")
        for problem in problems:
            print(f"  - {problem}")
        return 1
    print("no unexpected errors, no restarts")
    return 0


if __name__ == "__main__":
    if len(sys.argv) != 3:
        sys.exit(__doc__)
    sys.exit(main(sys.argv[1], sys.argv[2]))
