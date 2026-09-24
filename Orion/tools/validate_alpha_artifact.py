#!/usr/bin/env python
"""Hold a LEAN-emitted artifact to the normative alpha_artifact 1.0 contract.

Three layers, checked separately, so a pass says which promise was kept:

1. The pinned normative JSON Schema (``Orion/contracts/alpha-artifact-v1.schema.json``),
   the document nordstar-engine and the store validate against.
2. The engine gate, which states what a regular expression cannot: each locale's
   copy must be predominantly its own script, dates in the return series must be
   unique, ``run_at`` must carry an offset, and a store-owned field must not
   appear at the top level.
3. Reconciliation against the engine run that produced it: the daily fractional
   series must compound back to LEAN's own net profit, and each observation must
   equal LEAN's own daily performance sample for that session.

``--self-test`` mutates the artifact one field at a time and requires each
mutation to be refused, naming the layer that refused it. A binding that drifts
from the schema then fails a command rather than being discovered in a
client-facing screener.

No network, and no path into a sibling repository: the schema is the committed
pinned copy, whose bytes ``verify_contract_pin.py`` checks against the digest
recorded when it was pinned.
"""
from __future__ import annotations

import argparse
import datetime as dt
import json
import re
import sys
from pathlib import Path

from jsonschema import Draft202012Validator, FormatChecker

ORION_ROOT = Path(__file__).resolve().parents[1]
SCHEMA_PATH = ORION_ROOT / "contracts" / "alpha-artifact-v1.schema.json"

ARABIC_LETTER = re.compile(r"[ء-ي]")
LATIN_LETTER = re.compile(r"[A-Za-z]")
FORBIDDEN_TOP_LEVEL = ("status", "generated_at", "engine_alpha_id", "engine_alpha_version")

# LEAN samples the strategy's daily performance at midnight, so the sample
# labelled D reports the session that ended on D-1. The final sample is taken
# when the algorithm stops rather than at the following midnight, so it covers a
# shorter span than a session and is excluded from the per-session comparison.
SESSION_ALIGNMENT_DAYS = 1
SESSION_TOLERANCE = 1e-6
RECONCILIATION_TOLERANCE = 1e-6


def load(path: Path) -> dict:
    return json.loads(path.read_text(encoding="utf-8"))


def schema_errors(artifact: dict) -> list[str]:
    """Layer 1 - the normative document, formats included."""
    schema = load(SCHEMA_PATH)
    Draft202012Validator.check_schema(schema)
    validator = Draft202012Validator(schema, format_checker=FormatChecker())
    return [
        "/".join(str(part) for part in error.absolute_path) + ": " + error.message
        for error in sorted(validator.iter_errors(artifact), key=str)
    ]


def gate_errors(artifact: dict) -> list[str]:
    """Layer 2 - the rules the schema states as a floor or cannot state at all."""
    errors: list[str] = []

    for field in FORBIDDEN_TOP_LEVEL:
        if field in artifact:
            errors.append(f"{field} is store-owned or obsolete and must not be emitted")

    for field in ("localized_name", "localized_description"):
        text = artifact.get(field)
        if not isinstance(text, dict):
            errors.append(f"{field} must carry both locales")
            continue
        arabic, english = text.get("ar"), text.get("en")
        if not isinstance(arabic, str) or not arabic.strip():
            errors.append(f"{field}.ar must be authored")
            continue
        if not isinstance(english, str) or not english.strip():
            errors.append(f"{field}.en must be authored")
            continue
        if arabic == english:
            errors.append(f"{field}.ar and {field}.en must not be the same string")
        if len(ARABIC_LETTER.findall(arabic)) <= len(LATIN_LETTER.findall(arabic)):
            errors.append(f"{field}.ar must be predominantly Arabic script")
        if len(LATIN_LETTER.findall(english)) <= len(ARABIC_LETTER.findall(english)):
            errors.append(f"{field}.en must be predominantly Latin script")

    provenance = artifact.get("provenance") or {}
    run_at = provenance.get("run_at")
    flags = artifact.get("data_quality_flags") or []
    if run_at is None:
        if not any("run_at" in str(flag) for flag in flags):
            errors.append("a null run_at requires a data-quality flag naming run_at")
    elif isinstance(run_at, str):
        try:
            parsed = dt.datetime.fromisoformat(run_at.replace("Z", "+00:00"))
        except ValueError:
            errors.append("run_at is not an ISO-8601 instant")
        else:
            if parsed.tzinfo is None:
                errors.append("run_at must carry an offset; a naive stamp names no instant")

    observations = ((artifact.get("return_series") or {}).get("observations")) or []
    dates = [observation.get("date") for observation in observations]
    if len(set(dates)) != len(dates):
        errors.append("the return series reports the same session more than once")
    if dates != sorted(dates):
        errors.append("the return series is not in ascending session order")

    if any(not str(flag).strip() for flag in flags):
        errors.append("data_quality_flags must not contain blanks")
    if len(set(map(str, flags))) != len(flags):
        errors.append("data_quality_flags must not repeat")

    return errors


