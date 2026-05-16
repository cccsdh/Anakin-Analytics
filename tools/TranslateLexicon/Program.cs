using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

class Program
{
    // Simple tool to translate the first token of each line in the vader lexicon using LibreTranslate
    // Usage: dotnet run --project tools/TranslateLexicon -- [sourceFile] [frOut] [esOut]
    // Defaults: sourceFile = Anakin-Analytics/Anakin-Analytics/vader_lexicon_en-gb.txt

    static async Task<int> Main(string[] args)
    {
        var source = args.Length > 0 ? args[0] : Path.Combine("Anakin-Analytics", "Anakin-Analytics", "vader_lexicon_en-gb.txt");
        var frOut = args.Length > 1 ? args[1] : Path.Combine("Anakin-Analytics", "Anakin-Analytics", "vader_lexicon_fr.txt");
        var esOut = args.Length > 2 ? args[2] : Path.Combine("Anakin-Analytics", "Anakin-Analytics", "vader_lexicon_es.txt");

        if (!File.Exists(source))
        {
            Console.Error.WriteLine($"Source file not found: {source}");
            return 1;
        }

        var lines = await File.ReadAllLinesAsync(source);

        using var http = new HttpClient();
        http.Timeout = TimeSpan.FromSeconds(30);
        var frLines = new List<string>(lines.Length);
        var esLines = new List<string>(lines.Length);

        int count = 0;
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                frLines.Add(line);
                esLines.Add(line);
                continue;
            }

            // split only on first tab
            var parts = line.Split('\t', 2);
            var token = parts[0];
            var rest = parts.Length > 1 ? "\t" + parts[1] : "";

            // decide whether token looks like a word to translate: starts with a letter (covers accented letters)
            // This avoids skipping tokens that include punctuation later but still begin with a word character.
            if (Regex.IsMatch(token, "^\\p{L}", RegexOptions.None))
            {
                var clean = token.Trim();

                // translate token to French and Spanish via LibreTranslate (https://libretranslate.com/) - public instance
                // Warning: public instances may have rate limits. This is a best-effort automated translation of single tokens.
                var fr = await TranslateToken(http, clean, "en", "fr");
                var es = await TranslateToken(http, clean, "en", "es");

                // preserve original capitalization pattern
                fr = MatchCapitalization(clean, fr);
                es = MatchCapitalization(clean, es);

                frLines.Add(fr + rest);
                esLines.Add(es + rest);
            }
            else
            {
                frLines.Add(line);
                esLines.Add(line);
            }

            count++;
            if ((count % 200) == 0)
                Console.WriteLine($"Processed {count} lines...");
        }

        await File.WriteAllLinesAsync(frOut, frLines, Encoding.UTF8);
        await File.WriteAllLinesAsync(esOut, esLines, Encoding.UTF8);

        Console.WriteLine($"Wrote {frOut} and {esOut}");
        return 0;
    }

    static string MatchCapitalization(string source, string translation)
    {
        if (string.IsNullOrEmpty(translation)) return translation;
        if (string.IsNullOrEmpty(source)) return translation;
        if (source.ToUpperInvariant() == source)
            return translation.ToUpperInvariant();
        if (char.IsUpper(source[0]))
            return char.ToUpper(translation[0]) + translation.Substring(1);
        return translation;
    }

    static async Task<string> TranslateToken(HttpClient http, string text, string from, string to)
    {
        try
        {
            var url = "https://libretranslate.de/translate"; // alternative public instance
            var payload = new Dictionary<string, string>
            {
                { "q", text },
                { "source", from },
                { "target", to },
                { "format", "text" }
            };
            using var content = new FormUrlEncodedContent(payload);
            using var resp = await http.PostAsync(url, content);
            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("translatedText", out var tt))
                return tt.GetString() ?? text;
            return text;
        }
        catch
        {
            // fallback: return original token unchanged
            return text;
        }
    }
}
