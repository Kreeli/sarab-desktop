using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

namespace SarabDesktop
{
    public static class L
    {
        static readonly Dictionary<string, string> english = LoadEnglish();
        public static string SystemLanguage => CultureInfo.InstalledUICulture.TwoLetterISOLanguageName == "zh" ? "zh-CN" : "en-US";
        public static string Language { get; private set; } = SystemLanguage;
        public static void Set(string language)
        {
            Language = language == "zh-CN" ? "zh-CN" : language == "en-US" ? "en-US" : SystemLanguage;
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(Language);
        }
        public static string T(string text)
        {
            string translated;
            return Language == "en-US" && text != null && english.TryGetValue(text, out translated) ? translated : text;
        }
        public static string F(string text, params object[] values) => string.Format(CultureInfo.InvariantCulture, T(text), values);
        public static bool HasEnglish(string text) => english.ContainsKey(text);
        public static int TranslationCount => english.Count;
        static Dictionary<string, string> LoadEnglish()
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("SarabDesktop.en.json"))
            {
                if (stream == null) throw new InvalidDataException("Missing embedded English language resource.");
                using (var reader = new StreamReader(stream, Encoding.UTF8)) return Json.From<Dictionary<string, string>>(reader.ReadToEnd());
            }
        }
    }
}
