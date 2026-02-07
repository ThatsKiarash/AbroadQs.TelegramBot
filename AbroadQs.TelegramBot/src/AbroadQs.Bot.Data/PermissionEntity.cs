namespace AbroadQs.Bot.Data;

/// <summary>
/// Defines a permission that can be assigned to users.
/// </summary>
public sealed class PermissionEntity
{
    public int Id { get; set; }
    /// <summary>Unique key, e.g. "access_settings", "access_exchange", "access_admin".</summary>
    public string PermissionKey { get; set; } = "";
    /// <summary>Farsi display name.</summary>
    public string? NameFa { get; set; }
    /// <summary>English display name.</summary>
    public string? NameEn { get; set; }
    /// <summary>Optional description.</summary>
    public string? Description { get; set; }
}
