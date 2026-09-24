/*
 * QUANT-36 / ADR-0003 gate - the one-alpha spike that proves this engine can
 * emit the shared alpha_artifact contract.
 *
 * The algorithm itself is deliberately ordinary: six-month momentum across four
 * US index funds, reweighted monthly, long only. What matters is the evidence
 * it writes about its own run - identity, authored copy in both product
 * locales, classification, and the end-of-session portfolio value on every
 * session it saw. Orion/AlphaArtifact turns that plus LEAN's own result
 * document into an alpha_artifact 1.0, and Orion/tools/validate_alpha_artifact.py
 * holds the emitted bytes to the pinned normative schema.
 *
 * Nothing here is advice and nothing here is a recommendation: the artifact is
 * research evidence about a strategy, for a decision-support catalogue.
 */

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using QuantConnect.Data;
using QuantConnect.Indicators;
using QuantConnect.Interfaces;

namespace QuantConnect.Algorithm.CSharp
{
    /// <summary>
    /// Emits Orion's alpha_artifact run evidence beside LEAN's own result
    /// document. See Orion/README.md for the two-step emit and how it is
    /// validated.
    /// </summary>
    public class OrionUsMomentumAlphaArtifactAlgorithm : QCAlgorithm
    {
        private const string EvidenceVersion = "orion-lean-run-evidence-v1";
        private const int LookbackSessions = 126;
        private const int HeldNames = 2;

        private static readonly string[] UniverseTickers = { "SPY", "QQQ", "IWM", "EEM" };

        private readonly Dictionary<Symbol, MomentumPercent> _momentum = new();
        private readonly Dictionary<Symbol, decimal> _previousClose = new();
        private readonly List<KeyValuePair<DateTime, decimal>> _sessionEquity = new();

        private Symbol _reference;
        private decimal _maxBarMovePercent;

        public override void Initialize()
        {
            SetStartDate(2017, 1, 3);
            SetEndDate(2021, 3, 31);
            SetCash(100000);

            foreach (var ticker in UniverseTickers)
            {
                var equity = AddEquity(ticker, Resolution.Daily);
                equity.SetDataNormalizationMode(DataNormalizationMode.Adjusted);
                _momentum[equity.Symbol] = MOMP(equity.Symbol, LookbackSessions, Resolution.Daily);
                if (ticker == "SPY")
                {
                    _reference = equity.Symbol;
                }
            }

            SetBenchmark(_reference);
            SetWarmUp(LookbackSessions + 10, Resolution.Daily);

            Schedule.On(
                DateRules.MonthStart(_reference),
                TimeRules.AfterMarketOpen(_reference, 30),
                Reweight);
        }

        public override void OnData(Slice slice)
        {
            RecordSession(slice);
            foreach (var bar in slice.Bars.Values)
            {
                if (_previousClose.TryGetValue(bar.Symbol, out var previous) && previous > 0m)
                {
                    var move = Math.Abs(bar.Close / previous - 1m) * 100m;
                    if (move > _maxBarMovePercent)
                    {
                        _maxBarMovePercent = move;
                    }
                }
                _previousClose[bar.Symbol] = bar.Close;
            }
        }

        /// <summary>
        /// Hold the strongest names by six-month momentum, equally weighted,
        /// and hold nothing when nothing has positive momentum.
        /// </summary>
        private void Reweight()
        {
            if (IsWarmingUp)
            {
                return;
            }

            var ranked = _momentum
                .Where(pair => pair.Value.IsReady && pair.Value.Current.Value > 0m)
                .OrderByDescending(pair => pair.Value.Current.Value)
                .Take(HeldNames)
                .Select(pair => pair.Key)
                .ToList();

            foreach (var holding in Portfolio.Values.Where(holding => holding.Invested))
            {
                if (!ranked.Contains(holding.Symbol))
                {
                    Liquidate(holding.Symbol);
                }
            }

            if (ranked.Count == 0)
            {
                return;
            }

            var weight = 1m / ranked.Count;
            foreach (var symbol in ranked)
            {
                SetHoldings(symbol, weight);
            }
        }

        /// <summary>
        /// Record the portfolio value once per session, dated by that session.
        ///
        /// It is recorded here, on the session's own bar, and not in
        /// OnEndOfDay: with daily data the end-of-day event fires before the
        /// session's bar is applied, so a value recorded there carries the
        /// previous session's marks under this session's date, and every
        /// observation in the artifact would be labelled one session late.
        /// </summary>
        private void RecordSession(Slice slice)
        {
            if (IsWarmingUp || !slice.Bars.ContainsKey(_reference))
            {
                return;
            }

            var day = Time.Date;
            var value = Portfolio.TotalPortfolioValue;
            if (_sessionEquity.Count > 0 && _sessionEquity[^1].Key == day)
            {
                _sessionEquity[^1] = new KeyValuePair<DateTime, decimal>(day, value);
                return;
            }
            _sessionEquity.Add(new KeyValuePair<DateTime, decimal>(day, value));
        }

