-- ============================================================
-- SQL script to add ExchangeRequests and ExchangeRates tables
-- Run this on the production server if EF migrations are not used.
-- ============================================================

-- Create ExchangeRequests table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ExchangeRequests')
BEGIN
    CREATE TABLE [ExchangeRequests] (
        [Id]              INT              IDENTITY(1,1) NOT NULL,
        [RequestNumber]   INT              NOT NULL,
        [TelegramUserId]  BIGINT           NOT NULL,
        [Currency]        NVARCHAR(20)     NOT NULL,
        [TransactionType] NVARCHAR(20)     NOT NULL,
        [DeliveryMethod]  NVARCHAR(20)     NOT NULL,
        [AccountType]     NVARCHAR(20)     NULL,
        [Country]         NVARCHAR(100)    NULL,
        [Amount]          DECIMAL(18,2)    NOT NULL,
        [ProposedRate]    DECIMAL(18,2)    NOT NULL,
        [Description]     NVARCHAR(1000)   NULL,
        [FeePercent]      DECIMAL(5,2)     NOT NULL,
        [FeeAmount]       DECIMAL(18,2)    NOT NULL,
        [TotalAmount]     DECIMAL(18,2)    NOT NULL,
        [Status]          NVARCHAR(30)     NOT NULL,
        [ChannelMessageId] INT             NULL,
        [AdminNote]       NVARCHAR(1000)   NULL,
        [UserDisplayName] NVARCHAR(256)    NULL,
        [CreatedAt]       DATETIMEOFFSET   NOT NULL,
        [UpdatedAt]       DATETIMEOFFSET   NULL,
        CONSTRAINT [PK_ExchangeRequests] PRIMARY KEY CLUSTERED ([Id] ASC)
    );

    CREATE UNIQUE NONCLUSTERED INDEX [IX_ExchangeRequests_RequestNumber]
        ON [ExchangeRequests] ([RequestNumber] ASC);

    CREATE NONCLUSTERED INDEX [IX_ExchangeRequests_TelegramUserId]
        ON [ExchangeRequests] ([TelegramUserId] ASC);

    CREATE NONCLUSTERED INDEX [IX_ExchangeRequests_Status]
        ON [ExchangeRequests] ([Status] ASC);

    PRINT 'Created table ExchangeRequests with indexes.';
END
ELSE
    PRINT 'Table ExchangeRequests already exists — skipping.';
GO

-- Create ExchangeRates table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ExchangeRates')
BEGIN
    CREATE TABLE [ExchangeRates] (
        [Id]              INT              IDENTITY(1,1) NOT NULL,
        [CurrencyCode]    NVARCHAR(50)     NOT NULL,
        [CurrencyNameFa]  NVARCHAR(100)    NULL,
        [CurrencyNameEn]  NVARCHAR(100)    NULL,
        [Rate]            DECIMAL(18,2)    NOT NULL,
        [Change]          DECIMAL(18,2)    NOT NULL,
        [Source]          NVARCHAR(20)     NOT NULL,
        [LastUpdatedAt]   DATETIMEOFFSET   NOT NULL,
        CONSTRAINT [PK_ExchangeRates] PRIMARY KEY CLUSTERED ([Id] ASC)
    );

    CREATE UNIQUE NONCLUSTERED INDEX [IX_ExchangeRates_CurrencyCode]
        ON [ExchangeRates] ([CurrencyCode] ASC);

    PRINT 'Created table ExchangeRates with indexes.';
END
ELSE
    PRINT 'Table ExchangeRates already exists — skipping.';
GO

-- Record migration in __EFMigrationsHistory (if using EF migrations table)
IF EXISTS (SELECT * FROM sys.tables WHERE name = '__EFMigrationsHistory')
BEGIN
    IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = '20260212100000_AddExchangeTables')
    BEGIN
        INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
        VALUES ('20260212100000_AddExchangeTables', '8.0.11');
        PRINT 'Recorded migration 20260212100000_AddExchangeTables in __EFMigrationsHistory.';
    END
END
GO

PRINT 'Exchange tables setup complete.';
GO
