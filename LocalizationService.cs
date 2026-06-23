using System;
using System.Linq;
using System.Windows;

namespace ComputerTestApp
{
    internal static class LocalizationService
    {
        public static event EventHandler LanguageChanged;

        public static string CurrentLanguage { get; private set; } = "vi";

        public static void Initialize()
        {
            SetLanguage(UserSettings.Default.Language, false);
        }

        public static void SetLanguage(string language, bool save = true)
        {
            var normalizedLanguage = language == "en" ? "en" : "vi";
            var dictionaries = Application.Current.Resources.MergedDictionaries;
            var currentDictionary = dictionaries.FirstOrDefault(dictionary =>
                dictionary.Source?.OriginalString.IndexOf("Resources/Strings.", StringComparison.OrdinalIgnoreCase) >= 0);

            if (currentDictionary != null)
            {
                dictionaries.Remove(currentDictionary);
            }

            dictionaries.Add(new ResourceDictionary
            {
                Source = new Uri($"Resources/Strings.{normalizedLanguage}.xaml", UriKind.Relative)
            });
            CurrentLanguage = normalizedLanguage;
            LanguageChanged?.Invoke(null, EventArgs.Empty);

            if (!save) return;

            UserSettings.Default.Language = normalizedLanguage;
            UserSettings.Default.Save();
        }

        public static string Get(string key)
        {
            return Convert.ToString(Application.Current.TryFindResource(key)) ?? key;
        }

        public static string Format(string key, params object[] arguments)
        {
            return string.Format(Get(key), arguments);
        }
    }
}
