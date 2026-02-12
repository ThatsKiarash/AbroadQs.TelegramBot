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
    public DbSet<ExchangeRequestEntity> ExchangeRequests => Set<ExchangeRequestEntity>();
    public DbSet<ExchangeRateEntity> ExchangeRates => Set<ExchangeRateEntity>();
    public DbSet<WalletEntity> Wallets => Set<WalletEntity>();
    public DbSet<WalletTransactionEntity> WalletTransactions => Set<WalletTransactionEntity>();
    public DbSet<PaymentEntity> Payments => Set<PaymentEntity>();
    public DbSet<AdBidEntity> AdBids => Set<AdBidEntity>();
    public DbSet<ExchangeGroupEntity> ExchangeGroups => Set<ExchangeGroupEntity>();

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
            e.Property(x => x.PhoneNumber).HasMaxLength(20);
            e.Property(x => x.VerificationPhotoFileId).HasMaxLength(256);
            e.Property(x => x.Email).HasMaxLength(256);
            e.Property(x => x.Country).HasMaxLength(100);
            e.Property(x => x.KycStatus).HasMaxLength(20);
            e.Property(x => x.KycRejectionData).HasMaxLength(2000);
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

        modelBuilder.Entity<ExchangeRequestEntity>(e =>
        {
            e.ToTable("ExchangeRequests");
            e.HasKey(x => x.Id);
            e.Property(x => x.TelegramUserId).IsRequired();
            e.Property(x => x.Currency).IsRequired().HasMaxLength(20);
            e.Property(x => x.TransactionType).IsRequired().HasMaxLength(20);
            e.Property(x => x.DeliveryMethod).IsRequired().HasMaxLength(20);
            e.Property(x => x.AccountType).HasMaxLength(20);
            e.Property(x => x.Country).HasMaxLength(100);
            e.Property(x => x.Amount).HasColumnType("decimal(18,2)");
            e.Property(x => x.ProposedRate).HasColumnType("decimal(18,2)");
            e.Property(x => x.Description).HasMaxLength(1000);
            e.Property(x => x.FeePercent).HasColumnType("decimal(5,2)");
            e.Property(x => x.FeeAmount).HasColumnType("decimal(18,2)");
            e.Property(x => x.TotalAmount).HasColumnType("decimal(18,2)");
            e.Property(x => x.Status).IsRequired().HasMaxLength(30);
            e.Property(x => x.AdminNote).HasMaxLength(1000);
            e.Property(x => x.UserDisplayName).HasMaxLength(256);
            // New differentiated flow fields
            e.Property(x => x.DestinationCurrency).HasMaxLength(20);
            e.Property(x => x.City).HasMaxLength(100);
            e.Property(x => x.MeetingPreference).HasMaxLength(500);
            e.Property(x => x.PaypalEmail).HasMaxLength(256);
            e.Property(x => x.Iban).HasMaxLength(50);
            e.Property(x => x.BankName).HasMaxLength(100);
            e.HasIndex(x => x.TelegramUserId);
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.RequestNumber).IsUnique();
        });

        modelBuilder.Entity<ExchangeRateEntity>(e =>
        {
            e.ToTable("ExchangeRates");
            e.HasKey(x => x.Id);
            e.Property(x => x.CurrencyCode).IsRequired().HasMaxLength(50);
            e.HasIndex(x => x.CurrencyCode).IsUnique();
            e.Property(x => x.CurrencyNameFa).HasMaxLength(100);
            e.Property(x => x.CurrencyNameEn).HasMaxLength(100);
            e.Property(x => x.Rate).HasColumnType("decimal(18,2)");
            e.Property(x => x.Change).HasColumnType("decimal(18,2)");
            e.Property(x => x.Source).IsRequired().HasMaxLength(20);
        });

        modelBuilder.Entity<WalletEntity>(e =>
        {
            e.ToTable("Wallets");
            e.HasKey(x => x.Id);
            e.Property(x => x.TelegramUserId).IsRequired();
            e.HasIndex(x => x.TelegramUserId).IsUnique();
            e.Property(x => x.Balance).HasColumnType("decimal(18,2)");
        });

        modelBuilder.Entity<WalletTransactionEntity>(e =>
        {
            e.ToTable("WalletTransactions");
            e.HasKey(x => x.Id);
            e.Property(x => x.Amount).HasColumnType("decimal(18,2)");
            e.Property(x => x.Type).IsRequired().HasMaxLength(20);
            e.Property(x => x.Description).HasMaxLength(500);
            e.Property(x => x.ReferenceId).HasMaxLength(100);
            e.HasOne(x => x.Wallet).WithMany().HasForeignKey(x => x.WalletId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.WalletId);
        });

        modelBuilder.Entity<PaymentEntity>(e =>
        {
            e.ToTable("Payments");
            e.HasKey(x => x.Id);
            e.Property(x => x.TelegramUserId).IsRequired();
            e.Property(x => x.Amount).HasColumnType("decimal(18,2)");
            e.Property(x => x.GatewayName).IsRequired().HasMaxLength(50);
            e.Property(x => x.GatewayTransactionId).HasMaxLength(100);
            e.Property(x => x.Status).IsRequired().HasMaxLength(30);
            e.Property(x => x.Purpose).HasMaxLength(50);
            e.Property(x => x.ReferenceId).HasMaxLength(100);
            e.HasIndex(x => x.TelegramUserId);
            e.HasIndex(x => x.GatewayIdGet);
            e.HasIndex(x => x.Status);
        });

        modelBuilder.Entity<AdBidEntity>(e =>
        {
            e.ToTable("AdBids");
            e.HasKey(x => x.Id);
            e.Property(x => x.BidderTelegramUserId).IsRequired();
            e.Property(x => x.BidderDisplayName).HasMaxLength(256);
            e.Property(x => x.BidAmount).HasColumnType("decimal(18,2)");
            e.Property(x => x.BidRate).HasColumnType("decimal(18,2)");
            e.Property(x => x.Message).HasMaxLength(500);
            e.Property(x => x.Status).IsRequired().HasMaxLength(20);
            e.HasOne(x => x.ExchangeRequest).WithMany().HasForeignKey(x => x.ExchangeRequestId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.ExchangeRequestId);
            e.HasIndex(x => x.BidderTelegramUserId);
        });

        modelBuilder.Entity<ExchangeGroupEntity>(e =>
        {
            e.ToTable("ExchangeGroups");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(256);
            e.Property(x => x.TelegramGroupLink).IsRequired().HasMaxLength(512);
            e.Property(x => x.GroupType).IsRequired().HasMaxLength(20);
            e.Property(x => x.CurrencyCode).HasMaxLength(20);
            e.Property(x => x.CountryCode).HasMaxLength(10);
            e.Property(x => x.Description).HasMaxLength(1000);
            e.Property(x => x.Status).IsRequired().HasMaxLength(20);
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.GroupType);
        });
    }
}
