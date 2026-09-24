#!/usr/bin/env python
"""Check the committed contract copy against the bytes it was pinned from.

Agreement with the live upstream file is a cross-repository property, so it is
not a test in this repository and this tool never reaches for a sibling working
directory: with no arguments it checks the committed schema against the digest in
``alpha-artifact-v1.source.pinned.json``, which catches a local edit or a
line-ending change. To compare with upstream, an operator supplies the blob:

    git -C ../nordstar-engine show origin/develop:contracts/alpha-artifact-v1.schema.json > /tmp/upstream.json
    python Orion/tools/verify_contract_pin.py --source /tmp/upstream.json

and, once the change is reviewed on both sides, copies it in and re-pins with
``--repin --source <file>``, which rewrites the digest but never the commit and
blob ids: those are stated by whoever refreshes the pin, because only they know
which upstream revision they took.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import sys
from pathlib import Path

ORION_ROOT = Path(__file__).resolve().parents[1]
SCHEMA_PATH = ORION_ROOT / "contracts" / "alpha-artifact-v1.schema.json"
PIN_PATH = ORION_ROOT / "contracts" / "alpha-artifact-v1.source.pinned.json"


def digest(data: bytes) -> str:
    """sha256 after collapsing CRLF, so a checkout style cannot fail the pin."""
    return hashlib.sha256(data.replace(b"\r\n", b"\n")).hexdigest()


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, help="an upstream blob to compare with")
    parser.add_argument("--repin", action="store_true", help="rewrite the digest from --source")
    arguments = parser.parse_args()

    pin = json.loads(PIN_PATH.read_text(encoding="utf-8"))
    local = digest(SCHEMA_PATH.read_bytes())
    recorded = pin["digest"]["value"]

    if arguments.repin:
        if arguments.source is None:
            print("--repin needs --source, the bytes being pinned")
            return 2
        new = digest(arguments.source.read_bytes())
        pin["digest"]["value"] = new
        PIN_PATH.write_text(json.dumps(pin, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
        print(f"pinned digest rewritten to {new}; state the upstream commit and blob by hand")
        return 0

    failed = False
    if local != recorded:
        print(f"REFUSED: the committed schema hashes to {local}, the pin records {recorded}")
        failed = True
    else:
        print(f"OK the committed schema matches its pin ({recorded[:12]}...)")

    if arguments.source is not None:
        upstream = digest(arguments.source.read_bytes())
        if upstream != recorded:
            print(
                f"REFUSED: {arguments.source} hashes to {upstream}, which is not the pinned "
                f"{recorded}. The contract moved upstream; review both sides before copying it in."
            )
            failed = True
        else:
            print(f"OK {arguments.source} is the pinned revision")

    print(
        "pinned from {repository} {path} at commit {commit} (blob {blob})".format(
            repository=pin["upstream"]["repository"],
            path=pin["upstream"]["path"],
            commit=pin["upstream"]["commit"][:12],
            blob=pin["upstream"]["blob"][:12],
        )
    )
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