def reconciliation_errors(artifact: dict, result: dict) -> list[str]:
    """Layer 3 - the artifact against the run that produced it."""
    errors: list[str] = []
    statistics = (result.get("totalPerformance") or {}).get("portfolioStatistics") or {}
    start_equity = float(statistics.get("startEquity", 0) or 0)
    end_equity = float(statistics.get("endEquity", 0) or 0)
    observations = ((artifact.get("return_series") or {}).get("observations")) or []
    if not observations:
        return ["the artifact carries no return series to reconcile"]
    if start_equity <= 0:
        return ["the run reports no starting equity, so nothing can be reconciled"]

    compounded = 1.0
    for observation in observations:
        compounded *= 1.0 + float(observation["value"])
    engine_multiple = end_equity / start_equity
    relative = abs(compounded - engine_multiple) / engine_multiple
    if relative > RECONCILIATION_TOLERANCE:
        errors.append(
            "the daily series compounds to "
            f"{compounded:.10f} and the engine's own equity to {engine_multiple:.10f} "
            f"(relative difference {relative:.3e})"
        )

    samples = (
        ((result.get("charts") or {}).get("Strategy Equity") or {})
        .get("series", {})
        .get("Return", {})
        .get("values")
    )
    if not samples:
        errors.append("the result document carries no daily performance samples to compare")
        return errors

    engine_by_day = {
        dt.datetime.fromtimestamp(point[0], dt.timezone.utc).date(): float(point[1]) / 100.0
        for point in samples
        if len(point) >= 2
    }
    final_sample_day = max(engine_by_day)
    mismatches = []
    compared = 0
    for observation in observations:
        session = dt.date.fromisoformat(observation["date"])
        sample_day = session + dt.timedelta(days=SESSION_ALIGNMENT_DAYS)
        if sample_day not in engine_by_day or sample_day == final_sample_day:
            continue
        compared += 1
        if abs(engine_by_day[sample_day] - float(observation["value"])) > SESSION_TOLERANCE:
            mismatches.append(
                f"{observation['date']}: artifact {observation['value']}, "
                f"engine {engine_by_day[sample_day]}"
            )
    if compared == 0:
        errors.append("no session could be compared with the engine's own samples")
    if mismatches:
        errors.append(
            f"{len(mismatches)} of {compared} sessions disagree with the engine's own "
            "daily performance: " + "; ".join(mismatches[:5])
        )
    return errors


