using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using AnakinAnalytics;

namespace AnakinAnalytics.Test
{
    [TestClass]
    public class SentimentTest
    {
        [TestMethod]
        public void MatchPythonTest()
        {
            SentimentIntensityAnalyzer analyzer = new SentimentIntensityAnalyzer();

            var standardGoodTest = analyzer.PolarityScores("VADER is smart, handsome, and funny.");
            Assert.AreEqual(standardGoodTest.Negative, 0);
            Assert.AreEqual(standardGoodTest.Neutral, 0.254);
            Assert.AreEqual(standardGoodTest.Positive, 0.746);
            Assert.AreEqual(standardGoodTest.Compound, 0.8316);

            var kindOfTest = analyzer.PolarityScores("The book was kind of good.");
            Assert.AreEqual(kindOfTest.Negative, 0);
            Assert.AreEqual(kindOfTest.Neutral, 0.657);
            Assert.AreEqual(kindOfTest.Positive, 0.343);
            Assert.AreEqual(kindOfTest.Compound, 0.3832);

            var complexTest =
                analyzer.PolarityScores(
                    "The plot was good, but the characters are uncompelling and the dialog is not great.");
            Assert.AreEqual(complexTest.Negative, 0.327);
            Assert.AreEqual(complexTest.Neutral, 0.579);
            Assert.AreEqual(complexTest.Positive, 0.094);
            Assert.AreEqual(complexTest.Compound, -0.7042);

        }
        [TestMethod]
        public void VeryCleanNoProblem()
        {
            SentimentIntensityAnalyzer analyzer = new SentimentIntensityAnalyzer();

            var veryCleanTest = analyzer.PolarityScores("Very clean, no problem");
            Assert.AreEqual(veryCleanTest.Negative, 0);
            Assert.AreEqual(veryCleanTest.Neutral, 0.128);
            Assert.AreEqual(veryCleanTest.Positive, 0.872);
            Assert.AreEqual(veryCleanTest.Compound, 0.6997);
        }

        [TestMethod]
        public void UpperCaseTest()
        {
            SentimentIntensityAnalyzer analyzer = new SentimentIntensityAnalyzer();

            var notBadTest = analyzer.PolarityScores("not bad");
            Assert.AreEqual(notBadTest.Negative, 0);
            Assert.AreEqual(notBadTest.Neutral, 0.26);
            Assert.AreEqual(notBadTest.Positive, 0.74);
            Assert.AreEqual(notBadTest.Compound, 0.431);

            var NotBadTest = analyzer.PolarityScores("Not bad");
            Assert.AreEqual(NotBadTest.Negative, 0);
            Assert.AreEqual(NotBadTest.Neutral, 0.26);
            Assert.AreEqual(NotBadTest.Positive, 0.74);
            Assert.AreEqual(NotBadTest.Compound, 0.431);
        }
        [TestMethod]
        public void MatchPythonTestForUs()
        {
            SentimentIntensityAnalyzer analyzer = new SentimentIntensityAnalyzer();

            var standardGoodTest = analyzer.PolarityScores("VADER is smart, handsome, and funny.","en-us");
            Assert.AreEqual(standardGoodTest.Negative, 0);
            Assert.AreEqual(standardGoodTest.Neutral, 0.254);
            Assert.AreEqual(standardGoodTest.Positive, 0.746);
            Assert.AreEqual(standardGoodTest.Compound, 0.8316);

            var kindOfTest = analyzer.PolarityScores("The book was kind of good.");
            Assert.AreEqual(kindOfTest.Negative, 0);
            Assert.AreEqual(kindOfTest.Neutral, 0.657);
            Assert.AreEqual(kindOfTest.Positive, 0.343);
            Assert.AreEqual(kindOfTest.Compound, 0.3832);

            var complexTest =
                analyzer.PolarityScores(
                    "The plot was good, but the characters are uncompelling and the dialog is not great.");
            Assert.AreEqual(complexTest.Negative, 0.327);
            Assert.AreEqual(complexTest.Neutral, 0.579);
            Assert.AreEqual(complexTest.Positive, 0.094);
            Assert.AreEqual(complexTest.Compound, -0.7042);

        }

        [TestMethod]
        public void TestConfigStore()
        {
            ConfigStore cfg = ConfigStore.CreateConfig("en-gb");
            var negations = cfg.Negations;
            string[] Negate =
            {
            "aint", "arent", "cannot", "cant", "couldnt", "darent", "didnt", "doesnt",
            "ain't", "aren't", "can't", "couldn't", "daren't", "didn't", "doesn't",
            "dont", "hadnt", "hasnt", "havent", "isnt", "mightnt", "mustnt", "neither",
            "don't", "hadn't", "hasn't", "haven't", "isn't", "mightn't", "mustn't",
            "neednt", "needn't", "never", "none", "nope", "nor", "not", "nothing", "nowhere",
            "oughtnt", "shant", "shouldnt", "uhuh", "wasnt", "werent",
            "oughtn't", "shan't", "shouldn't", "uh-uh", "wasn't", "weren't",
            "without", "wont", "wouldnt", "won't", "wouldn't", "rarely", "seldom", "despite"
            };
            bool isExisting;
            Assert.AreEqual(negations.Length, Negate.Length);

            foreach (var a in negations)
            {
                isExisting = false;
                foreach (var b in Negate)
                {
                    if(a.Equals(b))
                    {
                        isExisting = true;
                        break;
                    }
                }
                Assert.IsTrue(isExisting);
            }
        }
    }
}
