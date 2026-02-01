using Microsoft.EntityFrameworkCore;

namespace AbroadQs.Bot.Data;

public sealed class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<TelegramUserEntity> TelegramUsers => Set<TelegramUserEntity>();
    public DbSet<SettingEntity> Settings => Set<SettingEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TelegramUserEntity>(e =>
        {
            e.ToTable("TelegramUsers");
            e.HasKey(x => x.Id);
            e.Property(x => x.TelegramUserId).IsRequired();
            e.HasIndex(x => x.TelegramUserId).IsUnique();
            e.Property(x => x.Username).HasMaxLength(128);
            e.Property(x => x.FirstName).HasMaxLength(128);
            e.Property(x => x.LastName).HasMaxLength(128);
        });

        modelBuilder.Entity<SettingEntity>(e =>
        {
            e.ToTable("Settings");
            e.HasKey(x => x.Id);
            e.Property(x => x.Key).IsRequired().HasMaxLength(256);
            e.Property(x => x.Value);
            e.HasIndex(x => x.Key).IsUnique();
        });
    }
}
