namespace Microsoft.CST.OpenSource.FindSquats.Mutators
{
    using Newtonsoft.Json;
    using PackageUrl;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;

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

        private const float PopularityThreshold = 0.15f; // Threshold for uncommon tokens
        private string JsonFilePath = "absolute_path_to_token_data"; // This line should be replaced with the token_data.json in data/toeknization folder

        public PackageTokenMutator()
        {
            LoadTokenData();
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
            if (string.IsNullOrEmpty(targetName) || targetName.Length <= 3)
            {
                yield break; // Skip if the target name is too short
            }

            // Ensure all tokens in the target package are uncommon
            var targetTokens = targetName.Split('-');
            if (!targetTokens.All(IsUncommonToken))
            {
                yield break; // Skip if not all tokens are uncommon
            }

            // Generate confuser package names by adding prefixes and suffixes
            var confuserNames = GenerateConfuserNames(targetName);

            foreach (var confuser in confuserNames)
            {

                if (IsValidConfuser(confuser, targetName))
                {
                    yield return new Mutation(
                        mutated: confuser,
                        original: targetName,
                        mutator: Kind,
                        reason: $"Confuser package generated: {confuser}");
                }
            }
        }

        private bool IsUncommonToken(string token)
        {
            return PKG_TOKEN_TO_RANK.TryGetValue(token, out var rank) && rank < PopularityThreshold;
        }

        private IEnumerable<string> GenerateConfuserNames(string targetName)
        {
            // Load all tokens from token data
            var tokens = PKG_TOKEN_TO_RANK.Keys;

            // Add prefixes
            foreach (var prefix in tokens)
            {
                
                yield return prefix + "-" + targetName;
            }

            // Add suffixes
            foreach (var suffix in tokens)
            {
                yield return targetName + "-" + suffix;
            }

            foreach (var prefix in tokens)
            {
                yield return prefix + targetName;
            }

            // Add suffixes without hyphen
            foreach (var suffix in tokens)
            {
                yield return targetName + suffix;
            }
        }

        private bool IsValidConfuser(string confuser, string target)
        {



            // Ensure the confuser contains the target as a substring
            if (!confuser.Contains(target))
            {
                return false;
            }

            // Ensure the length of both the confuser and target names is greater than 3
            if (confuser.Length <= 3 || target.Length <= 3)
            {
                return false;
            }

            // Ensure the confuser's length is less than twice the target's length
            if (confuser.Length >= 2 * target.Length)
            {
                return false;
            }

            return true;
        }
    }
}