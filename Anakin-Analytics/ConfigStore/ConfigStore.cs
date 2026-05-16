using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Newtonsoft.Json.Linq;

namespace AnakinAnalytics
{

    /// <summary>
    /// Proof of concept for loading the words to be used as boosters, negations etc.
    /// 
    /// Currently not used.
    /// </summary>
    public class ConfigStore
    {

        private static readonly Dictionary<string, ConfigStore> configs = new Dictionary<string, ConfigStore>(StringComparer.OrdinalIgnoreCase);
        private static bool configsLoaded = false;

        public Dictionary<string, double> BoosterDict { get; private set; }

        public string[] Negations { get; private set; }

        public Dictionary<string, double> SpecialCaseIdioms { get; private set; }

        // private default ctor used when loading from XML roots
        private ConfigStore()
        {
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="languageCode">Language code in writing style "language-country". Default is British English.</param>
        /// <returns>ConfigStore object.</returns>
        public static ConfigStore CreateConfig(string languageCode = "en-gb")
        {
            if (!configsLoaded)
            {
                LoadAllConfigs();
            }

            if (string.IsNullOrEmpty(languageCode))
                languageCode = "en-gb";

           // try exact match
            if (configs.TryGetValue(languageCode, out var cfg))
                return cfg;

            // try lower/normalized
            var key = languageCode.ToLowerInvariant();
            if (configs.TryGetValue(key, out cfg))
                return cfg;

            // try language-only (en from en-gb)
            var parts = languageCode.Split('-');
            if (parts.Length > 1 && configs.TryGetValue(parts[0], out cfg))
                return cfg;

            // fallback to en-gb if present
            if (configs.TryGetValue("en-gb", out cfg))
                return cfg;

            // fallback to any available
            return configs.Values.FirstOrDefault();
        }

        private static void LoadAllConfigs()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory ?? Directory.GetCurrentDirectory();
            string folder = Path.Combine(baseDir, "strings");
            if (!Directory.Exists(folder))
            {
                configsLoaded = true;
                return;
            }

            var files = Directory.GetFiles(folder, "*.xml");
            foreach (var file in files)
            {
                try
                {
                    XElement root = XDocument.Load(file).Document.Root;
                    var code = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
                    var cfg = new ConfigStore();
                    cfg.LoadFromRoot(root);
                    configs[code] = cfg;
                }
                catch
                {
                    // ignore invalid files
                }
            }

            configsLoaded = true;
        }

        private void LoadFromRoot(XElement root)
        {
            LoadNegations(root);
            LoadIdioms(root);
            LoadBooster(root);
        }

        /// <summary>
        /// Initializes the ConfigStore and loads the config file.
        /// </summary>
        /// <param name="languageCode">Language code in writing style "language-country".</param>
        private void LoadConfig(string languageCode)
        {
            // Try to load configuration from appsettings.json first (if present).
            string baseDir = AppDomain.CurrentDomain.BaseDirectory ?? Directory.GetCurrentDirectory();
            string appsettingsPath = Path.Combine(baseDir, "appsettings.json");

            string configFilePath = null;
            if (File.Exists(appsettingsPath))
            {
                var json = JObject.Parse(File.ReadAllText(appsettingsPath));
                var vs = json["AnakinAnalytics"] as JObject;
                if (vs != null)
                {
                    // override language code from settings when provided
                    var lang = vs.Value<string>("LanguageCode");
                    if (!string.IsNullOrEmpty(lang))
                    {
                        languageCode = lang;
                    }

                    // config file path can point to an XML file to load strings from
                    var cfg = vs.Value<string>("ConfigFile");
                    if (!string.IsNullOrEmpty(cfg))
                    {
                        configFilePath = cfg;
                        if (!Path.IsPathRooted(configFilePath))
                        {
                            configFilePath = Path.Combine(baseDir, configFilePath);
                        }
                    }

                    if (vs["Negations"] != null)
                    {
                        LoadNegationsFromJson(vs["Negations"]);
                    }

                    if (vs["Idioms"] != null)
                    {
                        LoadIdiomsFromJson(vs["Idioms"]);
                    }

                    if (vs["Boosters"] != null)
                    {
                        LoadBoosterFromJson(vs["Boosters"]);
                    }
                }
            }

            // Determine which XML file to use for any missing sections
            if (Negations == null || SpecialCaseIdioms == null || BoosterDict == null)
            {
                string xmlPath = configFilePath ?? Path.Combine(baseDir, "strings", $"{languageCode}.xml");
                if (!File.Exists(xmlPath))
                {
                    throw new FileNotFoundException("Language file was not found. Please check language code or ConfigFile path.");
                }
                XElement root = XDocument.Load(xmlPath).Document.Root;
                if (Negations == null)
                {
                    LoadNegations(root);
                }
                if (SpecialCaseIdioms == null)
                {
                    LoadIdioms(root);
                }
                if (BoosterDict == null)
                {
                    LoadBooster(root);
                }
            }
        }

