/*
 * QUANT-36 - turn one finished LEAN backtest into one alpha_artifact 1.0.
 *
 * Inputs are both produced by the engine: LEAN's own result document
 * (<algorithm-id>.json, written by BaseResultsHandler.SaveResults) and the run
 * evidence the algorithm wrote about itself. Nothing here invents a number: a
 * statistic that LEAN did not report is null with a data-quality flag beside
 * it, and a window shorter than the alpha declares is refused rather than
 * published.
 *
 * The statistics are read through LEAN's own PortfolioStatistics and
 * AlgorithmConfiguration types, so a rename upstream breaks the build here
 * instead of quietly emitting nulls.
 */

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using QuantConnect;
using QuantConnect.Statistics;

namespace Orion.AlphaArtifact
{
    public static class AlphaArtifactBuilder
    {
        /// <summary>Percent points; LEAN reports fractions for these.</summary>
        private const decimal PercentScale = 100m;

        public static AlphaArtifactV1 Build(string resultJson, string evidenceJson, string dataFolder = null)
        {
            var result = JObject.Parse(resultJson);
            var evidence = JsonConvert.DeserializeObject<RunEvidence>(evidenceJson);
            if (evidence == null || evidence.EvidenceVersion != RunEvidence.ExpectedVersion)
            {
                throw new AlphaArtifactContractException(
                    $"run evidence must declare {RunEvidence.ExpectedVersion}");
            }

            var statistics = ReadPortfolioStatistics(result);
            var configuration = ReadConfiguration(result);
            var flags = new List<string>();

            if (statistics.StartEquity <= 0m)
            {
                throw new AlphaArtifactContractException(
                    "the result document reports no starting equity, so the first session has no return");
            }
            var observations = DailyReturns(evidence, statistics.StartEquity);
            if (observations.Count < 2)
            {
                throw new AlphaArtifactContractException(
                    "the run recorded fewer than two sessions, so it has no daily return series");
            }

            var start = configuration.StartDate.Date;
            var end = configuration.EndDate.Date;
            var years = (end - start).TotalDays / 365.25;
            if (evidence.MinBacktestYears.HasValue && years + 1e-9 < evidence.MinBacktestYears.Value)
            {
                throw new AlphaArtifactContractException(
                    $"the run spans {years:0.00} years and the alpha declares a minimum of " +
                    $"{evidence.MinBacktestYears.Value}");
            }

            decimal? riskFree = evidence.RiskFreeRatePercent;
            if (!riskFree.HasValue)
            {
                flags.Add("risk_free_rate_not_reported_by_engine");
            }

            var benchmarkReturn = BenchmarkReturnPercent(result);
            if (!benchmarkReturn.HasValue)
            {
                flags.Add("benchmark_return_unavailable");
            }

            if (string.IsNullOrWhiteSpace(evidence.RunAt))
            {
                // The contract allows a null run_at, and requires it to say so.
                flags.Add("run_at_not_reported_by_engine");
            }

            var snapshot = DataSnapshot(evidence, dataFolder ?? Globals.DataFolder);
            if (snapshot == null)
            {
                flags.Add("data_snapshot_unavailable");
            }

            var artifact = new AlphaArtifactV1
            {
                AlphaId = evidence.AlphaId,
                Version = evidence.Version,
                Name = evidence.Name,
                Description = evidence.Description,
                LocalizedName = evidence.LocalizedName,
                LocalizedDescription = evidence.LocalizedDescription,
                Author = evidence.Author,
                Classification = evidence.Classification,
                Provenance = new Provenance
                {
                    SourceEngine = "LEAN_QUANTCONNECT",
                    RunType = "ENGINE_RUN",
                    RunAt = string.IsNullOrWhiteSpace(evidence.RunAt) ? null : evidence.RunAt,
                    RunBy = evidence.RunBy,
                    DataSnapshot = snapshot,
                    ReportSource = null
                },
                MetricBasis = new MetricBasis
                {
                    Currency = configuration.AccountCurrency,
                    Benchmark = evidence.BenchmarkId,
                    RiskFreeRate = riskFree
                },
                Metrics = HeadlineFrom(statistics, configuration, benchmarkReturn),
                Backtest = new BacktestWindow
                {
                    Start = start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    End = end.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    MinYears = evidence.MinBacktestYears
                },
                ReturnSeries = new ReturnSeries { Observations = observations },
                DataQuality = new DataQuality
                {
                    ContinuousAdjust = evidence.ContinuousAdjust,
                    MaxBarMovePercent = evidence.MaxBarMovePercent
                },
                Manifest = Manifest(evidence, configuration),
                ValidationReport = ValidationReport(result, evidence, observations.Count, years),
                DataQualityFlags = flags
            };

            var errors = AlphaArtifactContract.Validate(artifact);
            if (errors.Count > 0)
            {
                throw new AlphaArtifactContractException(
                    "the artifact this run produced does not satisfy alpha_artifact 1.0:" +
                    Environment.NewLine + string.Join(Environment.NewLine, errors));
            }

            return artifact;
        }

