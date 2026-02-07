using AbroadQs.Bot.Contracts;
using Microsoft.EntityFrameworkCore;

namespace AbroadQs.Bot.Data;

public sealed class SettingsRepository : ISettingsRepository
{
    private readonly ApplicationDbContext _db;

    public SettingsRepository(ApplicationDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public async Task<string?> GetValueAsync(string key, CancellationToken cancellationToken = default)
    {
        var entity = await _db.Settings
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Key == key, cancellationToken)
            .ConfigureAwait(false);
        return entity?.Value;
    }

    public async Task SetValueAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        var entity = await _db.Settings
            .FirstOrDefaultAsync(x => x.Key == key, cancellationToken)
            .ConfigureAwait(false);
        if (entity == null)
        {
            entity = new SettingEntity { Key = key, Value = value };
            _db.Settings.Add(entity);
        }
        else
        {
            entity.Value = value;
        }
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
