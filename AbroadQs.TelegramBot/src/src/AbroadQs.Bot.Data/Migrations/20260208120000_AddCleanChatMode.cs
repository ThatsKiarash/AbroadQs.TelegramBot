using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AbroadQs.Bot.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCleanChatMode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CleanChatMode",
                table: "TelegramUsers",
                type: "bit",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CleanChatMode",
                table: "TelegramUsers");
        }
    }
}
