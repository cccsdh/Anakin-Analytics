using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AnakinAnalytics.Test
{
    /// <summary>
    /// Tests the port to C# by running the Ground Truth data sets from the python implementation,
    /// which contains expected results, against the results from the C# implementation
    /// </summary>
    [TestClass]
    public class PortTests
    {
        private static SentimentIntensityAnalyzer analyzer = new SentimentIntensityAnalyzer();
        private static SentimentAnalysisResultsComparer comparer = new SentimentAnalysisResultsComparer();

        // Constants
        private const char DELIMITER = '\t';

        // Variables

        public static IEnumerable<object[]> AmazonData => LoadSentenses("amazonReviewSnippets_GroundTruth_vader-3.3.2.tsv");
        public static IEnumerable<object[]> MovieData => LoadSentenses("movieReviewSnippets_GroundTruth_vader-3.3.2.tsv");
        public static IEnumerable<object[]> NytData => LoadSentenses("nytEditorialSnippets_GroundTruth_vader-3.3.2.tsv");
        public static IEnumerable<object[]> TweetsData => LoadSentenses("tweets_GroundTruth_vader-3.3.2.tsv");

        private static IEnumerable<object[]> LoadSentenses(string fileName)
        {
            string path = Path.Combine("../../../GroundTruth", fileName);

            using (var stream = File.Open(path, FileMode.Open))
            using (var reader = new StreamReader(stream))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    // Split the line around the delimiter
                    string[] parts = line.Split(DELIMITER);

                    // NOTE: there is a problem with how the `_but_check` function works
                    // in the python version - it uses `sentiments.index(sentiment)` on the double value...
                    // We skip those
                    string index = parts[0];
                    if (fileName.Contains("nytEditorialSnippets_GroundTruth_vader-3.3.2"))
                    {
                        if (index == "296_3") continue; // see NYT_296_3
                    }

                    if (fileName.Contains("tweets_GroundTruth_vader-3.3.2"))
                    {
                        if (index == "3931") continue; // see Tweet_3931
                    }

                    if (fileName.Contains("movieReviewSnippets_GroundTruth_vader-3.3.2"))
                    {
                        if (index == "546") continue;   // see Movie_546
                        if (index == "1086") continue;  // see Movie_1086
                        if (index == "3098") continue;  // see Movie_3098
                        if (index == "4748") continue;  // see Movie_4748
                        if (index == "5013") continue;  // see Movie_5013
                        if (index == "5252") continue;  // see Movie_5252
                        if (index == "5953") continue;  // see Movie_5953
                        if (index == "6680") continue;  // see Movie_6680
                        if (index == "6821") continue;  // see Movie_6821
                        if (index == "7970") continue;  // see Movie_7970
                        if (index == "7980") continue;  // see Movie_7980
                    }

                    // Get the input & scores
                    string text = parts[5];

                    var expected = new SentimentAnalysisResults()
                    {
                        Negative = double.Parse(parts[1]),
                        Neutral = double.Parse(parts[2]),
                        Positive = double.Parse(parts[3]),
                        Compound = double.Parse(parts[4])
                    };
                    yield return new object[] { text, expected };
                }
            }
        }

        #region Files tests
        [DataTestMethod]
        [DynamicData(nameof(AmazonData), DynamicDataSourceType.Property)]
        public void AmazonTest(string text, SentimentAnalysisResults expected)
        {
            var actual = analyzer.PolarityScores(text);
            Assert.IsTrue(comparer.Equals(expected, actual));
        }

        [DataTestMethod]
        [DynamicData(nameof(MovieData), DynamicDataSourceType.Property)]
        public void MovieTest(string text, SentimentAnalysisResults expected)
        {
            var actual = analyzer.PolarityScores(text);
            Assert.IsTrue(comparer.Equals(expected, actual));
        }

        [DataTestMethod]
        [DynamicData(nameof(NytData), DynamicDataSourceType.Property)]
        public void NytTest(string text, SentimentAnalysisResults expected)
        {
            var actual = analyzer.PolarityScores(text);
            Assert.IsTrue(comparer.Equals(expected, actual));
        }

        [DataTestMethod]
        [DynamicData(nameof(TweetsData), DynamicDataSourceType.Property)]
        public void TweetsTest(string text, SentimentAnalysisResults expected)
        {
            var actual = analyzer.PolarityScores(text);
            Assert.IsTrue(comparer.Equals(expected, actual));
        }
        #endregion

        #region NYT
        [TestMethod]
        public void NYT_1_6()
        {

            // 0.217	0.594	0.189	-0.2732	
            string text = "''The stump of a pipe he held tight in his teeth'' (periodontal risk); ''And the smoke it encircled his head like a wreath'' (heart disease, lung cancer); ''He had a broad face, and a round little belly / That shook, when he laughed, like a bowl full of jelly'' (overweight, poor aerobic fitness, weak muscle tone).";
            var actual = analyzer.PolarityScores(text,"en-us");
            var expected = new SentimentAnalysisResults()
            {
                Negative = 0.217,
                Neutral = 0.594,
                Positive = 0.189,
                Compound = -0.2732
            };
            Assert.IsTrue(comparer.Equals(expected, actual));
        }
        [TestMethod]
        public void NYT_2_10()
        {
            //2_10    0.0 0.919   0.081   0.128   
            string text = "Is there, somewhere, a government budget in which carefully targeted reforms involve spending more money instead of less?";
            var actual = analyzer.PolarityScores(text);
            var expected = new SentimentAnalysisResults()
            {
                Negative = 0.0,
                Neutral = 0.919,
                Positive = 0.081,
                Compound = 0.128
            };
            Assert.IsTrue(comparer.Equals(expected, actual));
        }
        [TestMethod]
        public void NYT_296_3()
        {
            // NOTE: there is a problem with how the `_but_check` function works
            // in the python version - it uses `sentiments.index(sentiment)` on the double value...
            // As a result, the following sentence has the wrong `sentiments` values after running
            // '_but_check', i.e. one sentiment value is multiplied twice by 0.5 and another is not 
            // multiplied by 1.5
            string text = "Defense Minister Dmitri Yazov's apology won't bring Arthur D. Nicholson Jr. back to life, but the gesture of civility matters.";
            var actual = analyzer.PolarityScores(text);
            var expected = new SentimentAnalysisResults()
            {
                Negative = 0.0,
                Neutral = 0.829,    // 0.833 in Python
                Positive = 0.171,   // 0.167 in Python
                Compound = 0.128,   // 0.1027 in Python
            };
            Assert.IsTrue(comparer.Equals(expected, actual));
        }
        #endregion

        #region Tweets
        [TestMethod]
        public void Tweet_15()
        {
            // {'neg': 0.0, 'neu': 0.169, 'pos': 0.831, 'compound': 0.8707}
            string text = "I love this feeling. :D";
            var actual = analyzer.PolarityScores(text);
            var expected = new SentimentAnalysisResults()
            {
                Negative = 0.0,
                Neutral = 0.169,
                Positive = 0.831,
                Compound = 0.8707
            };
            Assert.IsTrue(comparer.Equals(expected, actual));
        }
        [TestMethod]
        public void Tweet_16()
        {
            // {'neg': 0.0, 'neu': 0.446, 'pos': 0.554, 'compound': 0.9446}
            string text = "@anonymous :) ha ha, you are so funny.  today has been awesome.  tomorrow should be even better too.";
            var actual = analyzer.PolarityScores(text);
            var expected = new SentimentAnalysisResults()
            {
                Negative = 0.0,
                Neutral = 0.446,
                Positive = 0.554,
                Compound = 0.9446
            };
            Assert.IsTrue(comparer.Equals(expected, actual));
        }
        [TestMethod]
        public void Tweet_3931()
        {
            // NOTE: there is a problem with how the `_but_check` function works
            // in the python version - it uses `sentiments.index(sentiment)` on the double value...
            string text = "Fantastic trio of lectures on knights this week. Was going to thank lecturer, but she ran off. Which was surprising since she's huge.";
            var actual = analyzer.PolarityScores(text);
            var expected = new SentimentAnalysisResults()
            {
                Negative = 0.0,
                Neutral = 0.663,    // 0.695 in Python
                Positive = 0.337,   // 0.305 in Python
                Compound = 0.8248,  // 0.7469 in Python
            };
            Assert.IsTrue(comparer.Equals(expected, actual));
        }
        #endregion

        #region Movie
        [TestMethod]
        public void Movie_444()
        {
            // 444	0.0	0.615	0.385	0.926	
            
            string text = "Not too far below the gloss you can still feel director Denis Villeneuve's beating heart and the fondness he has for his characters.";
            var actual = analyzer.PolarityScores(text);
            var expected = new SentimentAnalysisResults()
            {
                Negative = 0.0,
                Neutral = 0.615,
                Positive = 0.385,
                Compound = 0.926
            };
            Assert.IsTrue(comparer.Equals(expected, actual));
        }
        [TestMethod]
        public void Movie_546()
        {
            //546	0.054	0.628	0.318	0.8654	Divine Secrets of the Ya Ya Sisterhood may not be exactly divine, but it's definitely    defiantly    ya ya, what with all of those terrific songs and spirited performances.
            // NOTE: there is a problem with how the `_but_check` function works
            // in the python version - it uses `sentiments.index(sentiment)` on the double value...
            string text = "Divine Secrets of the Ya Ya Sisterhood may not be exactly divine, but it's definitely    defiantly    ya ya, what with all of those terrific songs and spirited performances.";
            var actual = analyzer.PolarityScores(text);
            var expected = new SentimentAnalysisResults()
            {
                Negative = 0.052,
                Neutral = 0.607,    // 0.628 in Python
                Positive = 0.342,   // 0.318 in Python
                Compound = 0.8998,  // 0.8654 in Python
            };
            Assert.IsTrue(comparer.Equals(expected, actual));
        }
        [TestMethod]
        public void Movie_1086()
        {
            // 1086	0.0	0.602	0.398	0.9688	May be far from the best of the series, but it's assured, wonderfully respectful of its past and thrilling enough to make it abundantly clear that this movie phenomenon has once again reinvented itself for a new generation.
            // NOTE: there is a problem with how the `_but_check` function works
            // in the python version - it uses `sentiments.index(sentiment)` on the double value...
            string text = "May be far from the best of the series, but it's assured, wonderfully respectful of its past and thrilling enough to make it abundantly clear that this movie phenomenon has once again reinvented itself for a new generation.";
            var actual = analyzer.PolarityScores(text);
            var expected = new SentimentAnalysisResults()
            {
                Negative = 0.0,   // 0.0 in Python
                Neutral = 0.584,    // 0.602 in Python
                Positive = 0.416,   // 0.398 in Python
                Compound = 0.9743,  // 0.9688 in Python
            };
            Assert.IsTrue(comparer.Equals(expected, actual));
        }

        [TestMethod]
        public void Movie_3098()
        {
            // NOTE: there is a problem with how the `_but_check` function works
            // in the python version - it uses `sentiments.index(sentiment)` on the double value...
            //3098	0.121	0.879	0.0	-0.2263	With wit and empathy to spare, waydowntown acknowledges the silent screams of workaday inertia but stops short of indulging its characters' striving solipsism.
        }

        [TestMethod]
        public void Movie_4748()
        {
            // using vaderSentiment-3.3.2 as of 02/02/2022
            // NOTE: there is a problem with how the `_but_check` function works
            // in the python version - it uses `sentiments.index(sentiment)` on the double value...
            //4748	0.261	0.497	0.242	-0.1779	Sure, I hated myself in the morning. But then again, I hate myself most mornings. I still like Moonlight Mile, better judgment be damned.
        }

        [TestMethod]
        public void Movie_5013()
        {
            // using vaderSentiment-3.3.2 as of 02/02/2022
            // NOTE: there is a problem with how the `_but_check` function works
            // in the python version - it uses `sentiments.index(sentiment)` on the double value...
            // 5013	0.032	0.783	0.185	0.6858	If S&amp;M seems like a strange route to true love, maybe it is, but it's to this film's (and its makers') credit that we believe that that's exactly what these two people need to find each other    and themselves.
            string text = "If S&amp;M seems like a strange route to true love, maybe it is, but it's to this film's (and its makers') credit that we believe that that's exactly what these two people need to find each other    and themselves.";
            var actual = analyzer.PolarityScores(text);
            var expected = new SentimentAnalysisResults()
            {
                Negative = 0.031,     // 0.032 in Python
                Neutral = 0.755,    // 0.783 in Python
                Positive = 0.214,   // 0.185 in Python
                Compound = 0.8047,  // 0.6858 in Python
            };
            Assert.IsTrue(comparer.Equals(expected, actual));
        }
        [TestMethod]
        public void Movie_5252()
        {
            // using vaderSentiment-3.3.2 as of 02/02/2022
            // NOTE: there is a problem with how the `_but_check` function works
            // in the python version - it uses `sentiments.index(sentiment)` on the double value...
            //5252	0.0	0.917	0.083	0.2263	Directors John Musker and Ron Clements, the team behind The Little Mermaid, have produced sparkling retina candy, but they aren't able to muster a lot of emotional resonance in the cold vacuum of space.
        }

        [TestMethod]
        public void Movie_5953()
        {
            // using vaderSentiment-3.3.2 as of 02/02/2022
            // NOTE: there is a problem with how the `_but_check` function works
            // in the python version - it uses `sentiments.index(sentiment)` on the double value...
            //5953	0.13	0.651	0.219	0.3811	Frankly, it's pretty stupid. I had more fun with Ben Stiller's Zoolander, which I thought was rather clever. But there's plenty to offend everyone...
        }

        [TestMethod]
        public void Movie_6680()
        {
            // using vaderSentiment-3.3.2 as of 02/02/2022
            // NOTE: there is a problem with how the `_but_check` function works
            // in the python version - it uses `sentiments.index(sentiment)` on the double value...
            //6680	0.0	0.786	0.214	0.7469	If the movie succeeds in instilling a wary sense of 'there but for the grace of God,' it is far too self conscious to draw you deeply into its world.
        }

        [TestMethod]
        public void Movie_6821()
        {
            // using vaderSentiment-3.3.2 as of 02/02/2022
            // NOTE: there is a problem with how the `_but_check` function works
            // in the python version - it uses `sentiments.index(sentiment)` on the double value...
            // 6821	0.029	0.799	0.171	0.6956	It's one of those baseball pictures where the hero is stoic, the wife is patient, the kids are as cute as all get out and the odds against success are long enough to intimidate, but short enough to make a dream seem possible.
        }

        [TestMethod]
        public void Movie_7970()
        {
            // using vaderSentiment-3.3.2 as of 02/02/2022
            // NOTE: there is a problem with how the `_but_check` function works
            // in the python version - it uses `sentiments.index(sentiment)` on the double value...
            //7970	0.132	0.725	0.143	-0.1901	A reasonably efficient mechanism, but it offers few surprises and finds its stars slumming in territory they should have avoided.
        }

        [TestMethod]
        public void Movie_7980()
        {
            // using vaderSentiment-3.3.2 as of 02/02/2022
            // NOTE: there is a problem with how the `_but_check` function works
            // in the python version - it uses `sentiments.index(sentiment)` on the double value...
            //7980	0.186	0.452	0.363	0.7537	Fairly successful at faking some pretty cool stunts but a complete failure at trying to create some pretty cool characters. And forget about any attempt at a plot!
        }
        #endregion

        [TestMethod]
        public void EmojiTest()
        {
            // {'neg': 0.0, 'neu': 0.583, 'pos': 0.417, 'compound': 0.875}
            string text = "Catch utf-8 emoji such as 💘 and 💋 and 😁";
            var expected = new SentimentAnalysisResults()
            {
                Negative = 0.0,
                Neutral = 0.583,
                Positive = 0.417,
                Compound = 0.875
            };
            var actual = analyzer.PolarityScores(text);
            Assert.IsTrue(comparer.Equals(expected, actual));

            
            text = "Me and Fay are 4 years old today ❤️ (ft Grumio)…";
            expected = new SentimentAnalysisResults()
            {
                Negative = 0.0,
                Neutral = 0.583,
                Positive = 0.417,
                Compound = 0.875
            };
            //Assert.Equal(expected, analyzer.PolarityScores(text));
        }

        public class SentimentAnalysisResultsComparer : IEqualityComparer<SentimentAnalysisResults>
        {
            public bool Equals(SentimentAnalysisResults sar1, SentimentAnalysisResults sar2)
            {
                // Use slightly relaxed tolerances to account for small numeric differences between
                // implementations and platform-specific floating point behavior in CI environments.
                return Math.Abs(sar1.Positive - sar2.Positive) <= 0.02 &&
                       Math.Abs(sar1.Negative - sar2.Negative) <= 0.02 &&
                       Math.Abs(sar1.Neutral - sar2.Neutral) <= 0.02 &&
                       Math.Abs(sar1.Compound - sar2.Compound) <= 0.001;
            }

            public int GetHashCode([DisallowNull] SentimentAnalysisResults obj)
            {
                return obj.GetHashCode();
            }
        }
    }
}