        private static PortfolioStatistics ReadPortfolioStatistics(JObject result)
        {
            var node = result["totalPerformance"]?["portfolioStatistics"]
                       ?? result["TotalPerformance"]?["PortfolioStatistics"];
            if (node == null)
            {
                throw new AlphaArtifactContractException(
                    "the result document carries no portfolio statistics, so there is nothing to report");
            }
            return node.ToObject<PortfolioStatistics>();
        }

        private static AlgorithmConfiguration ReadConfiguration(JObject result)
        {
            var node = result["algorithmConfiguration"] ?? result["AlgorithmConfiguration"];
            if (node == null)
            {
                throw new AlphaArtifactContractException(
                    "the result document carries no algorithm configuration, so the window is unknown");
            }
            var configuration = node.ToObject<AlgorithmConfiguration>();
            if (configuration.StartDate == default || configuration.EndDate == default)
            {
                throw new AlphaArtifactContractException("the run window is not stated in the result document");
            }
            return configuration;
        }

        private static HeadlineMetrics HeadlineFrom(
            PortfolioStatistics statistics,
            AlgorithmConfiguration configuration,
            decimal? benchmarkReturnPercent)
        {
            var sessionsPerYear = configuration.TradingDaysPerYear > 0
                ? configuration.TradingDaysPerYear
                : 252;

            var metrics = new HeadlineMetrics
            {
                Sharpe = statistics.SharpeRatio,
                AnnualReturn = statistics.CompoundingAnnualReturn * PercentScale,
                // LEAN reports drawdown as a positive fraction of peak equity;
                // the contract carries a signed percent, as the store displays it.
                MaxDrawdown = -(statistics.Drawdown * PercentScale),
                // LEAN's portfolio turnover is a mean daily fraction of the
                // portfolio; the contract's unit is an annual multiple.
                Turnover = statistics.PortfolioTurnover * sessionsPerYear,
                Psr = statistics.ProbabilisticSharpeRatio * PercentScale,
                BenchmarkReturn = benchmarkReturnPercent
            };

            metrics.Additional["sortino"] = statistics.SortinoRatio;
            metrics.Additional["beta"] = statistics.Beta;
            metrics.Additional["annual_volatility"] = statistics.AnnualStandardDeviation * PercentScale;
            metrics.Additional["information_ratio"] = statistics.InformationRatio;
            metrics.Additional["value_at_risk_95"] = statistics.ValueAtRisk95;
            metrics.Additional["value_at_risk_99"] = statistics.ValueAtRisk99;
            return metrics;
        }

        /// <summary>
        /// Close-to-close fractional returns from the algorithm's own record of
        /// its end-of-session portfolio value. The display chart is not used:
        /// it exists to be drawn, and a drawn series may be resampled.
        /// </summary>
        private static List<ReturnObservation> DailyReturns(RunEvidence evidence, decimal startEquity)
        {
            var equity = evidence.DailyEquity ?? new List<EquityObservation>();
            var observations = new List<ReturnObservation>();
            for (var index = 0; index < equity.Count; index++)
            {
                // The first session is measured from the run's starting equity,
                // which LEAN states in the result document. Dropping it instead
                // would leave a series that cannot be compounded back to the
                // engine's own net profit, and a series that does not reconcile
                // is not evidence.
                var previous = index == 0
                    ? new EquityObservation { Date = null, Value = startEquity }
                    : equity[index - 1];
                var current = equity[index];
                if (previous.Date != null && string.CompareOrdinal(current.Date, previous.Date) <= 0)
                {
                    throw new AlphaArtifactContractException(
                        $"the recorded session values are not in ascending date order at {current.Date}");
                }
                if (previous.Value <= 0m)
                {
                    throw new AlphaArtifactContractException(
                        $"the portfolio value before {current.Date} is not positive, so no return can be taken from it");
                }
                observations.Add(new ReturnObservation
                {
                    Date = current.Date,
                    Value = Math.Round(current.Value / previous.Value - 1m, 10)
                });
            }
            return observations;
        }

