# Orion in this engine — the `alpha_artifact` contract

This directory is the whole of Orion's addition to the LEAN fork: the pinned
`alpha_artifact` 1.0 contract, a C# binding for it, an emitter that turns a
finished LEAN backtest into one artifact, and the commands that hold an emitted
artifact to the contract. The algorithm that produces one lives beside the other
algorithms, at `Algorithm.CSharp/OrionUsMomentumAlphaArtifactAlgorithm.cs`.

QUANT-36 / ADR-0003. `alpha_artifact` is the seam between the two engines
(nordstar for Tadawul, LEAN for the US) and the product layer. ADR-0003 set a
gate before that architecture was committed to: **prove LEAN emits the artifact
faithfully with a one-alpha spike.** This is that gate, run.

## What the contract is, and who owns it

`contracts/alpha-artifact-v1.schema.json` is a **byte-pinned copy** of the
normative document, which lives in `nordstar-engine/contracts/`. Its provenance —
repository, path, commit, blob and a line-ending-normalised content digest — is
in `contracts/alpha-artifact-v1.source.pinned.json`.

The ticket asked for the schema to be owned "somewhere neither engine owns". It
is published from nordstar-engine and consumed here by pinned bytes, and that is
deliberate rather than pending:

- A new repository would move the file without adding a guarantee. What makes a
  consumer safe is that it states which bytes it was built against and fails when
  they change — which the pin does, in a check that needs no network and no
  sibling working directory.
- The schema's identity is already independent of where the file sits: its `$id`
  is `https://schemas.orion.sa/alpha-artifact/v1.0/schema.json`. Publishing it at
  that URL is a hosting change, not a contract change, and the pin stays the
  mechanism either way.
- The same pattern is already load-bearing in the estate: nordstar pins
  market-data's security reference and the client's positioning lint list the
  same way, for the same reason — a cross-repository property cannot be a test in
  one repository.

A semantic change therefore means independently reviewed changes in
nordstar-engine, in the store's Java binding, and here; a breaking change means a
new artifact version and a new schema file.

## The two-step emit

LEAN computes its headline statistics after the algorithm has stopped, in the
result handler, so no object inside the engine holds everything the artifact
needs. The emit is therefore in two steps, both from the engine's own output:

1. The algorithm writes **run evidence** about itself —
   `<algorithm-id>-orion-evidence.json` beside LEAN's result document: identity,
   authored copy in both locales, classification, the benchmark id, the
   risk-free series it was measured against, and its end-of-session portfolio
   value for every session it saw.
2. `Orion.AlphaArtifact` reads that plus LEAN's **result document** and emits one
   `alpha_artifact` 1.0.

The daily series is taken from the algorithm's own record, not from the display
chart: the chart exists to be drawn. It is recorded on the session's own bar
rather than in `OnEndOfDay`, because with daily data the end-of-day event fires
before that session's bar is applied — a value recorded there carries the
previous session's marks under this session's date, and every observation would
be labelled one session late. That defect was found by the reconciliation check
below and fixed before the artifact was accepted.

Nothing is invented. A statistic LEAN did not report is `null` beside a
data-quality flag that says so, a window shorter than the alpha declares is
refused, and a data snapshot that names a file nobody can hash is reported
unavailable rather than hashed from the file name.

## Running it

```
# 1. build
dotnet build Launcher/QuantConnect.Lean.Launcher.csproj -c Release
dotnet build Orion/AlphaArtifact/QuantConnect.Orion.AlphaArtifact.csproj -c Release

# 2. run the spike algorithm (LEAN's own sample US data, 2017-01-03..2021-03-31)
cd Launcher/bin/Release
dotnet QuantConnect.Lean.Launcher.dll \
  --algorithm-type-name OrionUsMomentumAlphaArtifactAlgorithm \
  --results-destination-folder <out> --close-automatically true

# 3. emit the artifact
dotnet Orion/AlphaArtifact/bin/Release/Orion.AlphaArtifact.dll \
  --result   <out>/OrionUsMomentumAlphaArtifactAlgorithm.json \
  --evidence <out>/OrionUsMomentumAlphaArtifactAlgorithm-orion-evidence.json \
  --data     Data \
  --out      Orion/evidence/lean_us_index_momentum-artifact.json

# 4. hold it to the contract — three layers, and the mutation proofs
python Orion/tools/validate_alpha_artifact.py \
  --artifact Orion/evidence/lean_us_index_momentum-artifact.json \
  --result   <out>/OrionUsMomentumAlphaArtifactAlgorithm.json --self-test

# 5. hold the C# binding to it too, separately
dotnet Orion/AlphaArtifact/bin/Release/Orion.AlphaArtifact.dll \
  --verify Orion/evidence/lean_us_index_momentum-artifact.json --self-test

# 6. and the pin itself
python Orion/tools/verify_contract_pin.py
```

## How an emitted artifact is checked

Three layers, checked separately, so a pass says which promise was kept and a
drifting layer fails a command instead of surfacing in a client-facing screener.

| Layer | What it holds | Proved by |
| --- | --- | --- |
| Pinned normative schema | the document nordstar and the store validate against | `validate_alpha_artifact.py`, 9 mutations |
| Engine gate | what a pattern cannot say: each locale predominantly its own script, unique ascending sessions, an offset on `run_at`, no store-owned field | `validate_alpha_artifact.py`, 4 mutations |
| C# binding | the same rules where a LEAN run actually passes through them | `--verify --self-test`, 19 mutations |
| Reconciliation to the run | the series compounds back to the engine's own net profit, and each observation equals the engine's own daily performance sample for that session | `validate_alpha_artifact.py --result`, 2 mutations |

The Arabic rule is the one worth spelling out. The schema requires both locales
and at least one letter of each locale's own script; the gate additionally
requires each locale's own script to **outnumber** the other, so English copy
with one Arabic letter appended is refused while a legitimately mixed Arabic
string carrying a Latin ticker is not. Nothing here translates, transliterates or
copies one locale into the other: an artifact whose Arabic copy does not exist is
refused before submission rather than published in English.

## The gate result (2026-09-24)

`lean_us_index_momentum` 1.0.0, emitted from a real run of LEAN over its own
sample US data, 1068 sessions from 2017-01-03 to 2021-03-31, is accepted by all
three layers, and its daily series compounds to the engine's own net profit to
within 2.3e-9 relative. The artifact is committed at
`Orion/evidence/lean_us_index_momentum-artifact.json`, and the same bytes are
carried in nordstar-engine as the gate fixture its store binding ingests through
the same code path a nordstar artifact takes.

The metrics are LEAN's own and are reported as research evidence, not as a
claim about the future: 17.12% compounding annual return, Sharpe 0.64,
probabilistic Sharpe 22.95%, maximum drawdown -30.7% against a benchmark that
returned 91.8% over the same window — the strategy underperformed simply holding
the benchmark, which is what the artifact says.

## What is deliberately not here

- **Nothing that reaches a market.** The artifact is research evidence for a
  decision-support catalogue; this work adds no venue, account or credential
  path of any kind.
- **No shared vocabulary for how a strategy fills.** ADR-0005 rules out one
  spanning Tadawul tick eras and LEAN's fill models, and it would be wrong for
  both engines; the runtime spec stays inside the opaque `manifest`, which the
  store round-trips and does not interpret.
- **No re-emission of the four transcribed US alphas.** They carry no local
  return series and no engine run; giving them a conforming artifact needs a
  real LEAN run each, not a re-ingest.
