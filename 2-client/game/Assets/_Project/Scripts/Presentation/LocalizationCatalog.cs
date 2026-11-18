using System;
using System.Collections.Generic;
using System.Globalization;

namespace Pogranichye.Client.Presentation
{
    /// <summary>
    /// Small, deterministic localization catalog. Missing translations fall back to Russian
    /// and then to the caller's natural-language fallback; localization keys are never shown.
    /// This is the first migration slice, not a claim that every client string is translated.
    /// </summary>
    public static class LocalizationCatalog
    {
        public const string Russian = "ru";
        public const string English = "en";
        public const string ChineseSimplified = "zh-Hans";
        public const string Arabic = "ar";
        public const string French = "fr";
        public const string German = "de";
        public const string Hindi = "hi";

        private static readonly string[] LocaleOrder =
        {
            Russian, English, ChineseSimplified, Arabic, French, German, Hindi
        };

        private static readonly Dictionary<string, string> LocaleNames =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { Russian, "Русский" },
                { English, "English" },
                { ChineseSimplified, "简体中文" },
                { Arabic, "العربية" },
                { French, "Français" },
                { German, "Deutsch" },
                { Hindi, "हिन्दी" }
            };

        private static readonly Dictionary<string, Dictionary<string, string>> Text =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal)
            {
                { Russian, new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        { "settings.title", "Настройки" },
                        { "settings.sound", "Звук" },
                        { "settings.graphics", "Графика" },
                        { "settings.controls", "Управление" },
                        { "settings.language", "Язык" },
                        { "settings.language_title", "Язык текста" },
                        { "settings.language_hint", "Язык сохраняется на этом устройстве. Часть старых надписей пока остаётся на русском." },
                        { "settings.language_changed", "Язык изменён." },
                        { "settings.language_restart", "Некоторые изменения текста появятся после повторного открытия окна." },
                        { "settings.rtl_pending", "Для арабского письма ещё настраивается направление текста." },
                        { "settings.reset", "Сбросить настройки" },
                        { "settings.master_volume", "Общая громкость" },
                        { "settings.effects_volume", "Эффекты: шаги, бой, магия" },
                        { "settings.ambience_volume", "Окружение: ветер, птицы, вода, костёр" },
                        { "settings.mouse_sensitivity", "Чувствительность мыши" },
                        { "settings.invert_mouse", "Инвертировать вертикальный обзор" },
                        { "settings.fullscreen", "Полноэкранный режим" },
                        { "settings.windowed", "Перейти в оконный режим" },
                        { "login.credentials", "Введите учётные данные." },
                        { "status.waiting_server", "Ожидаем ответа…" },
                        { "status.server_confirmed", "Подтверждено." },
                        { "status.server_rejected", "Не удалось выполнить действие." }
                    }
                },
                { English, new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        { "settings.title", "Settings" }, { "settings.sound", "Sound" },
                        { "settings.graphics", "Graphics" }, { "settings.controls", "Controls" },
                        { "settings.language", "Language" }, { "settings.language_title", "Text language" },
                        { "settings.language_hint", "Your choice is saved on this device. Some older text is still being translated." },
                        { "settings.language_changed", "Language changed." },
                        { "settings.language_restart", "Some text updates after reopening this panel." },
                        { "settings.rtl_pending", "Right-to-left text layout for Arabic is still being completed." },
                        { "settings.reset", "Reset settings" }, { "settings.master_volume", "Master volume" },
                        { "settings.effects_volume", "Effects: footsteps, combat, magic" },
                        { "settings.ambience_volume", "Ambience: wind, birds, water, fire" },
                        { "settings.mouse_sensitivity", "Mouse sensitivity" },
                        { "settings.invert_mouse", "Invert vertical look" },
                        { "settings.fullscreen", "Fullscreen" }, { "settings.windowed", "Switch to windowed mode" },
                        { "login.credentials", "Enter your credentials." },
                        { "status.waiting_server", "Waiting for a response…" },
                        { "status.server_confirmed", "Confirmed." },
                        { "status.server_rejected", "The action could not be completed." }
                    }
                },
                { ChineseSimplified, new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        { "settings.title", "设置" }, { "settings.sound", "声音" },
                        { "settings.graphics", "画面" }, { "settings.controls", "操作" },
                        { "settings.language", "语言" }, { "settings.language_title", "界面语言" },
                        { "settings.language_hint", "选择会保存在此设备上。部分旧文本仍在翻译中。" },
                        { "settings.language_changed", "语言已更改。" },
                        { "settings.language_restart", "重新打开此面板后，部分文本会更新。" },
                        { "settings.rtl_pending", "阿拉伯语的从右向左排版仍在完善中。" },
                        { "settings.reset", "重置设置" }, { "settings.master_volume", "主音量" },
                        { "settings.effects_volume", "音效：脚步、战斗、魔法" },
                        { "settings.ambience_volume", "环境音：风、鸟、水、火" },
                        { "settings.mouse_sensitivity", "鼠标灵敏度" },
                        { "settings.invert_mouse", "反转垂直视角" },
                        { "settings.fullscreen", "全屏模式" }, { "settings.windowed", "切换到窗口模式" },
                        { "login.credentials", "请输入账号信息。" },
                        { "status.waiting_server", "正在等待回应…" },
                        { "status.server_confirmed", "已确认。" },
                        { "status.server_rejected", "无法完成此操作。" }
                    }
                },
                { Arabic, new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        { "settings.title", "الإعدادات" }, { "settings.sound", "الصوت" },
                        { "settings.graphics", "الرسوميات" }, { "settings.controls", "عناصر التحكم" },
                        { "settings.language", "اللغة" }, { "settings.language_title", "لغة النص" },
                        { "settings.language_hint", "يُحفظ اختيارك على هذا الجهاز. ما زالت بعض النصوص القديمة قيد الترجمة." },
                        { "settings.language_changed", "تم تغيير اللغة." },
                        { "settings.language_restart", "ستتحدث بعض النصوص بعد إعادة فتح هذه اللوحة." },
                        { "settings.rtl_pending", "ما زال تنسيق النص العربي من اليمين إلى اليسار قيد الاستكمال." },
                        { "settings.reset", "إعادة ضبط الإعدادات" }, { "settings.master_volume", "مستوى الصوت العام" },
                        { "settings.effects_volume", "المؤثرات: الخطوات والقتال والسحر" },
                        { "settings.ambience_volume", "الأجواء: الرياح والطيور والماء والنار" },
                        { "settings.mouse_sensitivity", "حساسية الفأرة" },
                        { "settings.invert_mouse", "عكس اتجاه النظر العمودي" },
                        { "settings.fullscreen", "ملء الشاشة" }, { "settings.windowed", "الانتقال إلى وضع النافذة" },
                        { "login.credentials", "أدخل بيانات الدخول." },
                        { "status.waiting_server", "بانتظار الرد…" },
                        { "status.server_confirmed", "تم التأكيد." },
                        { "status.server_rejected", "تعذر إكمال الإجراء." }
                    }
                },
                { French, new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        { "settings.title", "Paramètres" }, { "settings.sound", "Son" },
                        { "settings.graphics", "Graphismes" }, { "settings.controls", "Commandes" },
                        { "settings.language", "Langue" }, { "settings.language_title", "Langue du texte" },
                        { "settings.language_hint", "Votre choix est enregistré sur cet appareil. Certains anciens textes restent à traduire." },
                        { "settings.language_changed", "Langue modifiée." },
                        { "settings.language_restart", "Certains textes seront actualisés à la réouverture de ce panneau." },
                        { "settings.rtl_pending", "La mise en page de droite à gauche pour l’arabe est encore en cours." },
                        { "settings.reset", "Réinitialiser les paramètres" }, { "settings.master_volume", "Volume général" },
                        { "settings.effects_volume", "Effets : pas, combat, magie" },
                        { "settings.ambience_volume", "Ambiance : vent, oiseaux, eau, feu" },
                        { "settings.mouse_sensitivity", "Sensibilité de la souris" },
                        { "settings.invert_mouse", "Inverser la vue verticale" },
                        { "settings.fullscreen", "Plein écran" }, { "settings.windowed", "Passer en mode fenêtré" },
                        { "login.credentials", "Saisissez vos identifiants." },
                        { "status.waiting_server", "En attente d’une réponse…" },
                        { "status.server_confirmed", "Confirmé." },
                        { "status.server_rejected", "L’action n’a pas pu être effectuée." }
                    }
                },
                { German, new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        { "settings.title", "Einstellungen" }, { "settings.sound", "Ton" },
                        { "settings.graphics", "Grafik" }, { "settings.controls", "Steuerung" },
                        { "settings.language", "Sprache" }, { "settings.language_title", "Sprache der Texte" },
                        { "settings.language_hint", "Die Auswahl wird auf diesem Gerät gespeichert. Einige ältere Texte werden noch übersetzt." },
                        { "settings.language_changed", "Sprache geändert." },
                        { "settings.language_restart", "Einige Texte werden nach erneutem Öffnen dieses Fensters aktualisiert." },
                        { "settings.rtl_pending", "Die Rechts-nach-links-Darstellung für Arabisch wird noch fertiggestellt." },
                        { "settings.reset", "Einstellungen zurücksetzen" }, { "settings.master_volume", "Gesamtlautstärke" },
                        { "settings.effects_volume", "Effekte: Schritte, Kampf, Magie" },
                        { "settings.ambience_volume", "Umgebung: Wind, Vögel, Wasser, Feuer" },
                        { "settings.mouse_sensitivity", "Mausempfindlichkeit" },
                        { "settings.invert_mouse", "Vertikale Sicht umkehren" },
                        { "settings.fullscreen", "Vollbild" }, { "settings.windowed", "Zum Fenstermodus wechseln" },
                        { "login.credentials", "Zugangsdaten eingeben." },
                        { "status.waiting_server", "Warte auf Antwort…" },
                        { "status.server_confirmed", "Bestätigt." },
                        { "status.server_rejected", "Die Aktion konnte nicht ausgeführt werden." }
                    }
                },
                { Hindi, new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        { "settings.title", "सेटिंग्स" }, { "settings.sound", "ध्वनि" },
                        { "settings.graphics", "ग्राफ़िक्स" }, { "settings.controls", "नियंत्रण" },
                        { "settings.language", "भाषा" }, { "settings.language_title", "पाठ की भाषा" },
                        { "settings.language_hint", "आपकी पसंद इस डिवाइस पर सहेजी जाती है। कुछ पुराने पाठों का अनुवाद अभी बाकी है।" },
                        { "settings.language_changed", "भाषा बदल दी गई है।" },
                        { "settings.language_restart", "इस पैनल को दोबारा खोलने पर कुछ पाठ अपडेट होंगे।" },
                        { "settings.rtl_pending", "अरबी के लिए दाएँ-से-बाएँ पाठ विन्यास पर अभी काम चल रहा है।" },
                        { "settings.reset", "सेटिंग्स रीसेट करें" }, { "settings.master_volume", "मुख्य ध्वनि स्तर" },
                        { "settings.effects_volume", "प्रभाव: कदम, लड़ाई, जादू" },
                        { "settings.ambience_volume", "माहौल: हवा, पक्षी, पानी, आग" },
                        { "settings.mouse_sensitivity", "माउस संवेदनशीलता" },
                        { "settings.invert_mouse", "ऊर्ध्वाधर दृश्य उलटें" },
                        { "settings.fullscreen", "पूर्ण स्क्रीन" }, { "settings.windowed", "विंडो मोड पर जाएँ" },
                        { "login.credentials", "अपना लॉगिन विवरण दर्ज करें।" },
                        { "status.waiting_server", "उत्तर की प्रतीक्षा है…" },
                        { "status.server_confirmed", "पुष्टि हो गई।" },
                        { "status.server_rejected", "यह कार्रवाई पूरी नहीं हो सकी।" }
                    }
                }
            };

        public static string[] SupportedLocales
        {
            get { return (string[])LocaleOrder.Clone(); }
        }

        public static bool IsSupported(string locale)
        {
            return !string.IsNullOrEmpty(locale) && Text.ContainsKey(locale);
        }

        public static string LocaleName(string locale)
        {
            return LocaleNames.TryGetValue(locale ?? string.Empty, out var name) ? name : LocaleNames[Russian];
        }

        public static bool IsRightToLeft(string locale)
        {
            return string.Equals(locale, Arabic, StringComparison.Ordinal);
        }

        public static string Get(string locale, string key, string fallback = null)
        {
            if (string.IsNullOrEmpty(key)) return fallback ?? string.Empty;
            if (IsSupported(locale) && Text[locale].TryGetValue(key, out var translated))
                return translated;
            if (Text[Russian].TryGetValue(key, out var russian))
                return russian;
            return fallback ?? string.Empty;
        }

        public static string Format(string locale, string key, string fallback, params object[] arguments)
        {
            var culture = CultureFor(locale);
            return string.Format(culture, Get(locale, key, fallback), arguments ?? new object[0]);
        }

        public static CultureInfo CultureFor(string locale)
        {
            switch (locale)
            {
                case English: return CultureInfo.GetCultureInfo("en");
                case ChineseSimplified: return CultureInfo.GetCultureInfo("zh-Hans");
                case Arabic: return CultureInfo.GetCultureInfo("ar");
                case French: return CultureInfo.GetCultureInfo("fr");
                case German: return CultureInfo.GetCultureInfo("de");
                case Hindi: return CultureInfo.GetCultureInfo("hi");
                default: return CultureInfo.GetCultureInfo("ru");
            }
        }
    }
}
