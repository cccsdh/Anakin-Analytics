using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Reflection;

namespace AnakinAnalytics
{
    using System.IO;
    using System.Reflection;

    /// <summary>
    /// An abstraction to represent the sentiment intensity analyzer.
    /// </summary>
    public class SentimentIntensityAnalyzer
    {
        private const double ExclIncr = 0.292;
        private const double QuesIncrSmall = 0.18;
        private const double QuesIncrLarge = 0.96;

        // Lazy-loaded lexicon to avoid heavy work in a type initializer and to make failures explicit.
        private static readonly Lazy<Dictionary<string, double>> LexiconLazy =
            new Lazy<Dictionary<string, double>>(LoadLexicon, isThreadSafe: true);

        private static Dictionary<string, double> Lexicon => LexiconLazy.Value;

        private static Dictionary<string, double> LoadLexicon()
        {
            var assembly = typeof(SentimentIntensityAnalyzer).Assembly;

            // Find the actual resource name instead of assuming a constructed name. This is robust
            // to default namespace changes or file placement in folders.
            var resourceName = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("vader_lexicon.txt", StringComparison.OrdinalIgnoreCase));

            if (resourceName == null)
                throw new InvalidOperationException("Embedded resource 'vader_lexicon.txt' not found in assembly. Available resources: "
                    + string.Join(", ", assembly.GetManifestResourceNames()));

            using (var stream = assembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                    throw new InvalidOperationException($"Resource stream '{resourceName}' could not be opened.");

                var dic = new Dictionary<string, double>();
                using (var reader = new StreamReader(stream))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        var trimmed = line.Trim();
                        if (string.IsNullOrEmpty(trimmed))
                            continue;

                        var parts = trimmed.Split('\t');
                        if (parts.Length < 2)
                            continue;

                        // parse using invariant culture to avoid locale issues
                        if (double.TryParse(parts[1], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var val))
                        {
                            if (!dic.ContainsKey(parts[0]))
                                dic.Add(parts[0], val);
                        }
                    }
                }

