using AbroadQs.Bot.Contracts;
using Microsoft.EntityFrameworkCore;

namespace AbroadQs.Bot.Data;

public sealed class BotStageRepository : IBotStageRepository
{
    private readonly ApplicationDbContext _db;

    public BotStageRepository(ApplicationDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    // --- Stages ---

    public async Task<BotStageDto?> GetByKeyAsync(string stageKey, CancellationToken cancellationToken = default)
    {
        var e = await _db.BotStages.AsNoTracking()
            .FirstOrDefaultAsync(x => x.StageKey == stageKey, cancellationToken).ConfigureAwait(false);
        return e == null ? null : ToDto(e);
    }

    public async Task<IReadOnlyList<BotStageDto>> ListAllAsync(CancellationToken cancellationToken = default)
    {
        return await _db.BotStages.AsNoTracking()
            .OrderBy(x => x.SortOrder).ThenBy(x => x.StageKey)
            .Select(x => new BotStageDto(x.Id, x.StageKey, x.TextFa, x.TextEn, x.IsEnabled, x.RequiredPermission, x.ParentStageKey, x.SortOrder))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<BotStageDto> CreateAsync(BotStageCreateDto dto, CancellationToken cancellationToken = default)
    {
        var entity = new BotStageEntity
        {
            StageKey = dto.StageKey,
            TextFa = dto.TextFa,
            TextEn = dto.TextEn,
            IsEnabled = dto.IsEnabled,
            RequiredPermission = dto.RequiredPermission,
            ParentStageKey = dto.ParentStageKey,
            SortOrder = dto.SortOrder
        };
        _db.BotStages.Add(entity);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToDto(entity);
    }

    public async Task<BotStageDto?> UpdateAsync(int id, BotStageUpdateDto dto, CancellationToken cancellationToken = default)
    {
        var entity = await _db.BotStages.FindAsync(new object[] { id }, cancellationToken).ConfigureAwait(false);
        if (entity == null) return null;
        if (dto.TextFa != null) entity.TextFa = dto.TextFa;
        if (dto.TextEn != null) entity.TextEn = dto.TextEn;
        if (dto.IsEnabled.HasValue) entity.IsEnabled = dto.IsEnabled.Value;
        if (dto.RequiredPermission != null) entity.RequiredPermission = dto.RequiredPermission == "" ? null : dto.RequiredPermission;
        if (dto.ParentStageKey != null) entity.ParentStageKey = dto.ParentStageKey == "" ? null : dto.ParentStageKey;
        if (dto.SortOrder.HasValue) entity.SortOrder = dto.SortOrder.Value;
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToDto(entity);
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var entity = await _db.BotStages.FindAsync(new object[] { id }, cancellationToken).ConfigureAwait(false);
        if (entity == null) return false;
        _db.BotStages.Remove(entity);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    // --- Buttons ---

    public async Task<IReadOnlyList<BotStageButtonDto>> GetButtonsAsync(string stageKey, CancellationToken cancellationToken = default)
    {
        var stage = await _db.BotStages.AsNoTracking()
            .FirstOrDefaultAsync(x => x.StageKey == stageKey, cancellationToken).ConfigureAwait(false);
        if (stage == null) return Array.Empty<BotStageButtonDto>();

        return await _db.BotStageButtons.AsNoTracking()
            .Where(x => x.StageId == stage.Id)
            .OrderBy(x => x.Row).ThenBy(x => x.Column)
            .Select(x => new BotStageButtonDto(x.Id, x.StageId, x.TextFa, x.TextEn, x.ButtonType,
                x.CallbackData, x.TargetStageKey, x.Url, x.Row, x.Column, x.IsEnabled, x.RequiredPermission))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<BotStageButtonDto> CreateButtonAsync(int stageId, BotStageButtonCreateDto dto, CancellationToken cancellationToken = default)
    {
        var entity = new BotStageButtonEntity
        {
            StageId = stageId,
            TextFa = dto.TextFa,
            TextEn = dto.TextEn,
            ButtonType = dto.ButtonType,
            CallbackData = dto.CallbackData,
            TargetStageKey = dto.TargetStageKey,
            Url = dto.Url,
            Row = dto.Row,
            Column = dto.Column,
            IsEnabled = dto.IsEnabled,
            RequiredPermission = dto.RequiredPermission
        };
        _db.BotStageButtons.Add(entity);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToBtnDto(entity);
    }

    public async Task<BotStageButtonDto?> UpdateButtonAsync(int buttonId, BotStageButtonUpdateDto dto, CancellationToken cancellationToken = default)
    {
        var entity = await _db.BotStageButtons.FindAsync(new object[] { buttonId }, cancellationToken).ConfigureAwait(false);
        if (entity == null) return null;
        if (dto.TextFa != null) entity.TextFa = dto.TextFa;
        if (dto.TextEn != null) entity.TextEn = dto.TextEn;
        if (dto.ButtonType != null) entity.ButtonType = dto.ButtonType;
        if (dto.CallbackData != null) entity.CallbackData = dto.CallbackData == "" ? null : dto.CallbackData;
        if (dto.TargetStageKey != null) entity.TargetStageKey = dto.TargetStageKey == "" ? null : dto.TargetStageKey;
        if (dto.Url != null) entity.Url = dto.Url == "" ? null : dto.Url;
        if (dto.Row.HasValue) entity.Row = dto.Row.Value;
        if (dto.Column.HasValue) entity.Column = dto.Column.Value;
        if (dto.IsEnabled.HasValue) entity.IsEnabled = dto.IsEnabled.Value;
        if (dto.RequiredPermission != null) entity.RequiredPermission = dto.RequiredPermission == "" ? null : dto.RequiredPermission;
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToBtnDto(entity);
    }

    public async Task<bool> DeleteButtonAsync(int buttonId, CancellationToken cancellationToken = default)
    {
        var entity = await _db.BotStageButtons.FindAsync(new object[] { buttonId }, cancellationToken).ConfigureAwait(false);
        if (entity == null) return false;
        _db.BotStageButtons.Remove(entity);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    // --- Mappers ---

    private static BotStageDto ToDto(BotStageEntity e) =>
        new(e.Id, e.StageKey, e.TextFa, e.TextEn, e.IsEnabled, e.RequiredPermission, e.ParentStageKey, e.SortOrder);

    private static BotStageButtonDto ToBtnDto(BotStageButtonEntity e) =>
        new(e.Id, e.StageId, e.TextFa, e.TextEn, e.ButtonType, e.CallbackData, e.TargetStageKey, e.Url, e.Row, e.Column, e.IsEnabled, e.RequiredPermission);
}
