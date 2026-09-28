using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;

namespace DsxLite.App;

public sealed class AppSettings
{
    public string? Language { get; set; }
}

/// <summary>Persists UI preferences under %APPDATA%\DsxLite\settings.json.</summary>
public static class SettingsStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DsxLite", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch { /* corrupted settings fall back to defaults */ }
        return new AppSettings();
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(settings));
        }
        catch { /* read-only profile etc. */ }
    }
}

/// <summary>
/// Runtime localization: one resource dictionary per language, swapped into the app
/// resources so every {DynamicResource} in XAML updates immediately. Code strings go
/// through <see cref="Get"/>/<see cref="Format"/>. Strings provided by the Core library
/// (trigger preset names, parameter labels) use their Chinese text as the lookup key.
/// </summary>
public static class Localization
{
    public static readonly (string Code, string Name)[] Languages =
    [
        ("zh-CN", "简体中文"),
        ("zh-TW", "繁體中文"),
        ("en-US", "English"),
        ("ja-JP", "日本語"),
        ("fr-FR", "Français"),
        ("es-ES", "Español"),
        ("pt-PT", "Português"),
        ("de-DE", "Deutsch"),
    ];

    public static string Current { get; private set; } = "en-US";

    /// <summary>Raised after a language switch so code-built content can refresh.</summary>
    public static event EventHandler? LanguageChanged;

    /// <summary>Applies the saved language, or the system language on first run.</summary>
    public static void Initialize()
    {
        string code = SettingsStore.Load().Language ?? DetectSystemLanguage();
        Apply(code, save: false);
    }

    public static string DetectSystemLanguage()
    {
        string name = CultureInfo.CurrentUICulture.Name; // e.g. zh-CN, zh-Hant-TW
        if (name.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
            return name.Contains("Hant", StringComparison.OrdinalIgnoreCase) ||
                   name is "zh-TW" or "zh-HK" or "zh-MO"
                ? "zh-TW"
                : "zh-CN";
        return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName switch
        {
            "ja" => "ja-JP",
            "fr" => "fr-FR",
            "es" => "es-ES",
            "pt" => "pt-PT",
            "de" => "de-DE",
            _ => "en-US",
        };
    }

    public static void Apply(string code, bool save = true)
    {
        if (Languages.All(l => l.Code != code))
            code = "en-US";

        var dict = new ResourceDictionary
        {
            Source = new Uri($"Resources/Strings.{code}.xaml", UriKind.Relative),
        };
        var merged = System.Windows.Application.Current.Resources.MergedDictionaries;
        for (int i = merged.Count - 1; i >= 0; i--)
            if (merged[i].Source?.OriginalString.Contains("Strings.") == true)
                merged.RemoveAt(i);
        merged.Add(dict);

        Current = code;
        if (save)
        {
            AppSettings settings = SettingsStore.Load();
            settings.Language = code;
            SettingsStore.Save(settings);
        }
        LanguageChanged?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>Looks up a localized string; missing keys fall back to the key itself.</summary>
    public static string Get(string key) =>
        System.Windows.Application.Current.TryFindResource(key) as string ?? key;

    public static string Format(string key, params object[] args) =>
        string.Format(Get(key), args);
}
