/*
 * QUANT-36 - the C# binding of the Orion alpha_artifact 1.0 contract.
 *
 * The normative document is Orion/contracts/alpha-artifact-v1.schema.json, a
 * byte-pinned copy of nordstar-engine contracts/alpha-artifact-v1.schema.json
 * (provenance in alpha-artifact-v1.source.pinned.json). This file restates that
 * schema as types and a validator so a LEAN run cannot emit an artifact the
 * contract refuses; the pinned schema, not this file, decides what is valid,
 * and Orion/tools/validate_alpha_artifact.py holds the emitted bytes to it.
 *
 * Restating rather than generating is deliberate and matches the store's Java
 * binding: the mutation proofs run against each layer separately, so a binding
 * that drifts from the schema fails a gate instead of being discovered in a
 * client-facing screener.
 */

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Orion.AlphaArtifact
{
    /// <summary>
    /// Raised when a candidate artifact does not satisfy the contract. Nothing
    /// is written when this is raised: a refused artifact is not a warning.
    /// </summary>
    public class AlphaArtifactContractException : Exception
    {
        public AlphaArtifactContractException(string message) : base(message)
        {
        }
    }

    /// <summary>Authored copy in both product locales.</summary>
    public sealed class LocalizedText
    {
        [JsonProperty("ar", Order = 1)]
        public string Ar { get; set; }

        [JsonProperty("en", Order = 2)]
        public string En { get; set; }
    }

    public sealed class Classification
    {
        [JsonProperty("object_type", Order = 1)]
        public string ObjectType { get; set; }

        [JsonProperty("venue", Order = 2)]
        public string Venue { get; set; }

        [JsonProperty("asset_class", Order = 3)]
        public string AssetClass { get; set; }

        [JsonProperty("family", Order = 4)]
        public string Family { get; set; }

        [JsonProperty("horizon", Order = 5)]
        public string Horizon { get; set; }

        [JsonProperty("direction", Order = 6)]
        public string Direction { get; set; }

        [JsonProperty("rebalance", Order = 7)]
        public string Rebalance { get; set; }
    }

    public sealed class Provenance
    {
        [JsonProperty("source_engine", Order = 1)]
        public string SourceEngine { get; set; }

        [JsonProperty("run_type", Order = 2)]
        public string RunType { get; set; }

        /// <summary>
        /// The immutable engine completion timestamp. Null is allowed and must
        /// carry a data-quality flag; the current time is never substituted.
        /// </summary>
        [JsonProperty("run_at", Order = 3)]
        public string RunAt { get; set; }

        [JsonProperty("run_by", Order = 4)]
        public string RunBy { get; set; }

        [JsonProperty("data_snapshot", Order = 5)]
        public string DataSnapshot { get; set; }

        [JsonProperty("report_source", Order = 6)]
        public string ReportSource { get; set; }
    }

    public sealed class MetricBasis
    {
        [JsonProperty("currency", Order = 1)]
        public string Currency { get; set; }

        [JsonProperty("benchmark", Order = 2)]
        public string Benchmark { get; set; }

        [JsonProperty("risk_free_rate", Order = 3)]
        public decimal? RiskFreeRate { get; set; }

        [JsonProperty("risk_free_rate_unit", Order = 4)]
        public string RiskFreeRateUnit { get; set; } = "PERCENT";

        [JsonProperty("return_metric_unit", Order = 5)]
        public string ReturnMetricUnit { get; set; } = "PERCENT";

        [JsonProperty("turnover_unit", Order = 6)]
        public string TurnoverUnit { get; set; } = "MULTIPLE";

        [JsonProperty("probability_unit", Order = 7)]
        public string ProbabilityUnit { get; set; } = "PERCENT";

        [JsonProperty("return_series_unit", Order = 8)]
        public string ReturnSeriesUnit { get; set; } = "FRACTION";
    }

    /// <summary>
    /// The six shared headline metrics. The schema allows more, so engine
    /// metrics with no shared meaning ride along in <see cref="Additional"/>
    /// rather than being squeezed into one of the six.
    /// </summary>
    public sealed class HeadlineMetrics
    {
        [JsonProperty("sharpe", Order = 1)]
        public decimal? Sharpe { get; set; }

        [JsonProperty("annual_return", Order = 2)]
        public decimal? AnnualReturn { get; set; }

        [JsonProperty("max_drawdown", Order = 3)]
        public decimal? MaxDrawdown { get; set; }

        [JsonProperty("turnover", Order = 4)]
        public decimal? Turnover { get; set; }

        [JsonProperty("psr", Order = 5)]
        public decimal? Psr { get; set; }

        [JsonProperty("benchmark_return", Order = 6)]
        public decimal? BenchmarkReturn { get; set; }

        [JsonExtensionData]
        public IDictionary<string, JToken> Additional { get; set; } = new Dictionary<string, JToken>();
    }

    public sealed class BacktestWindow
    {
        [JsonProperty("start", Order = 1)]
        public string Start { get; set; }

        [JsonProperty("end", Order = 2)]
        public string End { get; set; }

        [JsonProperty("min_years", Order = 3)]
        public int? MinYears { get; set; }
    }

    public sealed class ReturnObservation
    {
        [JsonProperty("date", Order = 1)]
        public string Date { get; set; }

        [JsonProperty("value", Order = 2)]
        public decimal Value { get; set; }
    }

    public sealed class ReturnSeries
    {
        [JsonProperty("kind", Order = 1)]
        public string Kind { get; set; } = "BACKTEST";

        [JsonProperty("frequency", Order = 2)]
        public string Frequency { get; set; } = "DAILY";

        [JsonProperty("unit", Order = 3)]
        public string Unit { get; set; } = "FRACTION";

        [JsonProperty("observations", Order = 4)]
        public List<ReturnObservation> Observations { get; set; } = new List<ReturnObservation>();
    }

    public sealed class DataQuality
    {
        [JsonProperty("continuous_adjust", Order = 1)]
        public bool? ContinuousAdjust { get; set; }

        [JsonProperty("max_bar_move_percent", Order = 2)]
        public decimal? MaxBarMovePercent { get; set; }
    }

    /// <summary>
    /// One alpha_artifact 1.0 document. Property order follows the schema so a
    /// diff between an artifact from this engine and one from nordstar reads
    /// field by field.
    /// </summary>
    public sealed class AlphaArtifactV1
    {
        [JsonProperty("artifact_type", Order = 1)]
        public string ArtifactType { get; set; } = "alpha_artifact";

        [JsonProperty("artifact_version", Order = 2)]
        public string ArtifactVersion { get; set; } = "1.0";

        [JsonProperty("alpha_id", Order = 3)]
        public string AlphaId { get; set; }

        [JsonProperty("version", Order = 4)]
        public string Version { get; set; }

        [JsonProperty("name", Order = 5)]
        public string Name { get; set; }

        [JsonProperty("description", Order = 6)]
        public string Description { get; set; }

        [JsonProperty("localized_name", Order = 7)]
        public LocalizedText LocalizedName { get; set; }

        [JsonProperty("localized_description", Order = 8)]
        public LocalizedText LocalizedDescription { get; set; }

        [JsonProperty("author", Order = 9)]
        public string Author { get; set; }

        [JsonProperty("classification", Order = 10)]
        public Classification Classification { get; set; }

        [JsonProperty("provenance", Order = 11)]
        public Provenance Provenance { get; set; }

        [JsonProperty("metric_basis", Order = 12)]
        public MetricBasis MetricBasis { get; set; }

        [JsonProperty("metrics", Order = 13)]
        public HeadlineMetrics Metrics { get; set; }

        [JsonProperty("backtest", Order = 14)]
        public BacktestWindow Backtest { get; set; }

        [JsonProperty("return_series", Order = 15)]
        public ReturnSeries ReturnSeries { get; set; }

        [JsonProperty("data_quality", Order = 16)]
        public DataQuality DataQuality { get; set; }

        [JsonProperty("manifest", Order = 17)]
        public JObject Manifest { get; set; }

        [JsonProperty("validation_report", Order = 18)]
        public JObject ValidationReport { get; set; }

        [JsonProperty("data_quality_flags", Order = 19)]
        public List<string> DataQualityFlags { get; set; } = new List<string>();

        public string ToJson()
        {
            return JsonConvert.SerializeObject(this, AlphaArtifactContract.SerializerSettings);
        }
    }

    /// <summary>
    /// The contract as rules. Every rule here is one the pinned schema states;
    /// the two are proved to agree by mutation, layer by layer, rather than by
    /// one of them being generated from the other.
    /// </summary>
    public static class AlphaArtifactContract
    {
        public const string ArtifactType = "alpha_artifact";
        public const string ArtifactVersion = "1.0";

        public static readonly JsonSerializerSettings SerializerSettings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Include,
            DateParseHandling = DateParseHandling.None,
            Culture = CultureInfo.InvariantCulture
        };

        // Store-owned or obsolete names. The schema sets each to false; here
        // they are simply never modelled, and the serialized document is
        // checked so an additive manifest cannot smuggle one in at top level.
        public static readonly string[] ForbiddenTopLevel =
        {
            "status", "generated_at", "engine_alpha_id", "engine_alpha_version"
        };

        public static readonly string[] ObjectTypes = { "ALPHA", "INDICATOR", "RESEARCH", "UNKNOWN" };
        public static readonly string[] Venues = { "TADAWUL", "US", "CRYPTO", "UNKNOWN" };
        public static readonly string[] AssetClasses = { "EQUITY", "ETF", "OPTION", "FUTURE", "CRYPTO", "UNKNOWN" };
        public static readonly string[] Families =
        {
            "MOMENTUM", "REVERSAL", "SEASONAL", "CARRY", "VOLATILITY", "INCOME", "BREAKOUT", "OTHER", "UNKNOWN"
        };
        public static readonly string[] Horizons = { "INTRADAY", "DAYS", "WEEKS", "MONTHS", "UNKNOWN" };
        public static readonly string[] Directions = { "LONG_ONLY", "LONG_SHORT", "MARKET_NEUTRAL", "UNKNOWN" };
        public static readonly string[] Rebalances = { "EACH_BAR", "DAILY_CLOSE", "WEEKLY", "MONTHLY", "UNKNOWN" };
        public static readonly string[] SourceEngines = { "NORDSTAR", "LEAN_QUANTCONNECT", "UNKNOWN" };
        public static readonly string[] RunTypes = { "ENGINE_RUN", "REPORT_INGESTED", "UNKNOWN" };

        private static readonly Regex AlphaIdPattern = new Regex("^[a-z][a-z0-9_]{2,63}$", RegexOptions.Compiled);
        private static readonly Regex VersionPattern = new Regex(@"^\d+\.\d+\.\d+$", RegexOptions.Compiled);
        private static readonly Regex CurrencyPattern = new Regex("^[A-Z]{3}$", RegexOptions.Compiled);
        private static readonly Regex DatePattern = new Regex(@"^\d{4}-\d{2}-\d{2}$", RegexOptions.Compiled);

        /// <summary>
        /// Arabic letters. Arabic-Indic digits are deliberately outside the
        /// range: a string of numerals is not Arabic copy.
        /// </summary>
        private static bool IsArabicLetter(char value) => value >= 'ء' && value <= 'ي';

        private static bool IsLatinLetter(char value) =>
            (value >= 'A' && value <= 'Z') || (value >= 'a' && value <= 'z');

        /// <summary>
        /// Validate one artifact, returning every reason it is refused. The
        /// emitter treats a non-empty list as fatal.
        /// </summary>
        public static IReadOnlyList<string> Validate(AlphaArtifactV1 artifact)
        {
            var errors = new List<string>();
            if (artifact == null)
            {
                return new[] { "artifact is null" };
            }

            if (artifact.ArtifactType != ArtifactType)
            {
                errors.Add($"artifact_type must be {ArtifactType}");
            }
            if (artifact.ArtifactVersion != ArtifactVersion)
            {
                errors.Add($"artifact_version must be {ArtifactVersion}");
            }
            if (artifact.AlphaId == null || !AlphaIdPattern.IsMatch(artifact.AlphaId))
            {
                errors.Add("alpha_id must match ^[a-z][a-z0-9_]{2,63}$");
            }
            if (artifact.Version == null || !VersionPattern.IsMatch(artifact.Version))
            {
                errors.Add("version must be three dot-separated integers");
            }
            if (string.IsNullOrWhiteSpace(artifact.Name))
            {
                errors.Add("name must carry a non-blank value");
            }

            ValidateLocalized(artifact.LocalizedName, "localized_name", errors);
            ValidateLocalized(artifact.LocalizedDescription, "localized_description", errors);
            ValidateClassification(artifact.Classification, errors);
            ValidateProvenance(artifact.Provenance, artifact.DataQualityFlags, errors);
            ValidateMetricBasis(artifact.MetricBasis, errors);
            ValidateBacktest(artifact.Backtest, errors);
            ValidateReturnSeries(artifact.ReturnSeries, errors);

            if (artifact.Metrics == null)
            {
                errors.Add("metrics is required");
            }
            if (artifact.DataQuality == null)
            {
                errors.Add("data_quality is required");
            }
            if (artifact.Manifest == null)
            {
                errors.Add("manifest is required");
            }
            if (artifact.DataQualityFlags == null)
            {
                errors.Add("data_quality_flags is required");
            }
            else
            {
                if (artifact.DataQualityFlags.Any(string.IsNullOrWhiteSpace))
                {
                    errors.Add("data_quality_flags must not contain blanks");
                }
                if (artifact.DataQualityFlags.Distinct().Count() != artifact.DataQualityFlags.Count)
                {
                    errors.Add("data_quality_flags must not repeat");
                }
            }

            return errors;
        }

        /// <summary>
        /// The same check applied to serialized bytes, which is where a
        /// store-owned field would actually arrive.
        /// </summary>
        public static IReadOnlyList<string> ValidateDocument(string json)
        {
            var errors = new List<string>();
            JObject document;
            try
            {
                document = JObject.Parse(json);
            }
            catch (JsonReaderException exception)
            {
                return new[] { "artifact is not JSON: " + exception.Message };
            }

            foreach (var forbidden in ForbiddenTopLevel)
            {
                if (document.Property(forbidden) != null)
                {
                    errors.Add($"{forbidden} is a store-owned or obsolete field and is forbidden");
                }
            }

            var artifact = JsonConvert.DeserializeObject<AlphaArtifactV1>(json, SerializerSettings);
            errors.AddRange(Validate(artifact));
            return errors;
        }

        private static void ValidateLocalized(LocalizedText text, string field, List<string> errors)
        {
            if (text == null)
            {
                errors.Add($"{field} is required and carries both locales");
                return;
            }
            if (string.IsNullOrWhiteSpace(text.Ar) || string.IsNullOrWhiteSpace(text.En))
            {
                errors.Add($"{field} requires non-blank ar and en");
                return;
            }
            if (string.Equals(text.Ar, text.En, StringComparison.Ordinal))
            {
                errors.Add($"{field}.ar and {field}.en must not be the same string");
            }

            var arabicInAr = text.Ar.Count(IsArabicLetter);
            var latinInAr = text.Ar.Count(IsLatinLetter);
            if (arabicInAr == 0)
            {
                errors.Add($"{field}.ar must be written in Arabic");
            }
            else if (arabicInAr <= latinInAr)
            {
                // The gate the schema pattern cannot state: English copy with a
                // stray Arabic letter passes every non-empty check and still
                // puts an English page in front of an Arabic reader.
                errors.Add($"{field}.ar must be predominantly Arabic script");
            }

            var latinInEn = text.En.Count(IsLatinLetter);
            var arabicInEn = text.En.Count(IsArabicLetter);
            if (latinInEn == 0)
            {
                errors.Add($"{field}.en must be written in Latin script");
            }
            else if (latinInEn <= arabicInEn)
            {
                errors.Add($"{field}.en must be predominantly Latin script");
            }
        }

        private static void ValidateClassification(Classification classification, List<string> errors)
        {
            if (classification == null)
            {
                errors.Add("classification is required");
                return;
            }
            Enumerated(classification.ObjectType, ObjectTypes, "classification.object_type", errors);
            Enumerated(classification.Venue, Venues, "classification.venue", errors);
            Enumerated(classification.AssetClass, AssetClasses, "classification.asset_class", errors);
            Enumerated(classification.Family, Families, "classification.family", errors);
            Enumerated(classification.Horizon, Horizons, "classification.horizon", errors);
            Enumerated(classification.Direction, Directions, "classification.direction", errors);
            Enumerated(classification.Rebalance, Rebalances, "classification.rebalance", errors);
        }

        private static void ValidateProvenance(Provenance provenance, List<string> flags, List<string> errors)
        {
            if (provenance == null)
            {
                errors.Add("provenance is required");
                return;
            }
            Enumerated(provenance.SourceEngine, SourceEngines, "provenance.source_engine", errors);
            Enumerated(provenance.RunType, RunTypes, "provenance.run_type", errors);

            if (provenance.RunAt == null)
            {
                if (flags == null || !flags.Any(flag => flag.Contains("run_at")))
                {
                    errors.Add("a null provenance.run_at requires a data-quality flag naming run_at");
                }
                return;
            }

            if (!DateTimeOffset.TryParse(
                    provenance.RunAt,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out _)
                || !(provenance.RunAt.EndsWith("Z", StringComparison.Ordinal)
                     || provenance.RunAt.Contains("+")
                     || provenance.RunAt.LastIndexOf('-') > 7))
            {
                errors.Add("provenance.run_at must be an offset-carrying date-time");
            }
        }

        private static void ValidateMetricBasis(MetricBasis basis, List<string> errors)
        {
            if (basis == null)
            {
                errors.Add("metric_basis is required");
                return;
            }
            if (basis.Currency != null && !CurrencyPattern.IsMatch(basis.Currency))
            {
                errors.Add("metric_basis.currency must be a three-letter code");
            }
            if (basis.RiskFreeRateUnit != "PERCENT"
                || basis.ReturnMetricUnit != "PERCENT"
                || basis.ProbabilityUnit != "PERCENT"
                || basis.TurnoverUnit != "MULTIPLE"
                || basis.ReturnSeriesUnit != "FRACTION")
            {
                errors.Add("metric_basis units are fixed by the contract and must not be re-stated");
            }
        }

        private static void ValidateBacktest(BacktestWindow backtest, List<string> errors)
        {
            if (backtest == null)
            {
                errors.Add("backtest is required");
                return;
            }
            foreach (var pair in new[] { ("start", backtest.Start), ("end", backtest.End) })
            {
                if (pair.Item2 != null && !DatePattern.IsMatch(pair.Item2))
                {
                    errors.Add($"backtest.{pair.Item1} must be a calendar date");
                }
            }
            if (backtest.MinYears.HasValue && backtest.MinYears.Value < 0)
            {
                errors.Add("backtest.min_years must not be negative");
            }
        }

        private static void ValidateReturnSeries(ReturnSeries series, List<string> errors)
        {
            if (series == null)
            {
                errors.Add("return_series is required");
                return;
            }
            if (series.Kind != "BACKTEST" || series.Frequency != "DAILY" || series.Unit != "FRACTION")
            {
                errors.Add("return_series kind, frequency and unit are fixed by the contract");
            }
            if (series.Observations == null)
            {
                errors.Add("return_series.observations is required");
                return;
            }
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var observation in series.Observations)
            {
                if (observation.Date == null || !DatePattern.IsMatch(observation.Date))
                {
                    errors.Add("every observation carries a calendar date");
                    break;
                }
                if (!seen.Add(observation.Date))
                {
                    errors.Add($"return_series repeats {observation.Date}");
                    break;
                }
            }
        }

        private static void Enumerated(string value, string[] allowed, string field, List<string> errors)
        {
            if (value == null || Array.IndexOf(allowed, value) < 0)
            {
                errors.Add($"{field} must be one of {string.Join(", ", allowed)}");
            }
        }
    }
}
