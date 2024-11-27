// Copyright (c) Microsoft Corporation. Licensed under the MIT License.

namespace Microsoft.CST.OpenSource.FindSquats.Mutators
{
    using Helpers;
    using System.Collections.Generic;
    using System.Linq;
    using System;
    using System.IO;
    using Newtonsoft.Json;

    /// <summary>
    /// Generates mutations for if a suffix was added to, or removed from the string.
    /// By default, we check for these prefixes: .
    /// </summary>
    public class PackageTokenMutator : IMutator
    {
        public MutatorType Kind { get; } = MutatorType.PackageToken;
        private List<string> _tokens = new() {};
        private Dictionary<string, float> PKG_TOKEN_TO_RANK;
        private const float PopularityThreshold = 0.15f; // Hardcoded popularity threshold
        private const string JsonFilePath = "../token_data.json"; // Hardcoded JSON file path

        public PrefixSuffixAugmentation()
        {
            // Load PKG_TOKEN_TO_RANK from the JSON file
            var jsonData = File.ReadAllText(JsonFilePath);
            var tokenData = JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonData);
            PKG_TOKEN_TO_RANK = JsonConvert.DeserializeObject<Dictionary<string, float>>(tokenData["PKG_TOKEN_TO_RANK"].ToString());
        }

        public int Detect(string basePkg, string adversarialPkg)
        {
            // Check for minimum length
            if (basePkg.Length < 3 || adversarialPkg.Length < 3) return 0;

            // Normalize by removing underscores
            string normalizedBase = basePkg.Replace("_", "");
            string normalizedAdversarial = adversarialPkg.Replace("_", "");

            // Check prefix/suffix conditions
            if (adversarialPkg.Contains(basePkg) &&
                normalizedAdversarial.Length > normalizedBase.Length &&
                normalizedAdversarial.Length <= normalizedBase.Length * 2)
            {
                bool startsWithOrEndsWith = adversarialPkg.StartsWith(basePkg) || adversarialPkg.EndsWith(basePkg);

                if (startsWithOrEndsWith && IsPopularityBelowThreshold(basePkg))
                {
                    return 1;
                }
            }
            return 0;
        }

        // private bool IsPopularityBelowThreshold(string basePkg)
        // {
        //     // Convert base package into tokens (simulated here by splitting by non-alphanumeric characters)
        //     var tokens = basePkg.Split(new[] { '_', '-', '.' }, StringSplitOptions.RemoveEmptyEntries);

        //     // Check if all tokens have popularity below the threshold
        //     return tokens.All(token =>
        //         PKG_TOKEN_TO_RANK.ContainsKey(token) ? PKG_TOKEN_TO_RANK[token] < PopularityThreshold : true);
        // }
        // /// <summary>
        // /// Initializes a <see cref="PackageTokenMutator"/> instance.
        // /// Optionally takes in a additional Tokens, or a list of overriding Tokens to replace the default list with.
        // /// </summary>
        // /// <param name="additionalTokens">An optional parameter for extra Tokens.</param>
        // /// <param name="overrideTokens">An optional parameter for list of Tokens to replace the default list with.</param>
        public PackageTokenMutator(string[]? additionalTokens = null, string[]? overrideTokens = null, string[]? skipTokens = null)
        {
            if (overrideTokens != null)
            {
                _tokens = overrideTokens.ToList();
            }
            if (additionalTokens != null)
            {
                _tokens.AddRange(additionalTokens);
            }
            if (skipTokens != null)
            {
                _tokens.RemoveAll(skipTokens.Contains);
            }
        }
        
        public IEnumerable<Mutation> Generate(string arg)
        { 
            var addedTokens = _tokens.Select(s => new Mutation(
                    mutated: string.Concat(arg, s),
                    original: arg,
                    mutator: Kind,
                    reason: $"Token Added: {s}"));
            
            var removedTokens = _tokens.Where(arg.EndsWith).Select(s => new Mutation(
                mutated: arg.ReplaceAtEnd(s, string.Empty),
                original: arg,
                mutator: Kind,
                reason: $"Token Removed: {s}"));

            return addedTokens.Concat(removedTokens);
        }
    }
}