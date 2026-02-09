using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AbroadQs.Bot.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddKycFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PhoneNumber",
                table: "TelegramUsers",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsVerified",
                table: "TelegramUsers",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "VerificationPhotoFileId",
                table: "TelegramUsers",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "PhoneNumber", table: "TelegramUsers");
            migrationBuilder.DropColumn(name: "IsVerified", table: "TelegramUsers");
            migrationBuilder.DropColumn(name: "VerificationPhotoFileId", table: "TelegramUsers");
        }
    }
}
