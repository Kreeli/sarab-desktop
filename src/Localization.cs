using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace SarabDesktop
{
    public static class L
    {
        static readonly Dictionary<string, Dictionary<string, string>> dictionaries = new Dictionary<string, Dictionary<string, string>>
        {
            ["en-US"] = Load("SarabDesktop.en.json"),
            ["ja-JP"] = Load("SarabDesktop.ja.json"),
            ["zh-TW"] = Load("SarabDesktop.zh-TW.json")
        };

        public static string SystemLanguage => ResolveCulture(ReadPreferredUiCulture());
        public static string Language { get; private set; } = SystemLanguage;
        public static string Preference { get; private set; } = "auto";
        public static int TranslationCount => dictionaries["en-US"].Count;
        public static IEnumerable<string> SupportedLanguages => new[] { "zh-CN", "zh-TW", "ja-JP", "en-US" };
        public static IEnumerable<string> SourceKeys => dictionaries["en-US"].Keys;
        public static int TranslationCountFor(string language) => language == "zh-CN" ? TranslationCount : dictionaries.ContainsKey(language) ? dictionaries[language].Count : 0;

        public static void Set(string language)
        {
            Preference = language == "zh-CN" || language == "zh-TW" || language == "ja-JP" || language == "en-US" ? language : "auto";
            Language = Preference == "auto" ? SystemLanguage : Preference;
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(Language);
        }

        public static string T(string text)
        {
            if (text == null || Language == "zh-CN") return text;
            string translated;
            if (dictionaries[Language].TryGetValue(text, out translated)) return translated;
            if (Language != "en-US" && dictionaries["en-US"].TryGetValue(text, out translated)) return translated;
            return text;
        }

        public static string SourceKeyFor(string translatedText)
        {
            if (translatedText == null) return null;
            foreach (string source in dictionaries["en-US"].Keys)
                if (string.Equals(T(source), translatedText, StringComparison.Ordinal)) return source;
            return null;
        }

        public static string F(string text, params object[] values) => string.Format(CultureInfo.InvariantCulture, T(text), values);
        public static bool HasTranslation(string language, string text) => language == "zh-CN" || dictionaries.ContainsKey(language) && dictionaries[language].ContainsKey(text);

        static Dictionary<string, string> Load(string resource)
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource))
            {
                if (stream == null) throw new InvalidDataException("Missing embedded language resource: " + resource);
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                    return Json.From<Dictionary<string, string>>(reader.ReadToEnd()) ?? new Dictionary<string, string>();
            }
        }

        public static string ResolveCulture(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "en-US";
            string normalized = name.Replace('_', '-');
            if (normalized.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
            {
                string lower = normalized.ToLowerInvariant();
                return lower.Contains("hant") || lower.Contains("tw") || lower.Contains("hk") || lower.Contains("mo") ? "zh-TW" : "zh-CN";
            }
            return normalized.StartsWith("ja", StringComparison.OrdinalIgnoreCase) ? "ja-JP" : "en-US";
        }

        static string ReadPreferredUiCulture()
        {
            try
            {
                uint count = 0, length = 0;
                GetUserPreferredUILanguages(8, ref count, IntPtr.Zero, ref length);
                if (length > 1)
                {
                    var buffer = new StringBuilder((int)length);
                    if (GetUserPreferredUILanguages(8, ref count, buffer, ref length))
                    {
                        string first = buffer.ToString().Split('\0')[0];
                        if (!string.IsNullOrWhiteSpace(first)) return first;
                    }
                }
            }
            catch { }
            try { return CultureInfo.GetCultureInfo((int)GetUserDefaultUILanguage()).Name; }
            catch { return CultureInfo.InstalledUICulture.Name; }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool GetUserPreferredUILanguages(uint flags, ref uint languageCount, IntPtr languages, ref uint bufferLength);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        static extern bool GetUserPreferredUILanguages(uint flags, ref uint languageCount, StringBuilder languages, ref uint bufferLength);

        [DllImport("kernel32.dll")]
        static extern ushort GetUserDefaultUILanguage();
    }
}
