# Anakin-Analytics

![Build Status](https://github.com/cccsdh/Anakin-Analytics/actions/workflows/ci.yml/badge.svg?branch=master)

Anakin-Analytics is a fork of `codingupastorm/vadersharp`, updated to target .NET 10 and extended to support multiple language sentiment configuration files simultaneously.

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
