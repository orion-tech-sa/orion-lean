/*
 * QUANT-36 - what a LEAN algorithm declares and records about its own run.
 *
 * LEAN computes its headline statistics after the algorithm has finished, in
 * the result handler, so no single object inside the engine holds everything an
 * alpha_artifact needs. The emitter therefore reads two LEAN-produced files:
 * the engine's own result document, and this one, which the algorithm writes at
 * the end of its run. Everything here is either authored by the researcher
 * (identity, copy, classification) or recorded by the algorithm from its own
 * portfolio (the daily series, the widest bar move it saw).
 *
 * The algorithm writes plain JSON rather than referencing this assembly, so
 * Algorithm.CSharp keeps the project references it has upstream. The emitter
 * refuses evidence that is missing or malformed, which is what keeps the two
 * sides honest.
 */

using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Orion.AlphaArtifact
{
    public sealed class EquityObservation
    {
        [JsonProperty("date")]
        public string Date { get; set; }

        [JsonProperty("value")]
        public decimal Value { get; set; }
    }

    public sealed class RunEvidence
    {
        public const string ExpectedVersion = "orion-lean-run-evidence-v1";

        [JsonProperty("evidence_version")]
        public string EvidenceVersion { get; set; }

        [JsonProperty("alpha_id")]
        public string AlphaId { get; set; }

        [JsonProperty("version")]
        public string Version { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("localized_name")]
        public LocalizedText LocalizedName { get; set; }

        [JsonProperty("localized_description")]
        public LocalizedText LocalizedDescription { get; set; }

        [JsonProperty("author")]
        public string Author { get; set; }

        [JsonProperty("classification")]
        public Classification Classification { get; set; }

        /// <summary>The id the benchmark series is published under, not a label.</summary>
        [JsonProperty("benchmark_id")]
        public string BenchmarkId { get; set; }

        /// <summary>
        /// Annualised percent, or null when the run's model does not report
        /// one. Null travels with a data-quality flag; it is never a zero.
        /// </summary>
        [JsonProperty("risk_free_rate_percent")]
        public decimal? RiskFreeRatePercent { get; set; }

        [JsonProperty("min_backtest_years")]
        public int? MinBacktestYears { get; set; }

        [JsonProperty("run_at")]
        public string RunAt { get; set; }

        [JsonProperty("run_by")]
        public string RunBy { get; set; }

        [JsonProperty("continuous_adjust")]
        public bool? ContinuousAdjust { get; set; }

        [JsonProperty("max_bar_move_percent")]
        public decimal? MaxBarMovePercent { get; set; }

        /// <summary>Data files the run actually read, for the snapshot digest.</summary>
        [JsonProperty("data_files")]
        public List<string> DataFiles { get; set; } = new List<string>();

        /// <summary>
        /// End-of-session portfolio value on every session the algorithm saw.
        /// The artifact's daily fractional series is derived from this, not
        /// from a display chart, which is sampled for drawing.
        /// </summary>
        [JsonProperty("daily_equity")]
        public List<EquityObservation> DailyEquity { get; set; } = new List<EquityObservation>();

        /// <summary>Engine-specific detail that stays opaque to the store.</summary>
        [JsonProperty("manifest")]
        public JObject Manifest { get; set; }
    }
}
