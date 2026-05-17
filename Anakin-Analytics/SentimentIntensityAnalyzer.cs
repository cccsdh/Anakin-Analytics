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

        // Cache lexicons per language code. Keys are normalized language codes (lowercase).
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<Dictionary<string, double>>> LexiconCache
            = new System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<Dictionary<string, double>>>(System.StringComparer.OrdinalIgnoreCase);

        private static Dictionary<string, double> GetLexiconForLanguage(string languageCode)
        {
            if (string.IsNullOrEmpty(languageCode))
                languageCode = "en-gb";

            var normalized = languageCode.ToLowerInvariant();

            // Remove any existing cached entry so that if lexicon-loading logic
            // was updated (for example to normalize keys) we will reload the
            // lexicon rather than returning a previously cached dictionary that
            // may contain unnormalized keys.
            try
            {
                LexiconCache.TryRemove(normalized, out _);
            }
            catch
            {
                // ignore any concurrency issues
            }

            var lazy = LexiconCache.GetOrAdd(normalized, _ => new Lazy<Dictionary<string, double>>(() => LoadLexicon(normalized), isThreadSafe: true));
            var lex = lazy.Value;

            // If the loaded lexicon is empty, attempt an explicit file-based
            // lookup from the test host base directory and nearby subfolders.
            if (lex == null || lex.Count == 0)
            {
                try
                {
                    var assembly = typeof(SentimentIntensityAnalyzer).Assembly;
                    var assemblyFolder = Path.GetDirectoryName(assembly.Location) ?? AppDomain.CurrentDomain.BaseDirectory ?? Directory.GetCurrentDirectory();

                    var candidates = new List<string> { $"vader_lexicon_{normalized}.txt" };
                    var langPart = languageCode.Split('-')[0];
                    if (!string.Equals(langPart, languageCode, StringComparison.OrdinalIgnoreCase))
                        candidates.Add($"vader_lexicon_{langPart}.txt");
                    candidates.Add("vader_lexicon.txt");

                    foreach (var fileName in candidates)
                    {
                        string filePath = Path.Combine(assemblyFolder, fileName);
                        if (!File.Exists(filePath))
                        {
                            // search subfolders up to depth 3
                            var found = FindFileInSubfolders(assemblyFolder, fileName, maxDepth: 3);
                            if (!string.IsNullOrEmpty(found))
                                filePath = found;
                        }

                        if (File.Exists(filePath))
                        {
                            try
                            {
                                using (var stream = File.OpenRead(filePath))
                                {
                                    var loaded = LoadLexiconFromStream(stream);
                                    var transformed = TransformLexiconForUS(loaded, languageCode);
                                    try { MergeEmojiLexicon(transformed, assemblyFolder, assembly); } catch { }

                                    if (transformed != null && transformed.Count > 0)
                                    {
                                        // Update cache so subsequent calls use this lexicon
                                        LexiconCache[normalized] = new Lazy<Dictionary<string, double>>(() => transformed, isThreadSafe: true);
                                        return transformed;
                                    }
                                }
                            }
                            catch
                            {
                                // ignore and continue trying other candidates
                            }
                        }
                    }
                }
                catch
                {
                    // swallow failures and fall back to whatever we have
                }
            }

            return lex;
        }

        // Search for a file in immediate subdirectories (up to maxDepth levels). Returns full path if found; otherwise null.
        private static string FindFileInSubfolders(string startDir, string fileName, int maxDepth = 1)
        {
            try
            {
                if (maxDepth < 1)
                    return null;

                var dirs = new Queue<(string path, int depth)>();
                dirs.Enqueue((startDir, 0));

                while (dirs.Count > 0)
                {
                    var (path, depth) = dirs.Dequeue();
                    try
                    {
                        foreach (var file in Directory.GetFiles(path, fileName))
                        {
                            if (File.Exists(file))
                                return file;
                        }
                        if (depth < maxDepth)
                        {
                            foreach (var dir in Directory.GetDirectories(path))
                            {
                                dirs.Enqueue((dir, depth + 1));
                            }
                        }
                    }
                    catch
                    {
                        // ignore IO errors for individual dirs
                    }
                }
            }
            catch
            {
                // ignore
            }
            return null;
        }

        // Write a lexicon dictionary to a file in the canonical format (token \t score)
        private static void WriteLexiconToFile(Dictionary<string, double> lexicon, string targetFilePath)
        {
            using (var writer = new StreamWriter(targetFilePath, false, System.Text.Encoding.UTF8))
            {
                foreach (var kv in lexicon)
                {
                    writer.WriteLine(kv.Key + "\t" + kv.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }
            }
        }

        // Read all lines from a stream
        private static List<string> ReadLinesFromStream(Stream stream)
        {
            var lines = new List<string>();
            using (var reader = new StreamReader(stream))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    lines.Add(line);
                }
            }
            return lines;
        }

        // Load lexicon dictionary from pre-read lines (preserves parsing behavior of LoadLexiconFromStream)
        private static Dictionary<string, double> LoadLexiconFromLines(List<string> lines)
        {
            var dic = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in lines)
            {
                var trimmed = line?.Trim();
                if (string.IsNullOrEmpty(trimmed))
                    continue;

                var parts = trimmed.Split('\t');
                if (parts.Length < 2)
                    continue;

                // Normalize token by trimming surrounding whitespace and stray quotes so keys
                // in the lexicon do not include enclosing '"' or '\'' characters which can
                // appear in some transformed lexicon sources.
                var key = parts[0].Trim().Trim('"', '\'');

                if (double.TryParse(parts[1], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var val))
                {
                    if (!dic.ContainsKey(key))
                        dic.Add(key, val);
                }
            }
            return dic;
        }

        // Create transformed lexicon file from a list of source lines, preserving remainder of each line
        private static void CreateTransformedLexiconFromLines(List<string> lines, string targetFilePath, Dictionary<string, string> map)
        {
            using (var writer = new StreamWriter(targetFilePath, false, System.Text.Encoding.UTF8))
            {
                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        writer.WriteLine(line);
                        continue;
                    }

                    var parts = line.Split('\t', 2);
                    var token = parts[0];
                    var rest = parts.Length > 1 ? "\t" + parts[1] : string.Empty;

                    if (System.Text.RegularExpressions.Regex.IsMatch(token, "^\\p{L}"))
                    {
                        var newToken = token;
                        foreach (var m in map)
                        {
                            try
                            {
                                var pattern = "\\b" + System.Text.RegularExpressions.Regex.Escape(m.Key) + "\\b";
                                if (System.Text.RegularExpressions.Regex.IsMatch(newToken, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                                {
                                    newToken = System.Text.RegularExpressions.Regex.Replace(newToken, pattern, m.Value, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                                }
                            }
                            catch
                            {
                                // ignore regex errors
                            }
                        }

                        writer.WriteLine(newToken + rest);
                    }
                    else
                    {
                        writer.WriteLine(line);
                    }
                }
            }
        }

        // Search for a file by walking up the directory tree from startDir. Returns full path if found, otherwise null.
        private static string FindFileUpwards(string startDir, string fileName)
        {
            try
            {
                var dir = new DirectoryInfo(startDir);
                while (dir != null)
                {
                    var candidate = Path.Combine(dir.FullName, fileName);
                    if (File.Exists(candidate))
                        return candidate;
                    dir = dir.Parent;
                }
            }
            catch
            {
                // ignore IO errors
            }
            return null;
        }

        private static Dictionary<string, double> LoadLexicon(string languageCode)
        {
            var assembly = typeof(SentimentIntensityAnalyzer).Assembly;

            // Normalize and compute assembly folder early so we can write transformed files when needed
            var normalizedLower = (languageCode ?? string.Empty).ToLowerInvariant();
            var assemblyFolder = Path.GetDirectoryName(assembly.Location) ?? AppDomain.CurrentDomain.BaseDirectory ?? Directory.GetCurrentDirectory();

            // candidates: exact languageCode, language-only (en from en-gb), default (no suffix)
            var candidates = new List<string> { languageCode };
            var langPart = languageCode.Split('-')[0];
            if (!string.Equals(langPart, languageCode, System.StringComparison.OrdinalIgnoreCase))
                candidates.Add(langPart);

            // try embedded resources first
            foreach (var cand in candidates)
            {
                var resName = assembly.GetManifestResourceNames()
                    .FirstOrDefault(n => n.EndsWith($"vader_lexicon_{cand}.txt", System.StringComparison.OrdinalIgnoreCase));
                if (resName != null)
                {
                    using (var stream = assembly.GetManifestResourceStream(resName))
                    {
                        if (stream != null)
                        {
                            var loaded = LoadLexiconFromStream(stream);
                            var transformed = TransformLexiconForUS(loaded, languageCode);

                            // If en-us was requested, persist transformed lexicon to disk so we don't need to transform again
                            if (normalizedLower.StartsWith("en-us", StringComparison.OrdinalIgnoreCase))
                            {
                                var targetName = $"vader_lexicon_{normalizedLower}.txt";
                                var targetPath = Path.Combine(assemblyFolder, targetName);
                                if (!File.Exists(targetPath))
                                {
                                    try
                                    {
                                        WriteLexiconToFile(transformed, targetPath);
                                    }
                                    catch
                                    {
                                        try
                                        {
                                            var alt = Path.Combine(Directory.GetCurrentDirectory(), targetName);
                                            WriteLexiconToFile(transformed, alt);
                                        }
                                        catch
                                        {
                                            // ignore write failures
                                        }
                                    }

        
                                }
                            }

                            // Merge emoji lexicon entries when available
                            try { MergeEmojiLexicon(transformed, assemblyFolder, assembly); } catch { }

                return transformed;
                        }
                    }
                }
            }

            // try default resource name
            var defaultRes = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("vader_lexicon.txt", System.StringComparison.OrdinalIgnoreCase));
            if (defaultRes != null)
            {
                using (var stream = assembly.GetManifestResourceStream(defaultRes))
                {
                    if (stream != null)
                    {
                        // read source lines so we can both parse and optionally write a preserved transformed file
                        var srcLines = ReadLinesFromStream(stream);
                        var loaded = LoadLexiconFromLines(srcLines);
                        var transformed = TransformLexiconForUS(loaded, languageCode);

                        if (normalizedLower.StartsWith("en-us", StringComparison.OrdinalIgnoreCase))
                        {
                            var map = GetGbToUsMap();
                            var targetName = $"vader_lexicon_{normalizedLower}.txt";
                            var targetPath = Path.Combine(assemblyFolder, targetName);
                            if (!File.Exists(targetPath))
                            {
                                try
                                {
                                    CreateTransformedLexiconFromLines(srcLines, targetPath, map);
                                }
                                catch
                                {
                                    try
                                    {
                                        var alt = Path.Combine(Directory.GetCurrentDirectory(), targetName);
                                        CreateTransformedLexiconFromLines(srcLines, alt, map);
                                    }
                                    catch
                                    {
                                        // ignore write failures
                                    }
                                }
                            }
                        }

                        return transformed;
                    }
                }
            }

            // fallback to files next to assembly or current directory
            // assemblyFolder already computed above
            foreach (var cand in candidates)
            {
                var fileName = $"vader_lexicon_{cand}.txt";
                var filePath = Path.Combine(assemblyFolder, fileName);
                if (!File.Exists(filePath))
                    filePath = Path.Combine(Directory.GetCurrentDirectory(), fileName);
                // also check common subfolders next to the assembly (e.g. 'Anakin-Analytics' subfolder)
                if (!File.Exists(filePath))
                {
                    var found = FindFileInSubfolders(assemblyFolder, fileName, maxDepth: 2);
                    if (!string.IsNullOrEmpty(found))
                        filePath = found;
                }
                if (File.Exists(filePath))
                {
                    // If en-us requested, prefer to create a preserved transformed file on disk then read it.
                    if (normalizedLower.StartsWith("en-us", StringComparison.OrdinalIgnoreCase))
                    {
                        var map = GetGbToUsMap();
                        var targetName = $"vader_lexicon_{normalizedLower}.txt";
                        var targetPath = Path.Combine(assemblyFolder, targetName);
                        if (!File.Exists(targetPath))
                        {
                            try
                            {
                                CreateTransformedLexiconFile(filePath, targetPath, map);
                            }
                            catch
                            {
                                try
                                {
                                    var alt = Path.Combine(Directory.GetCurrentDirectory(), targetName);
                                    CreateTransformedLexiconFile(filePath, alt, map);
                                }
                                catch
                                {
                                    // ignore write failures
                                }
                            }
                        }

                        if (File.Exists(targetPath))
                        {
                            using (var stream = File.OpenRead(targetPath))
                            {
                                var result = LoadLexiconFromStream(stream);
                                try { MergeEmojiLexicon(result, assemblyFolder, assembly); } catch { }
                                return result;
                            }
                        }
                        // fallback to loading the original file and transforming in-memory
                    }

                    using (var stream = File.OpenRead(filePath))
                    {
                        var loaded = LoadLexiconFromStream(stream);
                        var transformed = TransformLexiconForUS(loaded, languageCode);
                        try { MergeEmojiLexicon(transformed, assemblyFolder, assembly); } catch { }
                        return transformed;
                    }
                }
            }

            // try default file name
            var defaultFile = Path.Combine(assemblyFolder, "vader_lexicon.txt");
            if (!File.Exists(defaultFile))
                defaultFile = Path.Combine(Directory.GetCurrentDirectory(), "vader_lexicon.txt");
            if (File.Exists(defaultFile))
            {
                // read source file lines so we can preserve full line content when transforming
                var srcLines = File.ReadAllLines(defaultFile).ToList();
                var loaded = LoadLexiconFromLines(srcLines);
                var transformed = TransformLexiconForUS(loaded, languageCode);

                if (normalizedLower.StartsWith("en-us", StringComparison.OrdinalIgnoreCase))
                {
                    var map = GetGbToUsMap();
                    var targetName = $"vader_lexicon_{normalizedLower}.txt";
                    var targetPath = Path.Combine(assemblyFolder, targetName);
                    if (!File.Exists(targetPath))
                    {
                        try
                        {
                            CreateTransformedLexiconFromLines(srcLines, targetPath, map);
                        }
                        catch
                        {
                            try
                            {
                                var alt = Path.Combine(Directory.GetCurrentDirectory(), targetName);
                                CreateTransformedLexiconFromLines(srcLines, alt, map);
                            }
                            catch
                            {
                                // ignore write failures
                            }
                        }
                    }
                }

                return transformed;
            }

            // If en-us was requested but no en-us lexicon exists, attempt to create one from a GB or default lexicon
            if (normalizedLower.StartsWith("en-us", StringComparison.OrdinalIgnoreCase))
            {
                var map = GetGbToUsMap();
                var sourceFiles = new[] { "vader_lexicon_en-gb.txt", "vader_lexicon_en.txt", "vader_lexicon.txt" };
                foreach (var srcName in sourceFiles)
                {
                    var srcPath = Path.Combine(assemblyFolder, srcName);
                    if (!File.Exists(srcPath))
                        srcPath = Path.Combine(Directory.GetCurrentDirectory(), srcName);
                    // try to find file in parent directories or in subfolders next to the assembly
                    if (!File.Exists(srcPath))
                    {
                        var found = FindFileUpwards(assemblyFolder, srcName);
                        if (!string.IsNullOrEmpty(found))
                            srcPath = found;
                    }
                    if (!File.Exists(srcPath))
                    {
                        var foundSub = FindFileInSubfolders(assemblyFolder, srcName, maxDepth: 2);
                        if (!string.IsNullOrEmpty(foundSub))
                            srcPath = foundSub;
                    }
                    if (File.Exists(srcPath))
                    {
                        var targetName = $"vader_lexicon_{normalizedLower}.txt";
                        var targetPath = Path.Combine(assemblyFolder, targetName);
                        try
                        {
                            CreateTransformedLexiconFile(srcPath, targetPath, map);
                            if (File.Exists(targetPath))
                            {
                                using (var stream = File.OpenRead(targetPath))
                                {
                                    var result = LoadLexiconFromStream(stream);
                                    try { MergeEmojiLexicon(result, assemblyFolder, assembly); } catch { }
                                    return result;
                                }
                            }
                        }
                        catch
                        {
                            // ignore write failures and continue trying other sources
                        }
                    }
                }
            }

            throw new InvalidOperationException("vader_lexicon for language '" + languageCode + "' not found as an embedded resource or as a file next to the assembly.");
        }

        private static Dictionary<string, double> LoadLexiconFromStream(Stream stream)
        {
            var dic = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
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
                    // Normalize token by trimming surrounding whitespace and stray quotes so keys
                    // in the lexicon do not include enclosing '"' or '\'' characters which can
                    // appear in some transformed lexicon sources.
                    var key = parts[0].Trim().Trim('"', '\'');

                    if (double.TryParse(parts[1], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var val))
                    {
                        if (!dic.ContainsKey(key))
                            dic.Add(key, val);
                    }
                }
            }

            return dic;
        }

        // If a US English lexicon was requested but only a GB lexicon was found, transform keys
        // from common British spellings to US spellings so callers can request "en-us".
        private static Dictionary<string, double> TransformLexiconForUS(Dictionary<string, double> source, string languageCode)
        {
            if (string.IsNullOrEmpty(languageCode))
                return source;

            if (!languageCode.Equals("en-us", StringComparison.OrdinalIgnoreCase) && !languageCode.StartsWith("en-us", StringComparison.OrdinalIgnoreCase))
                return source;

            var map = GetGbToUsMap();

            var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in source)
            {
                var key = kv.Key;
                var newKey = key;

                // replace whole-word occurrences only
                foreach (var m in map)
                {
                    try
                    {
                        var pattern = "\\b" + System.Text.RegularExpressions.Regex.Escape(m.Key) + "\\b";
                        if (System.Text.RegularExpressions.Regex.IsMatch(newKey, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                        {
                            newKey = System.Text.RegularExpressions.Regex.Replace(newKey, pattern, m.Value, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        }
                    }
                    catch
                    {
                        // ignore regex errors for odd tokens
                    }
                }

                // prefer mapped key; if collision occurs, keep the first inserted value
                if (!result.ContainsKey(newKey))
                    result.Add(newKey, kv.Value);
            }

            return result;
        }

        // Return a conservative GB->US spelling map used for token-level replacements
        private static Dictionary<string, string> GetGbToUsMap()
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "colour", "color" }, { "colours", "colors" }, { "colourful", "colorful" },
                { "behaviour", "behavior" }, { "behaviours", "behaviors" }, { "behavioural", "behavioral" },
                { "organise", "organize" }, { "organised", "organized" }, { "organises", "organizes" }, { "organising", "organizing" },
                { "optimise", "optimize" }, { "optimised", "optimized" }, { "optimising", "optimizing" }, { "optimisation", "optimization" },
                { "realise", "realize" }, { "realised", "realized" }, { "realises", "realizes" }, { "realising", "realizing" },
                { "favour", "favor" }, { "favours", "favors" }, { "favourite", "favorite" }, { "favourites", "favorites" }, { "favourable", "favorable" },
                { "defence", "defense" }, { "defences", "defenses" },
                { "licence", "license" }, { "licences", "licenses" },
                { "labour", "labor" }, { "labours", "labors" },
                { "honour", "honor" }, { "honours", "honors" },
                { "neighbour", "neighbor" }, { "neighbours", "neighbors" },
                { "centre", "center" }, { "centres", "centers" },
                { "theatre", "theater" }, { "theatres", "theaters" },
                { "metre", "meter" }, { "metres", "meters" }, { "litre", "liter" }, { "litres", "liters" },
                { "paediatric", "pediatric" },
                { "tyre", "tire" }, { "tyres", "tires" },
                { "cheque", "check" }, { "cheques", "checks" },
                { "catalogue", "catalog" }, { "catalogues", "catalogs" },
                { "dialogue", "dialog" }, { "dialogues", "dialogs" },
                { "travelling", "traveling" }, { "travelled", "traveled" }, { "traveller", "traveler" }, { "travellers", "travelers" },
                { "counsellor", "counselor" }, { "counselling", "counseling" }, { "counselled", "counseled" },
                { "anaesthesia", "anesthesia" }, { "analogue", "analog" }, { "encyclopaedia", "encyclopedia" }, { "aeroplane", "airplane" }
            };
        }

        // Create a line-for-line transformed lexicon file from a source lexicon file by replacing word-like tokens
        // using the GB->US map and preserving the remainder of each line unchanged.
        private static void CreateTransformedLexiconFile(string sourceFilePath, string targetFilePath, Dictionary<string, string> map)
        {
            using (var reader = new StreamReader(sourceFilePath))
            using (var writer = new StreamWriter(targetFilePath, false, System.Text.Encoding.UTF8))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        writer.WriteLine(line);
                        continue;
                    }

                    var parts = line.Split('\t', 2);
                    var token = parts[0];
                    var rest = parts.Length > 1 ? "\t" + parts[1] : string.Empty;

                    // Only transform tokens that start with a letter
                    if (System.Text.RegularExpressions.Regex.IsMatch(token, "^\\p{L}"))
                    {
                        var newToken = token;
                        foreach (var m in map)
                        {
                            try
                            {
                                var pattern = "\\b" + System.Text.RegularExpressions.Regex.Escape(m.Key) + "\\b";
                                if (System.Text.RegularExpressions.Regex.IsMatch(newToken, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                                {
                                    newToken = System.Text.RegularExpressions.Regex.Replace(newToken, pattern, m.Value, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                                }
                            }
                            catch
                            {
                                // ignore regex errors for odd tokens
                            }
                        }

                        writer.WriteLine(newToken + rest);
                    }
                    else
                    {
                        writer.WriteLine(line);
                    }
                }
            }
        }

        /// <summary>
        /// Return metrics for positive, negative and neutral sentiment based on the input text.
        /// </summary>
        /// <param name="input">The input text to analyze.</param>
        /// <param name="languageCode">Language code in writing style "language-country" (e.g. "en-gb", "en-us"). Default is "en-gb".</param>
        /// <returns>SentimentAnalysisResults containing Positive, Negative, Neutral and Compound scores.</returns>
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

            // get lexicon for requested language
            var lexicon = GetLexiconForLanguage(languageCode);

            // Diagnostic logging (enabled via appsettings.json AnakinAnalytics.DebugLogging)
            try
            {
                if (ConfigStore.DebugLoggingEnabled)
                {
                    var baseDir = AppDomain.CurrentDomain.BaseDirectory ?? Directory.GetCurrentDirectory();
                    Console.WriteLine($"ANAKIN_DEBUG: BaseDir={baseDir}, Language={languageCode}, LexiconCount={(lexicon?.Count ?? 0)}, WordsAndEmoticonsCount={wordsAndEmoticons.Count}");
                    Console.WriteLine($"ANAKIN_DEBUG: WordsAndEmoticons=[{string.Join(",", wordsAndEmoticons)}]");
                }
            }
            catch
            {
                // swallow diagnostics failures
            }

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
                sentiments = SentimentValence(valence, sentiText, item, i, sentiments, resources, lexicon);
            }

            sentiments = ButCheck(wordsAndEmoticons, sentiments);

            return ScoreValence(sentiments, input);
        }

        private IList<double> SentimentValence(double valence, SentiText sentiText, string item, int i, IList<double> sentiments, SentimentResources resources, Dictionary<string, double> lexicon)
        {
            // Normalize token for lexicon lookup: trim surrounding whitespace and stray
            // quotes so tokens like '"beating"' match the lexicon key 'beating'.
            string itemLowerCase = item?.Trim().Trim('"', '\'').ToLower() ?? string.Empty;
            // Diagnostic: emit token/lookup details when enabled via appsettings.json
            try
            {
                if (ConfigStore.DebugLoggingEnabled)
                {
                    Console.WriteLine($"ANAKIN_DEBUG: SentimentValence: item='{item}', normalized='{itemLowerCase}', lexiconCount={(lexicon?.Count ?? 0)}, contains={lexicon != null && lexicon.ContainsKey(itemLowerCase)}");
                }
            }
            catch { }
            if (!lexicon.ContainsKey(itemLowerCase))
            {
                sentiments.Add(valence);
                return sentiments;
            }
            bool isCapDiff = sentiText.IsCapDifferential;
            IList<string> wordsAndEmoticons = sentiText.WordsAndEmoticons;
            valence = lexicon[itemLowerCase];
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
                if (i > startI && !lexicon.ContainsKey(wordsAndEmoticons[i - (startI + 1)].ToLower()))
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

            valence = LeastCheck(valence, wordsAndEmoticons, i, resources, lexicon);
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

        private double LeastCheck(double valence, IList<string> wordsAndEmoticons, int i, SentimentResources resources, Dictionary<string, double> lexicon)
        {
            if (i > 1 && !lexicon.ContainsKey(wordsAndEmoticons[i - 1].ToLower()) &&
                wordsAndEmoticons[i - 1].ToLower() == "least")
            {
                if (wordsAndEmoticons[i - 2].ToLower() != "at" && wordsAndEmoticons[i - 2].ToLower() != "very")
                {
                    valence = valence * resources.NScalar;
                }
            }
            else if (i > 0 && !lexicon.ContainsKey(wordsAndEmoticons[i-1].ToLower()) 
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
            // Normalize tokens for idiom matching (trim surrounding quotes/whitespace)
            // Trim a wider set of surrounding punctuation and common Unicode quotation marks
            var trimChars = new char[] { '"', '\'', '\u2018', '\u2019', '\u201C', '\u201D', '“', '”', '‘', '’', '(', ')', '[', ']', '{', '}', '.', ',', ':', ';', '!' , '?', ' ' };
            Func<string, string> normalize = s => (s ?? string.Empty).Trim(trimChars).Trim();

            var tokens = wordsAndEmoticons.Select(t => normalize(t)).ToArray();

            var oneZero = string.Concat(tokens[i - 1], " ", tokens[i]);
            var twoOneZero = string.Concat(tokens[i - 2], " ", tokens[i - 1], " ", tokens[i]);
            var twoOne = string.Concat(tokens[i - 2], " ", tokens[i - 1]);
            var threeTwoOne = string.Concat(tokens[i - 3], " ", tokens[i - 2], " ", tokens[i - 1]);
            var threeTwo = string.Concat(tokens[i - 3], " ", tokens[i - 2]);

            string[] sequences = { oneZero, twoOneZero, twoOne, threeTwoOne, threeTwo };

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
        
        // Merge emoji lexicon entries (if present) into the provided lexicon dictionary.
        // Emoji file format: token<TAB>valueOrDescription
        // If the value is numeric it will be used. Otherwise a small built-in mapping
        // maps common positive/negative emojis to heuristic scores.
        private static void MergeEmojiLexicon(Dictionary<string, double> lexicon, string assemblyFolder, Assembly assembly)
        {
            if (lexicon == null) return;

            // try embedded resource first
            try
            {
                var resName = assembly.GetManifestResourceNames()
                    .FirstOrDefault(n => n.EndsWith("emoji_utf8_lexicon.txt", StringComparison.OrdinalIgnoreCase));
                if (resName != null)
                {
                    using (var stream = assembly.GetManifestResourceStream(resName))
                    {
                        if (stream != null)
                        {
                            MergeEmojiFromStream(lexicon, stream);
                            return;
                        }
                    }
                }
            }
            catch
            {
                // ignore
            }

            // try files next to assembly or current directory
            var candidates = new[] { Path.Combine(assemblyFolder, "emoji_utf8_lexicon.txt"), Path.Combine(Directory.GetCurrentDirectory(), "emoji_utf8_lexicon.txt") };
            foreach (var c in candidates)
            {
                if (File.Exists(c))
                {
                    try
                    {
                        using (var stream = File.OpenRead(c))
                        {
                            MergeEmojiFromStream(lexicon, stream);
                        }
                    }
                    catch
                    {
                        // ignore
                    }
                    return;
                }
            }
        }

        private static void MergeEmojiFromStream(Dictionary<string, double> lexicon, Stream stream)
        {
            using (var reader = new StreamReader(stream))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    var trimmed = line?.Trim();
                    if (string.IsNullOrEmpty(trimmed))
                        continue;
                    var parts = trimmed.Split('\t');
                    if (parts.Length < 1) continue;
                    var token = parts[0];
                    if (string.IsNullOrEmpty(token)) continue;

                    // Do not overwrite existing lexicon values
                    // Normalize token by trimming surrounding whitespace and stray quotes so keys
                    // do not include enclosing '"' or '\'' characters.
                    token = token.Trim().Trim('"', '\'');

                    if (lexicon.ContainsKey(token)) continue;

                    double val;
                    if (parts.Length > 1 && double.TryParse(parts[1], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out val))
                    {
                        lexicon[token] = val;
                    }
                    else
                    {
                        // apply heuristic mapping for common emojis
                        lexicon[token] = GetEmojiDefaultValence(token);
                    }
                }
            }
        }

        private static double GetEmojiDefaultValence(string emoji)
        {
            // Conservative default scores; these are heuristics for common emojis
            // and are intentionally modest in magnitude.
            var positive = new HashSet<string>
            {
                "😀","😁","😂","🤣","😃","😄","😊","😎","😉","😍","😘","😇","🙂","🤗","🤩","🥰","😸","😺","😻"
            };
            var negative = new HashSet<string>
            {
                "😢","😭","😞","😟","😠","😡","🤬","😒","😔","😩","😫","😖","👿","💀","☠","😾"
            };

            if (positive.Contains(emoji)) return 2.0;
            if (negative.Contains(emoji)) return -2.0;
            return 0.0;
        }
    
    }

}
