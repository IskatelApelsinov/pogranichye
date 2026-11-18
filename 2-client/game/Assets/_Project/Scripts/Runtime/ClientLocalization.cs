using UnityEngine;
using Pogranichye.Client.Presentation;

namespace Pogranichye.Client
{
    /// <summary>Device-local language preference for client presentation.</summary>
    public static class ClientLocalization
    {
        private const string PreferenceKey = "Pogranichye.Locale";
        private static string _locale;

        public static string Locale
        {
            get
            {
                if (_locale == null)
                {
                    var saved = PlayerPrefs.GetString(PreferenceKey, LocalizationCatalog.Russian);
                    _locale = LocalizationCatalog.IsSupported(saved) ? saved : LocalizationCatalog.Russian;
                }
                return _locale;
            }
        }

        public static bool IsRightToLeft => LocalizationCatalog.IsRightToLeft(Locale);

        public static string Get(string key, string fallback = null)
        {
            return LocalizationCatalog.Get(Locale, key, fallback);
        }

        public static void SetLocale(string locale)
        {
            if (!LocalizationCatalog.IsSupported(locale)) return;
            _locale = locale;
            PlayerPrefs.SetString(PreferenceKey, locale);
            PlayerPrefs.Save();
        }

        public static string LocaleName(string locale)
        {
            return LocalizationCatalog.LocaleName(locale);
        }
    }
}