def mutations(artifact: dict) -> list[tuple[str, dict, str]]:
    """One refusal per failure mode, each naming the layer that must refuse it."""
    cases: list[tuple[str, dict, str]] = []

    def mutate(label: str, layer: str, change) -> None:
        candidate = json.loads(json.dumps(artifact))
        change(candidate)
        cases.append((label, candidate, layer))

    mutate("artifact_version is not 1.0", "schema",
           lambda a: a.__setitem__("artifact_version", "1.1"))
    mutate("alpha_id is not an identifier", "schema",
           lambda a: a.__setitem__("alpha_id", "LEAN US Momentum"))
    mutate("name is blank", "schema", lambda a: a.__setitem__("name", "   "))
    mutate("a store-owned status is emitted", "schema",
           lambda a: a.__setitem__("status", "PUBLISHED"))
    mutate("a serialization-time generated_at is emitted", "schema",
           lambda a: a.__setitem__("generated_at", "2026-09-24T00:00:00Z"))
    mutate("the venue is not an enumerated value", "schema",
           lambda a: a["classification"].__setitem__("venue", "NASDAQ"))
    mutate("the return series changes unit", "schema",
           lambda a: a["return_series"].__setitem__("unit", "PERCENT"))
    mutate("the Arabic locale is missing", "schema",
           lambda a: a["localized_name"].pop("ar"))
    mutate("the metric basis renames its currency", "schema",
           lambda a: a["metric_basis"].__setitem__("currency", "dollars"))

    # The gate is stricter than the schema pattern on purpose: English copy with
    # one Arabic letter appended satisfies every non-empty check and still ships
    # an English page to an Arabic reader.
    mutate("English copy is relabelled as Arabic", "gate",
           lambda a: a["localized_description"].__setitem__(
               "ar", a["localized_description"]["en"] + " ن"))
    mutate("run_at loses its offset", "gate",
           lambda a: a["provenance"].__setitem__("run_at", "2026-09-24T19:09:17"))
    mutate("a session is reported twice", "gate",
           lambda a: a["return_series"]["observations"].append(
               {"date": a["return_series"]["observations"][0]["date"], "value": 0.0}))
    mutate("a null run_at carries no flag", "gate",
           lambda a: (a["provenance"].__setitem__("run_at", None),
                      a.__setitem__("data_quality_flags", [])))

    mutate("one session's return is overstated", "reconciliation",
           lambda a: a["return_series"]["observations"][10].__setitem__("value", 0.25))
    mutate("the series is truncated", "reconciliation",
           lambda a: a["return_series"].__setitem__(
               "observations", a["return_series"]["observations"][:-40]))
    return cases


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--artifact", required=True, type=Path)
    parser.add_argument(
        "--result",
        type=Path,
        help="the LEAN result document the artifact was emitted from; enables reconciliation",
    )
    parser.add_argument("--self-test", action="store_true", help="prove each layer refuses its mutations")
    arguments = parser.parse_args()

    artifact = load(arguments.artifact)
    result = load(arguments.result) if arguments.result else None

    layers = {"schema": schema_errors(artifact), "gate": gate_errors(artifact)}
    if result is not None:
        layers["reconciliation"] = reconciliation_errors(artifact, result)

    failed = False
    for layer, errors in layers.items():
        if errors:
            failed = True
            print(f"REFUSED by {layer}:")
            for error in errors:
                print(f"  - {error}")
        else:
            print(f"OK {layer}")

    if failed:
        return 1

    observations = artifact["return_series"]["observations"]
    print(
        f"alpha_artifact 1.0 accepted: {artifact['alpha_id']} {artifact['version']} "
        f"from {artifact['provenance']['source_engine']}, "
        f"{len(observations)} sessions {artifact['backtest']['start']}..{artifact['backtest']['end']}"
    )

    if arguments.self_test:
        print("\nmutation proofs")
        for label, candidate, layer in mutations(artifact):
            checks = {"schema": schema_errors(candidate), "gate": gate_errors(candidate)}
            if result is not None:
                checks["reconciliation"] = reconciliation_errors(candidate, result)
            refused_by = [name for name, errors in checks.items() if errors]
            if layer not in refused_by:
                print(f"  NOT REFUSED by {layer}: {label} (refused by {refused_by or 'nothing'})")
                failed = True
            else:
                print(f"  refused by {layer}: {label}")

    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
