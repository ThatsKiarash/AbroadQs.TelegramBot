using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AbroadQs.Bot.Data.Migrations
{
    public partial class AddExchangeFlowFields : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DestinationCurrency", table: "ExchangeRequests",
                type: "nvarchar(20)", maxLength: 20, nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "City", table: "ExchangeRequests",
                type: "nvarchar(100)", maxLength: 100, nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MeetingPreference", table: "ExchangeRequests",
                type: "nvarchar(500)", maxLength: 500, nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaypalEmail", table: "ExchangeRequests",
                type: "nvarchar(256)", maxLength: 256, nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Iban", table: "ExchangeRequests",
                type: "nvarchar(50)", maxLength: 50, nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BankName", table: "ExchangeRequests",
                type: "nvarchar(100)", maxLength: 100, nullable: true);

            migrationBuilder.CreateTable(
                name: "ExchangeGroups",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    TelegramGroupId = table.Column<long>(type: "bigint", nullable: true),
                    CurrencyCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    CountryCode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    TelegramGroupLink = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    GroupType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "general"),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "pending"),
                    SubmittedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    MemberCount = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    IsOfficial = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExchangeGroups", x => x.Id);
                });

            migrationBuilder.CreateIndex(name: "IX_ExchangeGroups_Status", table: "ExchangeGroups", column: "Status");
            migrationBuilder.CreateIndex(name: "IX_ExchangeGroups_CurrencyCode", table: "ExchangeGroups", column: "CurrencyCode");
            migrationBuilder.CreateIndex(name: "IX_ExchangeGroups_CountryCode", table: "ExchangeGroups", column: "CountryCode");
            migrationBuilder.CreateIndex(name: "IX_ExchangeGroups_GroupType", table: "ExchangeGroups", column: "GroupType");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ExchangeGroups");
            migrationBuilder.DropColumn(name: "DestinationCurrency", table: "ExchangeRequests");
            migrationBuilder.DropColumn(name: "City", table: "ExchangeRequests");
            migrationBuilder.DropColumn(name: "MeetingPreference", table: "ExchangeRequests");
            migrationBuilder.DropColumn(name: "PaypalEmail", table: "ExchangeRequests");
            migrationBuilder.DropColumn(name: "Iban", table: "ExchangeRequests");
            migrationBuilder.DropColumn(name: "BankName", table: "ExchangeRequests");
        }
    }
}
