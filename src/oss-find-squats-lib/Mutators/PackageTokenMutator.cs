namespace Microsoft.CST.OpenSource.FindSquats.Mutators
{
    using Newtonsoft.Json;
    using PackageUrl;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.RegularExpressions;

    /// <summary>
    /// Implements Prefix/Suffix Augmentation for adversarial package detection.
    /// </summary>
    public class PackageTokenMutator : IMutator
    {
        public MutatorType Kind { get; } = MutatorType.PackageToken;

        public Dictionary<string, float> PKG_TOKEN_TO_RANK { get; private set; }
        public List<string> PKG_TOKENS { get; private set; }
        public List<string> EN_TOKENS_BEFORE_LEMMATIZER { get; private set; }
        public List<string> EN_TOKENS_AFTER_LEMMATIZER { get; private set; }
        public List<string> TECH_TOKENS { get; private set; }

        public List<string> ALL_TOKENS { get; private set; }

        private const float PopularityThreshold = 0.15f; // Threshold for uncommon tokens
        private string JsonFilePath = "/Users/pari/Documents/UCDavis/Courses/Fall24/ECS235/project/ossgadget-new-feature/oss-gadget/src/data/toeknization/token_data.json"; // This line should be replaced with the token_data.json in data/toeknization folder

        private static readonly char[] DELIMITERS = { '-', '_', ' ', '.', '~' };
        private static readonly Regex DELIMITER_PATTERN = new Regex($"[{Regex.Escape(new string(DELIMITERS))}]+", RegexOptions.Compiled);

        private Dictionary<string, List<string>> Corpora;

        public PackageTokenMutator()
        {
            LoadTokenData();
            // Corpora = LoadCorporaFromJson(JsonFilePath);
        }

        /// <summary>
        /// Replaces delimiters in the target string with a specified replacement string.
        /// </summary>
        /// <param name="target">The input string.</param>
        /// <param name="replacement">The string to replace delimiters with.</param>
        /// <returns>The string with delimiters replaced.</returns>
        public static string ReplaceDelimiters(string target, string replacement)
        {
            if (string.IsNullOrEmpty(target))
            {
                throw new ArgumentException("Target cannot be null or empty.", nameof(target));
            }

            return DELIMITER_PATTERN.Replace(target, replacement);
        }

        /// <summary>
        /// Splits the target string into a sequence based on delimiters.
        /// </summary>
        /// <param name="target">The input string.</param>
        /// <param name="deep">Ignored for simplicity, always splits using delimiters.</param>
        /// <returns>A list of string segments.</returns>
        public static List<string> ToSequence(string target, bool deep = true)
        {
            if (string.IsNullOrEmpty(target))
            {
                throw new ArgumentException("Target cannot be null or empty.", nameof(target));
            }

            // Replace delimiters with spaces and split into segments
            return ReplaceDelimiters(target, " ").Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        }


        /// <summary>
        /// Loads corpora from the JSON file and structures it as required.
        /// </summary>
        /// <param name="jsonFilePath">The path to the JSON file.</param>
        /// <returns>A dictionary of corpora keyed by the specified names.</returns>
        // private Dictionary<string, List<string>> LoadCorporaFromJson(string jsonFilePath)
        // {
        //     if (!File.Exists(jsonFilePath))
        //     {
        //         throw new FileNotFoundException($"File not found: {jsonFilePath}");
        //     }

        //     try
        //     {
        //         var jsonData = File.ReadAllText(jsonFilePath);
        //         var data = JsonConvert.DeserializeObject<Dictionary<string, List<string>>>(jsonData);

        //         // Ensure the required keys exist in the JSON data
        //         var requiredKeys = new[] { "PKG_TOKENS", "TECH_TOKENS", "ALL_TOKENS", "EN_TOKENS_AFTER_LEMMATIZER" };
        //         foreach (var key in requiredKeys)
        //         {
        //             if (!data.ContainsKey(key))
        //             {
        //                 throw new KeyNotFoundException($"Missing required key '{key}' in JSON file.");
        //             }
        //         }

        //         // Build the corpora dictionary
        //         return new Dictionary<string, List<string>>
        //         {
        //             { "en", data["EN_TOKENS_AFTER_LEMMATIZER"] },
        //             { "packages", data["PKG_TOKENS"] },
        //             { "tech", data["TECH_TOKENS"] },
        //             { "all", data["ALL_TOKENS"] }
        //         };
        //     }
        //     catch (JsonReaderException ex)
        //     {
        //         throw new InvalidOperationException($"Error parsing JSON: {ex.Message}");
        //     }
        // }


        public List<string> Segment(string target)
        {
            // Perform segmentation using different corpus_key_queues
            var forwardEn = SegmentForward(target, new Queue<string>(new[] { "en", "packages" }));
            var backwardEn = SegmentBackward(target, new Queue<string>(new[] { "en", "packages" }));
            var forwardPkg = SegmentForward(target, new Queue<string>(new[] { "packages", "en" }));
            var backwardPkg = SegmentBackward(target, new Queue<string>(new[] { "packages", "en" }));
            // Collect passes
            var passes = new List<List<string>> { forwardEn, backwardEn, forwardPkg, backwardPkg };

            // Debugging output for segmentation passes
            // foreach (var sPass in passes)
            // {
            //     Console.WriteLine($"s_pass: {string.Join(", ", sPass)}");
            // }

            // Calculate lengths and frequencies
            var lenCount = passes.Select(p => (Segments: p, Length: p.Count)).ToList();
            var countVals = lenCount.Select(l => l.Length).ToList();
            var lenFreq = countVals.GroupBy(l => l).ToDictionary(g => g.Key, g => g.Count());
            int maxLenFreq = lenFreq.OrderByDescending(kv => kv.Value).FirstOrDefault().Key;
            // Console.WriteLine($"max_len_freq: {maxLenFreq}");

            // Check for ambiguity
            bool ambiguous = lenCount.Select(l => l.Length).Distinct().Count() > 2;
            if (ambiguous)
            {
                return new List<string> { target };
            }

            // Filter viable segmentations
            var viable = passes
                .Where(p => p.All(s => Corpora["all"].Contains(s)) && p.Count == maxLenFreq)
                .Select(p => p)
                .ToList();

            // Debugging output for viable candidates
            // Console.WriteLine($"viable: {string.Join(" | ", viable.Select(v => string.Join(", ", v)))}");

            if (!viable.Any())
            {
                return new List<string> { target };
            }

            // Vote for the best candidates
            var candidateVotes = viable
                .GroupBy(v => string.Join(",", v))
                .ToDictionary(g => g.Key, g => g.Count());
            int maxVotes = candidateVotes.Values.Max();
            var bestCandidates = viable
                .Where(v => candidateVotes[string.Join(",", v)] == maxVotes)
                .ToList();

            // Select the best candidate
            var bestCandidate = bestCandidates
                .OrderBy(c => c.Min(s => s.Length))
                .FirstOrDefault();
            return bestCandidate ?? new List<string> { target };
        }

        private List<string> SegmentForward(string target, Queue<string> corpusKeyQueue)
        {
            if (string.IsNullOrEmpty(target)) return new List<string>();

            if (!corpusKeyQueue.Any())
            {
                return new List<string> { target };
            }

            var corpusKey = corpusKeyQueue.Dequeue();
            var corpus = Corpora.ContainsKey(corpusKey) ? Corpora[corpusKey] : new List<string>();

            if (target.Length <= 2 || corpus.Contains(target))
            {
                return new List<string> { target };
            }

            for (int offset = 0; offset < target.Length; offset++)
            {
                for (int window = target.Length - offset; window >= 2; window--)
                {
                    string frame = target.Substring(offset, window);
                    if (corpus.Contains(frame))
                    {
                        string postframe = target.Substring(offset + window);
                        var preframe = target.Substring(0, offset);
                        var segments = new List<string>();
                        if (!string.IsNullOrEmpty(preframe)) segments.Add(preframe);
                        segments.Add(frame);
                        segments.AddRange(SegmentForward(postframe, new Queue<string>(corpusKeyQueue)));
                        return segments;
                    }
                }
            }

            return new List<string> { target };
        }

        private List<string> SegmentBackward(string target, Queue<string> corpusKeyQueue)
        {
            if (string.IsNullOrEmpty(target)) return new List<string>();

            if (!corpusKeyQueue.Any())
            {
                return new List<string> { target };
            }

            var corpusKey = corpusKeyQueue.Dequeue();
            var corpus = Corpora.ContainsKey(corpusKey) ? Corpora[corpusKey] : new List<string>();

            if (target.Length <= 2 || corpus.Contains(target))
            {
                return new List<string> { target };
            }

            for (int offset = target.Length - 1; offset >= 0; offset--)
            {
                for (int window = offset + 1; window >= 2; window--)
                {
                    string frame = target.Substring(offset - window + 1, window);
                    if (corpus.Contains(frame))
                    {
                        string preframe = target.Substring(0, offset - window + 1);
                        var segments = SegmentBackward(preframe, new Queue<string>(corpusKeyQueue));
                        segments.Add(frame);
                        return segments;
                    }
                }
            }

            return new List<string> { target };
        }



  
        private void LoadTokenData()
        {
            if (!File.Exists(JsonFilePath))
            {
                throw new FileNotFoundException($"Token data file not found: {JsonFilePath}");
            }

            try
            {
                var jsonData = File.ReadAllText(JsonFilePath);

                // Deserialize the entire JSON structure into a dynamic object
                var jsonObject = JsonConvert.DeserializeObject<dynamic>(jsonData);

                // Load PKG_TOKEN_TO_RANK
                if (jsonObject.PKG_TOKEN_TO_RANK != null)
                {
                    PKG_TOKEN_TO_RANK = JsonConvert.DeserializeObject<Dictionary<string, float>>(Convert.ToString(jsonObject.PKG_TOKEN_TO_RANK));
                }
                else
                {
                    throw new InvalidOperationException("Missing 'PKG_TOKEN_TO_RANK' key in token data.");
                }

                // Load PKG_TOKENS
                if (jsonObject.PKG_TOKENS != null)
                {
                    PKG_TOKENS = JsonConvert.DeserializeObject<List<string>>(Convert.ToString(jsonObject.PKG_TOKENS));
                }
                else
                {
                    throw new InvalidOperationException("Missing 'PKG_TOKENS' key in token data.");
                }

                // Load EN_TOKENS_BEFORE_LEMMATIZER
                if (jsonObject.EN_TOKENS_BEFORE_LEMMATIZER != null)
                {
                    EN_TOKENS_BEFORE_LEMMATIZER = JsonConvert.DeserializeObject<List<string>>(Convert.ToString(jsonObject.EN_TOKENS_BEFORE_LEMMATIZER));
                }
                else
                {
                    throw new InvalidOperationException("Missing 'EN_TOKENS_BEFORE_LEMMATIZER' key in token data.");
                }

                // Load EN_TOKENS_AFTER_LEMMATIZER
                if (jsonObject.EN_TOKENS_AFTER_LEMMATIZER != null)
                {
                    EN_TOKENS_AFTER_LEMMATIZER = JsonConvert.DeserializeObject<List<string>>(Convert.ToString(jsonObject.EN_TOKENS_AFTER_LEMMATIZER));
                }
                else
                {
                    throw new InvalidOperationException("Missing 'EN_TOKENS_AFTER_LEMMATIZER' key in token data.");
                }

                // Load TECH_TOKENS
                if (jsonObject.TECH_TOKENS != null)
                {
                    TECH_TOKENS = JsonConvert.DeserializeObject<List<string>>(Convert.ToString(jsonObject.TECH_TOKENS));
                }
                else
                {
                    throw new InvalidOperationException("Missing 'TECH_TOKENS' key in token data.");
                }

                // Load ALL_TOKENS
                if (jsonObject.ALL_TOKENS != null)
                {
                    ALL_TOKENS = JsonConvert.DeserializeObject<List<string>>(Convert.ToString(jsonObject.ALL_TOKENS));
                }
                else
                {
                    throw new InvalidOperationException("Missing 'ALL_TOKENS' key in token data.");
                }

                Corpora = new Dictionary<string, List<string>>
                {
                    { "en", EN_TOKENS_AFTER_LEMMATIZER },
                    { "packages", PKG_TOKENS },
                    { "tech", TECH_TOKENS },
                    { "all", ALL_TOKENS }
                };
            }
            catch (JsonReaderException ex)
            {
                throw new InvalidOperationException($"Error parsing token data file: {JsonFilePath}. Check for JSON syntax issues. Error: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Generates mutations by adding prefixes or suffixes to the target package.
        /// </summary>
        /// <param name="arg">The input package URL string.</param>
        /// <returns>A list of mutations based on prefix/suffix augmentation rules.</returns>
        public IEnumerable<Mutation> Generate(string arg)
        {

            // Extract the package name from the purl
            string targetName = arg;
            // Console.WriteLine("targetName: " + targetName);
            string replaced = ReplaceDelimiters(targetName, "-");
            // Console.WriteLine($"Replaced: {replaced}");
            var sequence = ToSequence(targetName);
            // Console.WriteLine("Sequence: " + string.Join(", ", sequence));

            var segmented = Segment(targetName);
            // Console.WriteLine("Segmented: " + string.Join(", ", segmented));
       
            if (string.IsNullOrEmpty(targetName) || targetName.Length < 3)
            {
                yield break; // Skip if the target name is too short
            }

            // Ensure all tokens in the target package are uncommon
            var targetTokens = segmented;
            // Console.WriteLine("targetTokens: " + string.Join(", ", targetTokens));
            if (!targetTokens.All(IsUncommonToken))
            {
                // Console.WriteLine("targetTokens.All(IsUncommonToken): " + targetTokens.All(IsUncommonToken));
                yield break; // Skip if not all tokens are uncommon
            }

            // Generate confuser package names by adding prefixes and suffixes
            var confuserNames = GenerateConfuserNames(targetName);

            foreach (var confuser in confuserNames)
            {
                // Console.WriteLine("confuser: " + confuser);
                if (IsValidConfuser(confuser, targetName))
                {
                    yield return new Mutation(
                        mutated: confuser,
                        original: targetName,
                        mutator: Kind,
                        reason: $" Mutated package with prefix/suffix generated: {confuser}");
                }
            }
        }

        private bool IsUncommonToken(string token)
        {
            // Console.WriteLine("token: " + token);
            var found = PKG_TOKEN_TO_RANK.TryGetValue(token, out var rank);
            // if (found)
            // {
            //     Console.WriteLine($"Token '{token}' found with rank: {rank}");
            // }
            // else
            // {
            //     Console.WriteLine($"Token '{token}' not found.");
            // }
            return found && rank < PopularityThreshold;
        }

        private IEnumerable<string> GenerateConfuserNames(string targetName)
        {
            // Load all tokens from token data
            var tokens = PKG_TOKEN_TO_RANK.Keys;

            // Add prefixes
            foreach (var prefix in tokens)
            {
                // Console.WriteLine("prefix: " + prefix);
                yield return prefix + "-" + targetName;
            }

            // Add suffixes
            foreach (var suffix in tokens)
            {
                // Console.WriteLine("suffix: " + suffix);
                yield return targetName + "-" + suffix;
            }

            foreach (var prefix in tokens)
            {
                // Console.WriteLine("prefix: " + prefix);
                yield return prefix + targetName;
            }

            // Add suffixes without hyphen
            foreach (var suffix in tokens)
            {
                // Console.WriteLine("suffix: " + suffix);
                yield return targetName + suffix;
            }
        }

        private bool IsValidConfuser(string confuser, string targetName)
        {
            string normalizedConfuser = confuser.Replace("_", "");
            string normalizedTarget = targetName.Replace("_", "");

            bool startsWithOrEndsWith = confuser.StartsWith(targetName) || confuser.EndsWith(targetName);

            // Ensure the confuser contains the target as a substring
            if (confuser.Contains(targetName) && normalizedConfuser.Length > normalizedTarget.Length
            && normalizedConfuser.Length <= 2 * normalizedTarget.Length)
            {
                if (startsWithOrEndsWith)
                {
                    return true;
                }
                return false;
            }

            return false;
        }
    }
}