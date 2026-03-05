namespace AbroadQs.Bot.Data;

public sealed class RemoteServerAuditEntity
{
    public long Id { get; set; }
    public int ServerId { get; set; }
    public long ActorTelegramUserId { get; set; }
    public string ActionType { get; set; } = "";
    public string? CommandText { get; set; }
    public bool Success { get; set; }
    public int? ExitCode { get; set; }
    public long? DurationMs { get; set; }
    public string? OutputPreview { get; set; }
    public string? ErrorMessage { get; set; }
    public string? MetadataJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
