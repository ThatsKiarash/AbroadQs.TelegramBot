using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AbroadQs.Bot.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddKycExtraFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "TelegramUsers",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "EmailVerified",
                table: "TelegramUsers",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Country",
                table: "TelegramUsers",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "KycStatus",
                table: "TelegramUsers",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "KycRejectionData",
                table: "TelegramUsers",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "Email", table: "TelegramUsers");
            migrationBuilder.DropColumn(name: "EmailVerified", table: "TelegramUsers");
            migrationBuilder.DropColumn(name: "Country", table: "TelegramUsers");
            migrationBuilder.DropColumn(name: "KycStatus", table: "TelegramUsers");
            migrationBuilder.DropColumn(name: "KycRejectionData", table: "TelegramUsers");
        }
    }
}
