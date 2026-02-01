namespace AbroadQs.Bot.Contracts;

/// <summary>
/// Marker interface for bot modules. Used for assembly scanning and DI registration.
/// Each module project can implement this in a single class (e.g. CommonModule) for discovery.
/// </summary>
public interface IModuleMarker
{
    string ModuleName { get; }
}
