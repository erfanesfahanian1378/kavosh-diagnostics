namespace Kavosh.Core;

/// <summary>UI language. Persian switches the whole layout to right-to-left.</summary>
public enum AppLanguage
{
    En,
    Fa,
}

public static class AppLanguageExtensions
{
    public static bool IsRightToLeft(this AppLanguage language) => language == AppLanguage.Fa;

    public static string NativeName(this AppLanguage language) => language == AppLanguage.Fa ? "فارسی" : "English";

    public static string ShortName(this AppLanguage language) => language == AppLanguage.Fa ? "فا" : "EN";
}

/// <summary>Holds the language currently shown; the app rebuilds its views when it changes.</summary>
public static class Loc
{
    public static AppLanguage Language { get; set; } = AppLanguage.En;

    /// <summary>Picks the English or Persian text for the current language.</summary>
    public static string T(string english, string persian) => Language == AppLanguage.Fa ? persian : english;
}
