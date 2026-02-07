namespace AbroadQs.Bot.Contracts;

/// <summary>
/// CRUD for bot stages and their buttons.
/// </summary>
public interface IBotStageRepository
{
    // --- Stages ---
    Task<BotStageDto?> GetByKeyAsync(string stageKey, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BotStageDto>> ListAllAsync(CancellationToken cancellationToken = default);
    Task<BotStageDto> CreateAsync(BotStageCreateDto dto, CancellationToken cancellationToken = default);
    Task<BotStageDto?> UpdateAsync(int id, BotStageUpdateDto dto, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default);

    // --- Buttons ---
    Task<IReadOnlyList<BotStageButtonDto>> GetButtonsAsync(string stageKey, CancellationToken cancellationToken = default);
    Task<BotStageButtonDto> CreateButtonAsync(int stageId, BotStageButtonCreateDto dto, CancellationToken cancellationToken = default);
    Task<BotStageButtonDto?> UpdateButtonAsync(int buttonId, BotStageButtonUpdateDto dto, CancellationToken cancellationToken = default);
    Task<bool> DeleteButtonAsync(int buttonId, CancellationToken cancellationToken = default);
}

// --- Stage DTOs ---

public sealed record BotStageDto(
    int Id,
    string StageKey,
    string? TextFa,
    string? TextEn,
    bool IsEnabled,
    string? RequiredPermission,
    string? ParentStageKey,
    int SortOrder);

public sealed record BotStageCreateDto(
    string StageKey,
    string? TextFa,
    string? TextEn,
    bool IsEnabled,
    string? RequiredPermission,
    string? ParentStageKey,
    int SortOrder);

public sealed record BotStageUpdateDto(
    string? TextFa,
    string? TextEn,
    bool? IsEnabled,
    string? RequiredPermission,
    string? ParentStageKey,
    int? SortOrder);

// --- Button DTOs ---

public sealed record BotStageButtonDto(
    int Id,
    int StageId,
    string? TextFa,
    string? TextEn,
    string ButtonType,
    string? CallbackData,
    string? TargetStageKey,
    string? Url,
    int Row,
    int Column,
    bool IsEnabled,
    string? RequiredPermission);

public sealed record BotStageButtonCreateDto(
    string? TextFa,
    string? TextEn,
    string ButtonType,
    string? CallbackData,
    string? TargetStageKey,
    string? Url,
    int Row,
    int Column,
    bool IsEnabled,
    string? RequiredPermission);

public sealed record BotStageButtonUpdateDto(
    string? TextFa,
    string? TextEn,
    string? ButtonType,
    string? CallbackData,
    string? TargetStageKey,
    string? Url,
    int? Row,
    int? Column,
    bool? IsEnabled,
    string? RequiredPermission);
