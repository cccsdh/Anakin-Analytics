using System;
using System.Collections.Generic;
using System.Linq;

namespace AnakinAnalytics
{
    internal class SentimentResources
    {
        public string[] Negations { get; }
        public Dictionary<string, double> BoosterDict { get; }
        public Dictionary<string, double> SpecialCaseIdioms { get; }

        // expose constants from SentimentUtils
        public double CIncr => SentimentUtils.CIncr;
        public double NScalar => SentimentUtils.NScalar;
        public double BDecr => SentimentUtils.BDecr;

        public SentimentResources(ConfigStore cfg)
        {
            if (cfg != null)
            {
                Negations = cfg.Negations ?? SentimentUtils.Negate;
                BoosterDict = cfg.BoosterDict != null
                    ? new Dictionary<string, double>(cfg.BoosterDict, StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, double>(SentimentUtils.BoosterDict, StringComparer.OrdinalIgnoreCase);
                SpecialCaseIdioms = cfg.SpecialCaseIdioms != null
                    ? new Dictionary<string, double>(cfg.SpecialCaseIdioms, StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, double>(SentimentUtils.SpecialCaseIdioms, StringComparer.OrdinalIgnoreCase);
            }
            else
            {
                Negations = SentimentUtils.Negate;
                BoosterDict = new Dictionary<string, double>(SentimentUtils.BoosterDict, StringComparer.OrdinalIgnoreCase);
                SpecialCaseIdioms = new Dictionary<string, double>(SentimentUtils.SpecialCaseIdioms, StringComparer.OrdinalIgnoreCase);
            }
        }

        public bool Negated(IList<string> inputWords, bool includenT = true)
        {
            if (Negations != null)
            {
                foreach (var word in Negations)
                {
                    if (inputWords.Contains(word))
                        return true;
                }
            }

            if (includenT)
            {
                foreach (var word in inputWords)
                {
                    if (word.Contains("n't"))
                        return true;
                }
            }

            if (inputWords.Contains("least"))
            {
                int i = inputWords.IndexOf("least");
                if (i > 0 && inputWords[i - 1] != "at")
                    return true;
            }

            return false;
        }

        public double ScalarIncDec(string word, double valence, bool isCapDiff)
        {
            var wordLower = word.ToLower();
            if (!BoosterDict.ContainsKey(wordLower))
                return 0.0;

            double scalar = BoosterDict[wordLower];
            if (valence < 0)
                scalar *= -1;

            if (word.IsUpper() && isCapDiff)
            {
                scalar += (valence > 0) ? CIncr : -CIncr;
            }

            return scalar;
        }
    }
}
