namespace AbroadQs.Bot.Data;

/// <summary>
/// Represents a single "screen" / menu in the bot. Each stage has bilingual text and buttons.
/// </summary>
public sealed class BotStageEntity
{
    public int Id { get; set; }
    /// <summary>Unique key, e.g. "welcome", "main_menu", "settings", "lang_select".</summary>
    public string StageKey { get; set; } = "";
    /// <summary>Farsi text (HTML markup supported).</summary>
    public string? TextFa { get; set; }
    /// <summary>English text (HTML markup supported).</summary>
    public string? TextEn { get; set; }
    /// <summary>Whether this stage is currently enabled.</summary>
    public bool IsEnabled { get; set; } = true;
    /// <summary>Permission key required to access this stage; null = public.</summary>
    public string? RequiredPermission { get; set; }
    /// <summary>Parent stage key for auto back-button.</summary>
    public string? ParentStageKey { get; set; }
    /// <summary>Display order when listing stages.</summary>
    public int SortOrder { get; set; }

    // Navigation
    public ICollection<BotStageButtonEntity> Buttons { get; set; } = new List<BotStageButtonEntity>();
}
