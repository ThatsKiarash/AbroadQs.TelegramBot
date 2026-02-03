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
    }
}