        /// <summary>
        /// Percent change of LEAN's benchmark series over the same window, or
        /// null when the run recorded no benchmark.
        /// </summary>
        private static decimal? BenchmarkReturnPercent(JObject result)
        {
            var values = result["charts"]?["Benchmark"]?["series"]?["Benchmark"]?["values"] as JArray;
            if (values == null || values.Count < 2)
            {
                return null;
            }
            var first = EdgeValue(values, false);
            var last = EdgeValue(values, true);
            if (!first.HasValue || !last.HasValue || first.Value <= 0m)
            {
                return null;
            }
            return (last.Value / first.Value - 1m) * PercentScale;
        }

        private static decimal? EdgeValue(JArray values, bool fromEnd)
        {
            var indexes = Enumerable.Range(0, values.Count);
            if (fromEnd)
            {
                indexes = indexes.Reverse();
            }
            foreach (var index in indexes)
            {
                if (values[index] is JArray point && point.Count >= 2
                    && (point[1].Type == JTokenType.Float || point[1].Type == JTokenType.Integer))
                {
                    var value = point[1].Value<decimal>();
                    if (value > 0m)
                    {
                        return value;
                    }
                }
            }
            return null;
        }

        private static string DataSnapshot(RunEvidence evidence, string dataFolder)
        {
            var files = (evidence.DataFiles ?? new List<string>())
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToList();
            if (files.Count == 0)
            {
                return null;
            }

            var builder = new StringBuilder();
            foreach (var relative in files)
            {
                var full = Path.Combine(dataFolder, relative);
                if (!File.Exists(full))
                {
                    // A snapshot that names a file nobody can hash is not a
                    // snapshot. Say so rather than hashing the name.
                    return null;
                }
                using (var stream = File.OpenRead(full))
                using (var sha = SHA256.Create())
                {
                    builder.Append(relative.Replace('\\', '/'));
                    builder.Append(':');
                    builder.Append(Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant());
                    builder.Append('\n');
                }
            }

            using (var sha = SHA256.Create())
            {
                var digest = sha.ComputeHash(Encoding.UTF8.GetBytes(builder.ToString()));
                return "lean-local-data:sha256:" + Convert.ToHexString(digest).ToLowerInvariant();
            }
        }

        private static JObject Manifest(RunEvidence evidence, AlgorithmConfiguration configuration)
        {
            var manifest = evidence.Manifest != null
                ? (JObject)evidence.Manifest.DeepClone()
                : new JObject();
            manifest["engine"] = "LEAN";
            manifest["account_currency"] = configuration.AccountCurrency;
            manifest["sessions_per_year"] = configuration.TradingDaysPerYear;
            manifest["data_files"] = new JArray(evidence.DataFiles ?? new List<string>());
            return manifest;
        }

        private static JObject ValidationReport(
            JObject result,
            RunEvidence evidence,
            int observationCount,
            double years)
        {
            var gates = new JObject
            {
                ["min_backtest_years"] = evidence.MinBacktestYears.HasValue
                    ? (years + 1e-9 >= evidence.MinBacktestYears.Value ? "pass" : "fail")
                    : "not_declared",
                ["daily_return_series"] = observationCount >= 20 ? "pass" : "fail"
            };

            var details = new JObject
            {
                ["engine"] = "LEAN",
                ["window_years"] = Math.Round(years, 4),
                ["daily_observations"] = observationCount,
                ["portfolio_statistics"] =
                    result["totalPerformance"]?["portfolioStatistics"]?.DeepClone() ?? JValue.CreateNull(),
                ["algorithm_configuration"] =
                    result["algorithmConfiguration"]?.DeepClone() ?? JValue.CreateNull()
            };

            return new JObject
            {
                ["gates"] = gates,
                ["engine_details"] = details
            };
        }
    }
}
