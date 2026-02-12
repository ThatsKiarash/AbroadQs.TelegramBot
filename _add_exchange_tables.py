import paramiko, sys
sys.stdout.reconfigure(encoding='utf-8')

host = "167.235.159.117"
port = 2200
user = "root"
password = "Kia135724!"

ssh = paramiko.SSHClient()
ssh.set_missing_host_key_policy(paramiko.AutoAddPolicy())
print(f"Connecting to {host}:{port}...")
ssh.connect(host, port=port, username=user, password=password, timeout=30)
print("Connected!")

sa_password = "YourStrong@Pass123"

sql_commands = [
    # Create ExchangeRequests table
    """
    IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ExchangeRequests')
    BEGIN
        CREATE TABLE ExchangeRequests (
            Id INT IDENTITY(1,1) PRIMARY KEY,
            RequestNumber INT NOT NULL,
            TelegramUserId BIGINT NOT NULL,
            Currency NVARCHAR(20) NOT NULL,
            TransactionType NVARCHAR(20) NOT NULL,
            DeliveryMethod NVARCHAR(20) NOT NULL,
            AccountType NVARCHAR(20) NULL,
            Country NVARCHAR(100) NULL,
            Amount DECIMAL(18,2) NOT NULL,
            ProposedRate DECIMAL(18,2) NOT NULL,
            Description NVARCHAR(1000) NULL,
            FeePercent DECIMAL(5,2) NOT NULL DEFAULT 0,
            FeeAmount DECIMAL(18,2) NOT NULL DEFAULT 0,
            TotalAmount DECIMAL(18,2) NOT NULL DEFAULT 0,
            Status NVARCHAR(30) NOT NULL DEFAULT 'pending_approval',
            ChannelMessageId INT NULL,
            AdminNote NVARCHAR(1000) NULL,
            UserDisplayName NVARCHAR(256) NULL,
            CreatedAt DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
            UpdatedAt DATETIMEOFFSET NULL
        );
        CREATE UNIQUE INDEX IX_ExchangeRequests_RequestNumber ON ExchangeRequests(RequestNumber);
        CREATE INDEX IX_ExchangeRequests_TelegramUserId ON ExchangeRequests(TelegramUserId);
        CREATE INDEX IX_ExchangeRequests_Status ON ExchangeRequests(Status);
        PRINT 'ExchangeRequests table created.';
    END
    ELSE
        PRINT 'ExchangeRequests table already exists.';
    """,
    # Create ExchangeRates table
    """
    IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ExchangeRates')
    BEGIN
        CREATE TABLE ExchangeRates (
            Id INT IDENTITY(1,1) PRIMARY KEY,
            CurrencyCode NVARCHAR(50) NOT NULL,
            CurrencyNameFa NVARCHAR(100) NULL,
            CurrencyNameEn NVARCHAR(100) NULL,
            Rate DECIMAL(18,2) NOT NULL DEFAULT 0,
            Change DECIMAL(18,2) NOT NULL DEFAULT 0,
            Source NVARCHAR(20) NOT NULL DEFAULT 'manual',
            LastUpdatedAt DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET()
        );
        CREATE UNIQUE INDEX IX_ExchangeRates_CurrencyCode ON ExchangeRates(CurrencyCode);
        PRINT 'ExchangeRates table created.';
    END
    ELSE
        PRINT 'ExchangeRates table already exists.';
    """
]

for i, sql in enumerate(sql_commands, 1):
    print(f"\n--- SQL Command {i} ---")
    # Escape single quotes for bash
    escaped_sql = sql.replace("'", "'\\''")
    cmd = f"""docker exec abroadqs-sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U SA -P '{sa_password}' -d AbroadQsBot -C -Q '{escaped_sql}'"""
    stdin, stdout, stderr = ssh.exec_command(cmd, timeout=60)
    out = stdout.read().decode('utf-8', errors='replace')
    err = stderr.read().decode('utf-8', errors='replace')
    exit_code = stdout.channel.recv_exit_status()
    if out.strip():
        print(out.strip())
    if err.strip():
        print(f"STDERR: {err.strip()}")
    print(f"Exit code: {exit_code}")

ssh.close()
print("\nDone!")
