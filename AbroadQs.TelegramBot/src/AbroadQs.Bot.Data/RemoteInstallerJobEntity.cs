namespace AbroadQs.Bot.Data;

public sealed class RemoteInstallerJobEntity
{
    public long Id { get; set; }
    public int ServerId { get; set; }
    public long ActorTelegramUserId { get; set; }
    public string JobType { get; set; } = "";
    public string Status { get; set; } = "queued";
    public string? RequestJson { get; set; }
    public string? LogText { get; set; }
    public string? ResultJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
}