        public override void OnEndOfAlgorithm()
        {
            var path = Path.Combine(
                Globals.ResultsDestinationFolder,
                $"{AlgorithmId}-orion-evidence.json");
            File.WriteAllText(path, JsonConvert.SerializeObject(Evidence(), Formatting.Indented));
            Log($"Orion run evidence written to {path} ({_sessionEquity.Count} sessions)");
        }

        private JObject Evidence()
        {
            return new JObject
            {
                ["evidence_version"] = EvidenceVersion,
                ["alpha_id"] = "lean_us_index_momentum",
                ["version"] = "1.0.0",
                ["name"] = "US index-fund momentum (LEAN)",
                ["description"] =
                    "Six-month momentum ranking across four US index funds, reweighted monthly, long only.",
                ["localized_name"] = new JObject
                {
                    ["ar"] = "زخم صناديق المؤشرات الأمريكية",
                    ["en"] = "US Index-Fund Momentum"
                },
                ["localized_description"] = new JObject
                {
                    ["ar"] =
                        "دراسة بحثية تصنّف أربعة صناديق مؤشرات أمريكية وفق زخم ستة أشهر وتعيد ترجيحها شهرياً، شراء فقط، لدعم القرار فقط.",
                    ["en"] =
                        "Research study ranking four US index funds by six-month momentum and reweighting monthly, long only, for decision support."
                },
                ["author"] = "research@orion.sa",
                ["classification"] = new JObject
                {
                    ["object_type"] = "ALPHA",
                    ["venue"] = "US",
                    ["asset_class"] = "ETF",
                    ["family"] = "MOMENTUM",
                    ["horizon"] = "MONTHS",
                    ["direction"] = "LONG_ONLY",
                    ["rebalance"] = "MONTHLY"
                },
                ["benchmark_id"] = "lean_quantconnect.spy_pr",
                ["risk_free_rate_percent"] = AverageRiskFreeRatePercent(),
                ["min_backtest_years"] = 3,
                ["run_at"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
                ["run_by"] = "lean-launcher",
                ["continuous_adjust"] = true,
                ["max_bar_move_percent"] = Math.Round(_maxBarMovePercent, 4),
                ["data_files"] = new JArray(
                    UniverseTickers.Select(ticker => $"equity/usa/daily/{ticker.ToLowerInvariant()}.zip")),
                ["daily_equity"] = new JArray(_sessionEquity.Select(session => new JObject
                {
                    ["date"] = session.Key.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    ["value"] = session.Value
                })),
                ["manifest"] = new JObject
                {
                    ["engine_version"] = Globals.Version,
                    ["algorithm"] = nameof(OrionUsMomentumAlphaArtifactAlgorithm),
                    ["universe"] = new JArray(UniverseTickers),
                    ["resolution"] = "Daily",
                    ["lookback_sessions"] = LookbackSessions,
                    ["held_names"] = HeldNames,
                    ["rebalance_rule"] = "first session of each month, equal weights",
                    ["output_contract"] = new JObject { ["kind"] = "target_weights" },
                    ["price_basis"] = "split and dividend adjusted daily closes",
                    ["benchmark_definition"] = new JObject
                    {
                        ["ar"] = "عائد سعر إغلاق صندوق SPY كما سجّله المحرك، وليس سلسلة مؤشر مرخّصة.",
                        ["en"] = "SPY close-to-close price return as recorded by the engine, not a licensed index series."
                    },
                    ["fee_basis"] = "LEAN default US equity fee and slippage models",
                    ["risk_free_rate_source"] =
                        "LEAN alternative/interest-rate/usa primary credit rate, averaged over the recorded sessions"
                }
            };
        }

        /// <summary>
        /// The engine's own risk-free series over the sessions this run saw, as
        /// an annualised percent. Null when the run recorded no session, so the
        /// artifact carries a flag rather than a zero nobody measured.
        /// </summary>
        private decimal? AverageRiskFreeRatePercent()
        {
            if (_sessionEquity.Count == 0)
            {
                return null;
            }
            var total = _sessionEquity.Sum(session => RiskFreeInterestRateModel.GetInterestRate(session.Key));
            return Math.Round(total / _sessionEquity.Count * 100m, 6);
        }
    }
}
