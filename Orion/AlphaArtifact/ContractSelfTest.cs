/*
 * QUANT-36 - mutation proofs for the C# binding, run against a real artifact.
 *
 * The Python tool proves the pinned schema and the engine gate refuse their
 * mutations. This proves the same of this binding, separately, because the
 * binding is what a LEAN algorithm actually goes through: if it drifted from the
 * schema it could emit a document the store refuses, or refuse one the store
 * accepts, and the first anyone would hear of it is a rejected submission.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Orion.AlphaArtifact
{
    public static class ContractSelfTest
    {
        private sealed class Mutation
        {
            public string Label { get; init; }
            public Action<JObject> Apply { get; init; }
        }

        private static readonly List<Mutation> Mutations = new()
        {
            new Mutation
            {
                Label = "artifact_version is not 1.0",
                Apply = document => document["artifact_version"] = "1.1"
            },
            new Mutation
            {
                Label = "alpha_id is not an identifier",
                Apply = document => document["alpha_id"] = "LEAN US Momentum"
            },
            new Mutation
            {
                Label = "name is blank",
                Apply = document => document["name"] = "   "
            },
            new Mutation
            {
                Label = "a store-owned status is emitted",
                Apply = document => document["status"] = "PUBLISHED"
            },
            new Mutation
            {
                Label = "a serialization-time generated_at is emitted",
                Apply = document => document["generated_at"] = "2026-09-24T00:00:00Z"
            },
            new Mutation
            {
                Label = "the venue is not an enumerated value",
                Apply = document => document["classification"]["venue"] = "NASDAQ"
            },
            new Mutation
            {
                Label = "the rebalance cadence is not an enumerated value",
                Apply = document => document["classification"]["rebalance"] = "FORTNIGHTLY"
            },
            new Mutation
            {
                Label = "the source engine is not an enumerated value",
                Apply = document => document["provenance"]["source_engine"] = "LEAN"
            },
            new Mutation
            {
                Label = "the return series changes unit",
                Apply = document => document["return_series"]["unit"] = "PERCENT"
            },
            new Mutation
            {
                Label = "the metric basis renames its currency",
                Apply = document => document["metric_basis"]["currency"] = "dollars"
            },
            new Mutation
            {
                Label = "the turnover unit is restated",
                Apply = document => document["metric_basis"]["turnover_unit"] = "PERCENT"
            },
            new Mutation
            {
                Label = "English copy is relabelled as Arabic",
                Apply = document =>
                    document["localized_description"]["ar"] =
                        document["localized_description"]["en"].Value<string>() + " ن"
            },
            new Mutation
            {
                Label = "the Arabic locale is dropped",
                Apply = document => ((JObject)document["localized_name"]).Remove("ar")
            },
            new Mutation
            {
                Label = "both locales carry the same string",
                Apply = document =>
                    document["localized_name"]["ar"] = document["localized_name"]["en"]
            },
            new Mutation
            {
                Label = "run_at loses its offset",
                Apply = document => document["provenance"]["run_at"] = "2026-09-24T19:09:17"
            },
            new Mutation
            {
                Label = "a null run_at carries no flag",
                Apply = document =>
                {
                    document["provenance"]["run_at"] = null;
                    document["data_quality_flags"] = new JArray();
                }
            },
            new Mutation
            {
                Label = "a session is reported twice",
                Apply = document =>
                {
                    var observations = (JArray)document["return_series"]["observations"];
                    observations.Add(new JObject
                    {
                        ["date"] = observations[0]["date"],
                        ["value"] = 0.0
                    });
                }
            },
            new Mutation
            {
                Label = "a data-quality flag is blank",
                Apply = document => document["data_quality_flags"] = new JArray(" ")
            },
            new Mutation
            {
                Label = "the backtest window is not a calendar date",
                Apply = document => document["backtest"]["start"] = "03/01/2017"
            }
        };

        /// <summary>
        /// Returns true when the binding accepts the document and refuses every
        /// mutation of it.
        /// </summary>
        public static bool Run(string json, Action<string> report)
        {
            var accepted = AlphaArtifactContract.ValidateDocument(json);
            if (accepted.Count > 0)
            {
                report("REFUSED the artifact under test:");
                foreach (var error in accepted)
                {
                    report("  - " + error);
                }
                return false;
            }
            report("accepted the artifact under test");

            var passed = true;
            foreach (var mutation in Mutations)
            {
                var document = JObject.Parse(json);
                mutation.Apply(document);
                var errors = AlphaArtifactContract.ValidateDocument(document.ToString());
                if (errors.Count == 0)
                {
                    report("  NOT REFUSED: " + mutation.Label);
                    passed = false;
                }
                else
                {
                    report($"  refused: {mutation.Label} ({errors.First()})");
                }
            }
            return passed;
        }
    }
}
