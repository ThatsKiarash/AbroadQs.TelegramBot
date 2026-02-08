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
    public DbSet<MessageEntity> Messages => Set<MessageEntity>();
    public DbSet<UserMessageStateEntity> UserMessageStates => Set<UserMessageStateEntity>();
    public DbSet<BotStageEntity> BotStages => Set<BotStageEntity>();
    public DbSet<BotStageButtonEntity> BotStageButtons => Set<BotStageButtonEntity>();
    public DbSet<PermissionEntity> Permissions => Set<PermissionEntity>();
    public DbSet<UserPermissionEntity> UserPermissions => Set<UserPermissionEntity>();

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
            e.Property(x => x.PreferredLanguage).HasMaxLength(10);
            e.Property(x => x.CleanChatMode).HasDefaultValue(true);
        });

        modelBuilder.Entity<SettingEntity>(e =>
        {
            e.ToTable("Settings");
            e.HasKey(x => x.Id);
            e.Property(x => x.Key).IsRequired().HasMaxLength(256);
            e.Property(x => x.Value);
            e.HasIndex(x => x.Key).IsUnique();
        });

        modelBuilder.Entity<MessageEntity>(e =>
        {
            e.ToTable("Messages");
            e.HasKey(x => x.Id);
            e.Property(x => x.TelegramMessageId).IsRequired();
            e.Property(x => x.TelegramChatId).IsRequired();
            e.Property(x => x.Text).HasMaxLength(4096);
            e.Property(x => x.MessageType).HasMaxLength(50);
            e.Property(x => x.InlineKeyboardId).HasMaxLength(500);
            e.HasIndex(x => new { x.TelegramChatId, x.TelegramMessageId });
            e.HasIndex(x => x.TelegramUserId);
            e.HasIndex(x => x.SentAt);
            e.HasOne(x => x.ReplyToMessage)
                .WithMany()
                .HasForeignKey(x => x.ReplyToMessageId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.TelegramUserId)
                .HasPrincipalKey(x => x.TelegramUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<UserMessageStateEntity>(e =>
        {
            e.ToTable("UserMessageStates");
            e.HasKey(x => x.Id);
            e.Property(x => x.TelegramUserId).IsRequired();
            e.Property(x => x.LastAction).HasMaxLength(50);
            e.HasIndex(x => x.TelegramUserId).IsUnique();
            e.HasOne(x => x.LastBotMessage)
                .WithMany()
                .HasForeignKey(x => x.LastBotMessageId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.TelegramUserId)
                .HasPrincipalKey(x => x.TelegramUserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<BotStageEntity>(e =>
        {
            e.ToTable("BotStages");
            e.HasKey(x => x.Id);
            e.Property(x => x.StageKey).IsRequired().HasMaxLength(100);
            e.HasIndex(x => x.StageKey).IsUnique();
            e.Property(x => x.TextFa).HasMaxLength(4096);
            e.Property(x => x.TextEn).HasMaxLength(4096);
            e.Property(x => x.RequiredPermission).HasMaxLength(100);
            e.Property(x => x.ParentStageKey).HasMaxLength(100);
            e.HasMany(x => x.Buttons)
                .WithOne(x => x.Stage)
                .HasForeignKey(x => x.StageId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<BotStageButtonEntity>(e =>
        {
            e.ToTable("BotStageButtons");
            e.HasKey(x => x.Id);
            e.Property(x => x.TextFa).HasMaxLength(256);
            e.Property(x => x.TextEn).HasMaxLength(256);
            e.Property(x => x.ButtonType).IsRequired().HasMaxLength(20);
            e.Property(x => x.CallbackData).HasMaxLength(256);
            e.Property(x => x.TargetStageKey).HasMaxLength(100);
            e.Property(x => x.Url).HasMaxLength(1024);
            e.Property(x => x.RequiredPermission).HasMaxLength(100);
            e.HasIndex(x => new { x.StageId, x.Row, x.Column });
        });

        modelBuilder.Entity<PermissionEntity>(e =>
        {
            e.ToTable("Permissions");
            e.HasKey(x => x.Id);
            e.Property(x => x.PermissionKey).IsRequired().HasMaxLength(100);
            e.HasIndex(x => x.PermissionKey).IsUnique();
            e.Property(x => x.NameFa).HasMaxLength(128);
            e.Property(x => x.NameEn).HasMaxLength(128);
            e.Property(x => x.Description).HasMaxLength(500);
        });

        modelBuilder.Entity<UserPermissionEntity>(e =>
        {
            e.ToTable("UserPermissions");
            e.HasKey(x => x.Id);
            e.Property(x => x.TelegramUserId).IsRequired();
            e.Property(x => x.PermissionKey).IsRequired().HasMaxLength(100);
            e.HasIndex(x => new { x.TelegramUserId, x.PermissionKey }).IsUnique();
            e.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.TelegramUserId)
                .HasPrincipalKey(x => x.TelegramUserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
