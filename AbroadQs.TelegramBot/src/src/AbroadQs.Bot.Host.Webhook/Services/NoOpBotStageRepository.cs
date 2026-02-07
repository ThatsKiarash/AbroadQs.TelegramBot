using AbroadQs.Bot.Contracts;

namespace AbroadQs.Bot.Host.Webhook.Services;

/// <summary>
/// No-op implementation when SQL Server is not configured.
/// </summary>
public sealed class NoOpBotStageRepository : IBotStageRepository
{
    public Task<BotStageDto?> GetByKeyAsync(string stageKey, CancellationToken cancellationToken = default)
        => Task.FromResult<BotStageDto?>(null);

    public Task<IReadOnlyList<BotStageDto>> ListAllAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<BotStageDto>>(Array.Empty<BotStageDto>());

    public Task<BotStageDto> CreateAsync(BotStageCreateDto dto, CancellationToken cancellationToken = default)
        => Task.FromResult(new BotStageDto(0, dto.StageKey, dto.TextFa, dto.TextEn, dto.IsEnabled, dto.RequiredPermission, dto.ParentStageKey, dto.SortOrder));

    public Task<BotStageDto?> UpdateAsync(int id, BotStageUpdateDto dto, CancellationToken cancellationToken = default)
        => Task.FromResult<BotStageDto?>(null);

    public Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
        => Task.FromResult(false);

    public Task<IReadOnlyList<BotStageButtonDto>> GetButtonsAsync(string stageKey, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<BotStageButtonDto>>(Array.Empty<BotStageButtonDto>());

    public Task<BotStageButtonDto> CreateButtonAsync(int stageId, BotStageButtonCreateDto dto, CancellationToken cancellationToken = default)
        => Task.FromResult(new BotStageButtonDto(0, stageId, dto.TextFa, dto.TextEn, dto.ButtonType, dto.CallbackData, dto.TargetStageKey, dto.Url, dto.Row, dto.Column, dto.IsEnabled, dto.RequiredPermission));

    public Task<BotStageButtonDto?> UpdateButtonAsync(int buttonId, BotStageButtonUpdateDto dto, CancellationToken cancellationToken = default)
        => Task.FromResult<BotStageButtonDto?>(null);

    public Task<bool> DeleteButtonAsync(int buttonId, CancellationToken cancellationToken = default)
        => Task.FromResult(false);
}
