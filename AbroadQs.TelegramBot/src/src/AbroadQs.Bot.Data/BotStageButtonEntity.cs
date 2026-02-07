namespace AbroadQs.Bot.Data;

/// <summary>
/// A button displayed within a <see cref="BotStageEntity"/>.
/// </summary>
public sealed class BotStageButtonEntity
{
    public int Id { get; set; }
    /// <summary>FK to BotStages.Id.</summary>
    public int StageId { get; set; }
    /// <summary>Farsi button label.</summary>
    public string? TextFa { get; set; }
    /// <summary>English button label.</summary>
    public string? TextEn { get; set; }
    /// <summary>"callback" or "url".</summary>
    public string ButtonType { get; set; } = "callback";
    /// <summary>Callback data sent when pressed, e.g. "stage:settings" or "lang:fa".</summary>
    public string? CallbackData { get; set; }
    /// <summary>If this button navigates to another stage, store the target key here.</summary>
    public string? TargetStageKey { get; set; }
    /// <summary>URL for link buttons.</summary>
    public string? Url { get; set; }
    /// <summary>Row number (0-based). Buttons with same Row appear on the same line.</summary>
    public int Row { get; set; }
    /// <summary>Column order within the row.</summary>
    public int Column { get; set; }
    /// <summary>Whether this button is currently enabled.</summary>
    public bool IsEnabled { get; set; } = true;
    /// <summary>Permission key required to see this button; null = visible to all.</summary>
    public string? RequiredPermission { get; set; }

    // Navigation
    public BotStageEntity? Stage { get; set; }
}