        private void LoadNegationsFromJson(JToken token)
        {
            if (token == null) return;
            var arr = token as JArray;
            if (arr == null) return;
            Negations = arr.Values<string>().ToArray();
        }

        private void LoadIdiomsFromJson(JToken token)
        {
            SpecialCaseIdioms = new Dictionary<string, double>();
            if (token == null) return;
            var obj = token as JObject;
            if (obj == null) return;
            foreach (var p in obj.Properties())
            {
                double value;
                if (double.TryParse(p.Value.ToString(), out value))
                {
                    SpecialCaseIdioms[p.Name] = value;
                }
            }
        }

        private void LoadBoosterFromJson(JToken token)
        {
            BoosterDict = new Dictionary<string, double>();
            if (token == null) return;
            var obj = token as JObject;
            if (obj == null) return;
            foreach (var p in obj.Properties())
            {
                // Value can be a string like "BIncr"/"BDecr" or a numeric value
                var v = p.Value.Type == JTokenType.String ? p.Value.ToString() : p.Value.ToString();
                double sign;
                if (string.Equals(v, "BIncr", StringComparison.OrdinalIgnoreCase))
                {
                    sign = 0.293;
                }
                else if (string.Equals(v, "BDecr", StringComparison.OrdinalIgnoreCase))
                {
                    sign = -0.293;
                }
                else if (!double.TryParse(v, out sign))
                {
                    // default to positive booster if parsing fails
                    sign = 0.293;
                }
                BoosterDict[p.Name] = sign;
            }
        }

        /// <summary>
        /// Loads negations from config file.
        /// </summary>
        /// <param name="root">Root element of XML document</param>
        private void LoadNegations(XElement root)
        {
            var nodes = root.Descendants(XName.Get("negation"));
            int length = nodes.Count();
            Negations = new string[length];
            for (int i = 0; i < length; i++)
            {
                Negations[i] = nodes.ElementAt(i).Value;
            }
        }

        /// <summary>
        /// Loads idioms from config file.
        /// </summary>
        /// <param name="root">Root element of XML document</param>
        private void LoadIdioms(XElement root)
        {
            SpecialCaseIdioms = new Dictionary<string, double>();
            var nodes = root.Descendants(XName.Get("idiom"));
            double value;
            foreach (var n in nodes)
            {
                value = double.Parse(n.Attribute(XName.Get("value")).Value);
                SpecialCaseIdioms.Add(n.Value, value);
            }
        }

        /// <summary>
        /// Loads booster words from config file.
        /// </summary>
        /// <param name="root">Root element of XML document</param>
        private void LoadBooster(XElement root)
        {
            BoosterDict = new Dictionary<string, double>();
            var nodes = root.Descendants(XName.Get("booster"));
            double sign;
            foreach (var n in nodes)
            {
                sign = n.Attribute(XName.Get("sign")).Value == "BIncr" ? 0.293 : -0.293;
                BoosterDict.Add(n.Value, sign);
            }
        }
    }
}
