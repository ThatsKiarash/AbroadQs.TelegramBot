namespace AbroadQs.Bot.Contracts;

/// <summary>
/// Shared bilingual text utility. Use L(fa, en, lang) to pick the correct string.
/// </summary>
public static class BilingualHelper
{
    /// <summary>Returns the Farsi or English text based on the user's language.</summary>
    public static string L(string fa, string en, string? lang)
        => (lang ?? "fa") == "fa" ? fa : en;

    /// <summary>Returns true if the user prefers Farsi.</summary>
    public static bool IsFa(string? lang) => (lang ?? "fa") == "fa";

    /// <summary>Returns true if the user prefers Farsi (from TelegramUserDto).</summary>
    public static bool IsFa(TelegramUserDto? user) => IsFa(user?.PreferredLanguage);
}
