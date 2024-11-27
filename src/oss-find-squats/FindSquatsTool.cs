// Copyright (c) Microsoft Corporation. Licensed under the MIT License.

namespace Microsoft.CST.OpenSource.FindSquats
{
    using CommandLine;
    using CommandLine.Text;
    using Microsoft.CodeAnalysis.Sarif;
    using Microsoft.CST.OpenSource.Shared;
    using Mutators;
    using Extensions;
    using Newtonsoft.Json;
    using PackageManagers;
    using PackageUrl;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading.Tasks;
    using SarifResult = Microsoft.CodeAnalysis.Sarif.Result;

    public class FindSquatsTool : OSSGadget
    {
        public class Options
        {
            [Usage()]
            public static IEnumerable<Example> Examples
            {
                get
                {
                    return new List<Example>() {
                        new Example("Find Squat Candidates for the Given Packages",
                        new Options { Targets = new List<string>() {"[options]", "package-urls..." } })};
                }
            }

            [Option('o', "output-file", Required = false, Default = "",
                HelpText = "send the command output to a file instead of stdout")]
            public string OutputFile { get; set; } = "";

            [Option('f', "format", Required = false, Default = "text",
                HelpText = "specify the output format(text|sarifv1|sarifv2)")]
            public string Format { get; set; } = "text";

            [Option('q', "quiet", Required = false, Default = false,
                HelpText = "Suppress console output.")]
            public bool Quiet { get; set; } = false;

            [Option('s', "sleep-delay", Required = false, Default = 0, HelpText = "Number of ms to sleep between checks.")]
            public int SleepDelay { get; set; }

            [Value(0, Required = true,
                HelpText = "PackgeURL(s) specifier to analyze (required, repeats OK)", Hidden = true)] // capture all targets to analyze
            public IEnumerable<string>? Targets { get; set; }

        }

        public FindSquatsTool(ProjectManagerFactory projectManagerFactory) : base(projectManagerFactory)
        {
        }

        public FindSquatsTool() : this(new ProjectManagerFactory())
        {
        }

        static async Task Main(string[] args)
        {
            ShowToolBanner();
            FindSquatsTool findSquatsTool = new();
            (string output, int numSquats) = (string.Empty, 0);
            await findSquatsTool.ParseOptions<Options>(args).WithParsedAsync(async options =>
            {
                (output, numSquats) = await findSquatsTool.RunAsync(options);
                if (string.IsNullOrEmpty(options.OutputFile))
                {
                    options.OutputFile = $"oss-find-squat.{options.Format}";
                }
                if (!options.Quiet)
                {
                    if (numSquats > 0)
                    {
                        Logger.Warn($"Found {numSquats} potential squats.");
                    }
                    else
                    {
                        Logger.Info($"No squats detected.");
                    }
                }
                using StreamWriter fw = new(options.OutputFile);
                await fw.WriteLineAsync(output);
                fw.Close();
            });
        }

        public IEnumerable<FindPackageSquatResult> FindExistingSquatsFromFile(PackageURL purl, IDictionary<string, IList<Mutation>>? candidateMutations, List<string> allPackages, MutateOptions? options = null)
        {
            if (purl.Name is null || purl.Type is null)
            {
                yield break;
            }

            if (candidateMutations is not null)
            {
                foreach ((string candidatePurlString, IList<Mutation> mutations) in candidateMutations)
                {
                    // Create the purl from the mutation to see if it exists.
                    PackageURL candidatePurl = new(candidatePurlString);
                    FindPackageSquatResult? res = null;

                    var pkgName = candidatePurl.GetFullName();

                    if (allPackages.Contains(pkgName))
                    {
                        // The candidate mutation exists on the package registry.
                        res = new FindPackageSquatResult(
                            mutatedPackageName: candidatePurl.GetFullName(),
                            mutatedPackageUrl: candidatePurl,
                            originalPackageUrl: purl,
                            mutations: mutations);

                        yield return res;
                    }
                }

            }
        }

        public async Task<(string output, int numSquats)> RunAsync(Options options)
        {
            List<string> allPackages = new List<string>();
            List<string> csvs = new List<string> { 
                @"../oss-find-squats-lib/pypi_popular.csv",
                @"../oss-find-squats-lib/pypi_unpopular.csv"
            };

            foreach (string csvFile in csvs)
            {
                using(var reader = new StreamReader(csvFile))
                {
                    // skip headers
                    reader.ReadLine();

                    while (!reader.EndOfStream)
                    {
                        var line = reader.ReadLine();
                        var values = line.Split(',');

                        allPackages.Add(values[0]);
                    }
                }
            }

            IOutputBuilder? outputBuilder = SelectFormat(options.Format);
            int foundSquats = 0;
            MutateOptions? checkerOptions = new()
            {
                SleepDelay = options.SleepDelay
            };
            foreach (string? target in options.Targets ?? Array.Empty<string>())
            {
                PackageURL? purl = new(target);
                if (purl.Name is null || purl.Type is null)
                {
                    Logger.Trace($"Could not generate valid PackageURL from { target }.");
                    continue;
                }

                FindPackageSquats findPackageSquats = new FindPackageSquats(ProjectManagerFactory, purl);

                IDictionary<string, IList<Mutation>>? potentialSquats = findPackageSquats.GenerateSquatCandidates(options: checkerOptions);

                foreach (FindPackageSquatResult? potentialSquat in FindExistingSquatsFromFile(findPackageSquats.PackageUrl,  potentialSquats, allPackages, checkerOptions))
                {
                    foundSquats++;
                    if (!options.Quiet)
                    {
                        Logger.Info($"{potentialSquat.MutatedPackageName} package exists. Potential squat. {JsonConvert.SerializeObject(potentialSquat.Rules)}");
                    }
                    if (outputBuilder is SarifOutputBuilder sarob)
                    {
                        SarifResult? sarifResult = new()
                        {
                            Message = new Message()
                            {
                                Text = $"Potential Squat candidate { potentialSquat.MutatedPackageName }.",
                                Id = "oss-find-squats"
                            },
                            Kind = ResultKind.Review,
                            Level = FailureLevel.None,
                            Locations = SarifOutputBuilder.BuildPurlLocation(potentialSquat.MutatedPackageUrl),
                        };
                        foreach (string? tag in potentialSquat.Rules)
                        {
                            sarifResult.Tags.Add(tag);
                        }
                        sarob.AppendOutput(new SarifResult[] { sarifResult });
                    }
                    else if (outputBuilder is StringOutputBuilder strob)
                    {
                        string? rulesString = string.Join(',', potentialSquat.Rules);
                        strob.AppendOutput(new string[] { $"Potential Squat candidate '{ potentialSquat.MutatedPackageName }' detected. Generated by { rulesString }.{Environment.NewLine}" });
                    }
                    else
                    {
                        string? rulesString = string.Join(',', potentialSquat.Rules);
                        if (!options.Quiet)
                        {
                            Logger.Info($"Potential Squat candidate '{ potentialSquat.MutatedPackageName }' detected. Generated by { rulesString }.");
                        }
                    }
                }
            }

            return (outputBuilder.GetOutput(), foundSquats);
        }
    }
}