                return dic;
            }
        }

        /// <summary>
        /// Return metrics for positive, negative and neutral sentiment based on the input text.
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        public SentimentAnalysisResults PolarityScores(string input, string languageCode = "en-gb")
        {
            // apply language-specific config
            var cfg = ConfigStore.CreateConfig(languageCode);
            SentimentUtils.ApplyConfig(cfg);
            // create resources based on config (holds booster dicts, idioms and helpers)
            var resources = new SentimentResources(cfg);

            SentiText sentiText = new SentiText(input);
            IList<double> sentiments = new List<double>();
            IList<string> wordsAndEmoticons = sentiText.WordsAndEmoticons;

            for (int i = 0; i < wordsAndEmoticons.Count; i++)
            {
                string item = wordsAndEmoticons[i];
                double valence = 0;
                if ((i < wordsAndEmoticons.Count - 1 && item.ToLower() == "kind" && wordsAndEmoticons[i + 1] == "of")
                    || resources.BoosterDict.ContainsKey(item.ToLower()))
                {
                    sentiments.Add(valence);
                    continue;
                }
                sentiments = SentimentValence(valence, sentiText, item, i, sentiments, resources);
            }

            sentiments = ButCheck(wordsAndEmoticons, sentiments);

            return ScoreValence(sentiments, input);
        }

        private IList<double> SentimentValence(double valence, SentiText sentiText, string item, int i, IList<double> sentiments, SentimentResources resources)
        {
            string itemLowerCase = item.ToLower();
            if (!Lexicon.ContainsKey(itemLowerCase))
            {
                sentiments.Add(valence);
                return sentiments;
            }
            bool isCapDiff = sentiText.IsCapDifferential;
            IList<string> wordsAndEmoticons = sentiText.WordsAndEmoticons;
            valence = Lexicon[itemLowerCase];
            if (isCapDiff && item.IsUpper())
            {
                if (valence > 0)
                {
                    valence += resources.CIncr;
                }
                else
                {
                    valence -= resources.CIncr;
                }
            }

            for (int startI = 0; startI < 3; startI++)
            {
                if (i > startI && !Lexicon.ContainsKey(wordsAndEmoticons[i - (startI + 1)].ToLower()))
                {
                    double s = resources.ScalarIncDec(wordsAndEmoticons[i - (startI + 1)], valence, isCapDiff);
                    if (startI == 1 && s != 0)
                        s = s * 0.95;
                    if (startI == 2 && s != 0)
                        s = s * 0.9;
                    valence = valence + s;

                    valence = NeverCheck(valence, wordsAndEmoticons, startI, i, resources);

                    if (startI == 2)
                    {
                        valence = IdiomsCheck(valence, wordsAndEmoticons, i, resources);
                    }

                }
            }

            valence = LeastCheck(valence, wordsAndEmoticons, i, resources);
            sentiments.Add(valence);
            return sentiments;
        }

        private IList<double> ButCheck(IList<string> wordsAndEmoticons, IList<double> sentiments)
        {
            bool containsBUT = wordsAndEmoticons.Contains("BUT");
            bool containsbut = wordsAndEmoticons.Contains("but");
            if (!containsBUT && !containsbut)
                return sentiments;

            int butIndex = (containsBUT) 
                ? wordsAndEmoticons.IndexOf("BUT") 
                : wordsAndEmoticons.IndexOf("but");

           for (int i = 0; i < sentiments.Count; i++)
            {
                double sentiment = sentiments[i];
                if (i < butIndex)
                {
                    sentiments.RemoveAt(i);
                    sentiments.Insert(i,sentiment*0.5);
                }
                else if (i > butIndex)
                {
                    sentiments.RemoveAt(i);
                    sentiments.Insert(i, sentiment * 1.5);
                }
            }
            return sentiments;
        }

        private double LeastCheck(double valence, IList<string> wordsAndEmoticons, int i, SentimentResources resources)
        {
            if (i > 1 && !Lexicon.ContainsKey(wordsAndEmoticons[i - 1].ToLower()) &&
                wordsAndEmoticons[i - 1].ToLower() == "least")
            {
                if (wordsAndEmoticons[i - 2].ToLower() != "at" && wordsAndEmoticons[i - 2].ToLower() != "very")
                {
                    valence = valence * resources.NScalar;
                }
            }
            else if (i > 0 && !Lexicon.ContainsKey(wordsAndEmoticons[i-1].ToLower()) 
                && wordsAndEmoticons[i - 1].ToLower() == "least")
            {
                valence = valence * resources.NScalar;
            }

            return valence;
        }

        private double NeverCheck(double valence, IList<string> wordsAndEmoticons, int startI, int i, SentimentResources resources)
        {
            if (startI == 0)
            {
                if (resources.Negated(new List<string> {wordsAndEmoticons[i - 1]}))
                    valence = valence * resources.NScalar;
            }
            if (startI == 1)
            {
                if (wordsAndEmoticons[i - 2] == "never" &&
                    (wordsAndEmoticons[i - 1] == "so" || wordsAndEmoticons[i - 1] == "this"))
                {
                    valence = valence * 1.5;
                }
                else if (resources.Negated(new List<string> {wordsAndEmoticons[i - (startI + 1)]}))
                {
                    valence = valence * resources.NScalar;
                }
            }
            if (startI == 2)
            {
                if (wordsAndEmoticons[i - 3] == "never"
                    && (wordsAndEmoticons[i - 2] == "so" || wordsAndEmoticons[i - 2] == "this")
                    || (wordsAndEmoticons[i - 1] == "so" || wordsAndEmoticons[i - 1] == "this"))
                {
                    valence = valence * 1.25;
                }
                else if (resources.Negated(new List<string> { wordsAndEmoticons[i - (startI + 1)] }))
                {
                    valence = valence * resources.NScalar;
                }
            }

            return valence;
        }

        private double IdiomsCheck(double valence, IList<string> wordsAndEmoticons, int i, SentimentResources resources)
        {
            var oneZero = string.Concat(wordsAndEmoticons[i - 1], " ", wordsAndEmoticons[i]);
            var twoOneZero = string.Concat(wordsAndEmoticons[i - 2], " ", wordsAndEmoticons[i - 1], " ", wordsAndEmoticons[i]);
            var twoOne = string.Concat(wordsAndEmoticons[i - 2], " ", wordsAndEmoticons[i - 1]);
            var threeTwoOne = string.Concat(wordsAndEmoticons[i - 3], " ", wordsAndEmoticons[i - 2], " ", wordsAndEmoticons[i - 1]);
            var threeTwo = string.Concat(wordsAndEmoticons[i - 3], " ", wordsAndEmoticons[i - 2]);
            
            string[] sequences = {oneZero, twoOneZero, twoOne, threeTwoOne, threeTwo};

            foreach (var seq in sequences)
            {
                if (resources.SpecialCaseIdioms.ContainsKey(seq))
                {
                    valence = resources.SpecialCaseIdioms[seq];
                    break;
                }
            }

            if (wordsAndEmoticons.Count - 1 > i)
            {
                string zeroOne = string.Concat(wordsAndEmoticons[i], " ", wordsAndEmoticons[i + 1]);
                if (resources.SpecialCaseIdioms.ContainsKey(zeroOne))
                {
                    valence = resources.SpecialCaseIdioms[zeroOne];
                }
            }
            if (wordsAndEmoticons.Count - 1 > i + 1)
            {
                string zeroOneTwo = string.Concat(wordsAndEmoticons[i], " ", wordsAndEmoticons[i + 1], " ", wordsAndEmoticons[i + 2]);
                if (resources.SpecialCaseIdioms.ContainsKey(zeroOneTwo))
                {
                    valence = resources.SpecialCaseIdioms[zeroOneTwo];
                }
            }
            if (resources.BoosterDict.ContainsKey(threeTwo) || resources.BoosterDict.ContainsKey(twoOne))
            {
                valence += resources.BDecr;
            }
            return valence;
        }

        private double PunctuationEmphasis(string text)
        {
            return AmplifyExclamation(text) + AmplifyQuestion(text);
        }

        private double AmplifyExclamation(string text)
        {
            int epCount = text.Count(x => x == '!');

            if (epCount > 4)
                epCount = 4;

            return epCount * ExclIncr;
        }

        private static double AmplifyQuestion(string text)
        {
            int qmCount = text.Count(x => x == '?');

            if (qmCount < 1)
                return 0;

            if (qmCount <= 3)
                return qmCount * QuesIncrSmall;

            return QuesIncrLarge;
        }

        private static SiftSentiments SiftSentimentScores(IList<double> sentiments)
        {
            SiftSentiments siftSentiments = new SiftSentiments();

            foreach (var sentiment in sentiments)
            {
                if (sentiment > 0)
                    siftSentiments.PosSum += (sentiment + 1); //1 compensates for neutrals

                if (sentiment < 0)
                    siftSentiments.NegSum += (sentiment - 1);

                if (sentiment == 0)
                    siftSentiments.NeuCount++;
            }
            return siftSentiments;
        }

        private SentimentAnalysisResults ScoreValence(IList<double> sentiments, string text)
        {
            if (sentiments.Count == 0)
                return new SentimentAnalysisResults(); //will return with all 0

            double sum = sentiments.Sum();
            double puncAmplifier = PunctuationEmphasis(text);

            sum += Math.Sign(sum) * puncAmplifier;
            
            double compound = SentimentUtils.Normalize(sum);
            SiftSentiments sifted = SiftSentimentScores(sentiments);

            if (sifted.PosSum > Math.Abs(sifted.NegSum))
            {
                sifted.PosSum += puncAmplifier;
            }
            else if (sifted.PosSum < Math.Abs(sifted.NegSum))
            {
                sifted.NegSum -= puncAmplifier;
            }

            double total = sifted.PosSum + Math.Abs(sifted.NegSum) + sifted.NeuCount;
            return new SentimentAnalysisResults
            {
                Compound = Math.Round(compound,4),
                Positive = Math.Round(Math.Abs(sifted.PosSum /total), 3),
                Negative = Math.Round(Math.Abs(sifted.NegSum/total),3),
                Neutral = Math.Round(Math.Abs(sifted.NeuCount/total), 3)
            };
        }

        private class SiftSentiments
        {
            public double PosSum { get; set; }
            public double NegSum { get; set; }
            public int NeuCount { get; set; }
        }
    
    }

}
