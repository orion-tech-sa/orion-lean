/*
 * QUANT-36 - the emitter as a command.
 *
 *   dotnet Orion.AlphaArtifact.dll \
 *     --result   Launcher/bin/Release/<algorithm-id>.json \
 *     --evidence Launcher/bin/Release/<algorithm-id>-orion-evidence.json \
 *     --data     Data \
 *     --out      Orion/evidence/<alpha-id>-artifact.json
 *
 * Exit code 0 means an artifact that this binding accepts was written. Any
 * refusal exits 2 and writes nothing, so a broken run cannot leave a
 * half-credible artifact behind for someone to publish.
 */

using System;
using System.IO;
using System.Linq;

namespace Orion.AlphaArtifact
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            string result = null;
            string evidence = null;
            string output = null;
            string dataFolder = null;
            string verify = null;
            var selfTest = false;

            for (var index = 0; index < args.Length; index++)
            {
                if (args[index] == "--self-test")
                {
                    selfTest = true;
                }
            }

            args = args.Where(argument => argument != "--self-test").ToArray();

            for (var index = 0; index + 1 < args.Length; index += 2)
            {
                switch (args[index])
                {
                    case "--result":
                        result = args[index + 1];
                        break;
                    case "--evidence":
                        evidence = args[index + 1];
                        break;
                    case "--out":
                        output = args[index + 1];
                        break;
                    case "--data":
                        dataFolder = args[index + 1];
                        break;
                    case "--verify":
                        verify = args[index + 1];
                        break;
                    default:
                        Console.Error.WriteLine($"unknown argument {args[index]}");
                        return 2;
                }
            }

            if (verify != null)
            {
                // Validate a document that already exists, and optionally prove
                // this binding refuses every mutation of it.
                var document = File.ReadAllText(verify);
                if (selfTest)
                {
                    return ContractSelfTest.Run(document, Console.WriteLine) ? 0 : 2;
                }
                var problems = AlphaArtifactContract.ValidateDocument(document);
                if (problems.Count > 0)
                {
                    Console.Error.WriteLine(string.Join(Environment.NewLine, problems));
                    return 2;
                }
                Console.WriteLine($"{verify} satisfies alpha_artifact 1.0 under the C# binding");
                return 0;
            }

            if (result == null || evidence == null || output == null)
            {
                Console.Error.WriteLine(
                    "usage: --result <lean result json> --evidence <run evidence json> --out <artifact json> [--data <data folder>]"
                    + Environment.NewLine
                    + "   or: --verify <artifact json> [--self-test]");
                return 2;
            }

            try
            {
                var artifact = AlphaArtifactBuilder.Build(
                    File.ReadAllText(result),
                    File.ReadAllText(evidence),
                    dataFolder);

                var json = artifact.ToJson();
                var errors = AlphaArtifactContract.ValidateDocument(json);
                if (errors.Count > 0)
                {
                    // The serialized bytes are what the store receives, so they
                    // are checked again after serialization rather than only
                    // the object that produced them.
                    Console.Error.WriteLine(string.Join(Environment.NewLine, errors));
                    return 2;
                }

                var directory = Path.GetDirectoryName(Path.GetFullPath(output));
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                File.WriteAllText(output, json + Environment.NewLine);
                Console.WriteLine(
                    $"alpha_artifact 1.0 written to {output} " +
                    $"({artifact.ReturnSeries.Observations.Count} daily observations, " +
                    $"{artifact.Backtest.Start} to {artifact.Backtest.End})");
                return 0;
            }
            catch (AlphaArtifactContractException exception)
            {
                Console.Error.WriteLine(exception.Message);
                return 2;
            }
        }
    }
}
