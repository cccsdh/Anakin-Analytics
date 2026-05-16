# Anakin-Analytics

![Build Status](https://github.com/cccsdh/Anakin-Analytics/actions/workflows/ci.yml/badge.svg?branch=master)

Anakin-Analytics is a fork of "VADER (Valence Aware Dictionary and sEntiment Reasoner) is a lexicon and rule-based sentiment analysis tool that is specifically attuned to sentiments expressed in social media."

Previously VADER was only available in python (https://github.com/cjhutto/vaderSentiment), and was then ported to C# in https://github.com/codingupastorm/vadersharp (this is a fork from these repos).

## Citation Information ([source](https://github.com/cjhutto/vaderSentiment#citation-information))
If you use either the dataset or any of the VADER sentiment analysis tools (VADER sentiment lexicon or Python code for rule-based sentiment analysis engine) in your research, please cite the above paper. For example:  

>  **Hutto, C.J. & Gilbert, E.E. (2014). VADER: A Parsimonious Rule-based Model for Sentiment Analysis of Social Media Text. Eighth International Conference on Weblogs and Social Media (ICWSM-14). Ann Arbor, MI, June 2014.** 

## Changes since [original port](https://github.com/cjhutto/vaderSentiment) to C#
- Implement Multiple Lexicon language support
- Implement Multiple ConfigStore support by language

## ToDo:
- Update to the latest Python version support

# Getting Started

Anakin-Analytics provides a .NET 10-ready implementation of the VADER sentiment analysis algorithm and adds support for loading and using multiple language sentiment configuration files at runtime.

Supported highlights:

- Targets .NET 10
- Load and use multiple language sentiment config files simultaneously
- Lexicon and rule-based sentiment analysis tuned for social media text
- Cross-platform (.NET 10)

# Usage

Import the library namespace and create a `SentimentIntensityAnalyzer` instance, then call `PolarityScores`:

```c#
using AnakinAnalytics;

var analyzer = new SentimentIntensityAnalyzer();
var results = analyzer.PolarityScores("Wow, this package is amazingly easy to use");

Console.WriteLine("Positive score: " + results.Positive);
Console.WriteLine("Negative score: " + results.Negative);
Console.WriteLine("Neutral score: " + results.Neutral);
Console.WriteLine("Compound score: " + results.Compound);
