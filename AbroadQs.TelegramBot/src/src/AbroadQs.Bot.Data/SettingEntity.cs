namespace AbroadQs.Bot.Data;

public sealed class SettingEntity
{
    public int Id { get; set; }
    public string Key { get; set; } = "";
    public string? Value { get; set; }
}
