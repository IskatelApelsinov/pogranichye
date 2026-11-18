using System;
using Pogranichye.Client.Presentation;

public static class LocalizationCatalogCheck
{
    public static int Main()
    {
        var locales = LocalizationCatalog.SupportedLocales;
        Require(locales.Length == 7, "expected seven supported locales");
        foreach (var locale in locales)
        {
            Require(LocalizationCatalog.IsSupported(locale), "locale missing: " + locale);
            Require(!string.IsNullOrWhiteSpace(LocalizationCatalog.Get(locale, "settings.title")),
                "settings title missing: " + locale);
            Require(!string.IsNullOrWhiteSpace(LocalizationCatalog.Get(locale, "settings.language_hint")),
                "language hint missing: " + locale);
            Require(!string.IsNullOrWhiteSpace(LocalizationCatalog.Get(locale, "settings.rtl_pending")),
                "RTL notice missing: " + locale);
            Require(LocalizationCatalog.CultureFor(locale) != null,
                "culture mapping missing: " + locale);
            Require(!string.IsNullOrWhiteSpace(LocalizationCatalog.Get(locale, "status.action_ok")),
                "success feedback missing: " + locale);
            Require(!string.IsNullOrWhiteSpace(LocalizationCatalog.Get(locale, "status.action_failed")),
                "failure feedback missing: " + locale);
            Require(!string.IsNullOrWhiteSpace(LocalizationCatalog.Get(locale, "status.action_wait")),
                "timeout feedback missing: " + locale);
        }

        Require(LocalizationCatalog.Get("en", "settings.title") == "Settings", "English lookup failed");
        Require(LocalizationCatalog.Get("zh-Hans", "settings.title") == "设置", "Chinese lookup failed");
        Require(LocalizationCatalog.Get("ar", "settings.title") == "الإعدادات", "Arabic lookup failed");
        Require(LocalizationCatalog.Get("fr", "settings.title") == "Paramètres", "French lookup failed");
        Require(LocalizationCatalog.Get("de", "settings.title") == "Einstellungen", "German lookup failed");
        Require(LocalizationCatalog.Get("hi", "settings.title") == "सेटिंग्स", "Hindi lookup failed");
        Require(LocalizationCatalog.Get("unknown", "settings.title") == "Настройки", "Russian fallback failed");
        Require(LocalizationCatalog.Get("en", "missing.key", "Natural fallback") == "Natural fallback",
            "caller fallback failed");
        Require(LocalizationCatalog.IsRightToLeft("ar"), "Arabic must be RTL");
        Require(!LocalizationCatalog.IsRightToLeft("en"), "English must be LTR");
        Console.WriteLine("Localization catalog checks passed.");
        return 0;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
