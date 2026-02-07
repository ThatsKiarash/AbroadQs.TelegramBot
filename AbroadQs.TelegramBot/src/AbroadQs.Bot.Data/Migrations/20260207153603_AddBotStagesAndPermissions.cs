using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AbroadQs.Bot.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBotStagesAndPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsRegistered",
                table: "TelegramUsers",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RegisteredAt",
                table: "TelegramUsers",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BotStages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StageKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TextFa = table.Column<string>(type: "nvarchar(max)", maxLength: 4096, nullable: true),
                    TextEn = table.Column<string>(type: "nvarchar(max)", maxLength: 4096, nullable: true),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    RequiredPermission = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ParentStageKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BotStages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Permissions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PermissionKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    NameFa = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    NameEn = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Permissions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserPermissions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TelegramUserId = table.Column<long>(type: "bigint", nullable: false),
                    PermissionKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    GrantedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserPermissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserPermissions_TelegramUsers_TelegramUserId",
                        column: x => x.TelegramUserId,
                        principalTable: "TelegramUsers",
                        principalColumn: "TelegramUserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BotStageButtons",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StageId = table.Column<int>(type: "int", nullable: false),
                    TextFa = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    TextEn = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ButtonType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CallbackData = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    TargetStageKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Url = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    Row = table.Column<int>(type: "int", nullable: false),
                    Column = table.Column<int>(type: "int", nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    RequiredPermission = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BotStageButtons", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BotStageButtons_BotStages_StageId",
                        column: x => x.StageId,
                        principalTable: "BotStages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BotStageButtons_StageId_Row_Column",
                table: "BotStageButtons",
                columns: new[] { "StageId", "Row", "Column" });

            migrationBuilder.CreateIndex(
                name: "IX_BotStages_StageKey",
                table: "BotStages",
                column: "StageKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Permissions_PermissionKey",
                table: "Permissions",
                column: "PermissionKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserPermissions_TelegramUserId_PermissionKey",
                table: "UserPermissions",
                columns: new[] { "TelegramUserId", "PermissionKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BotStageButtons");

            migrationBuilder.DropTable(
                name: "Permissions");

            migrationBuilder.DropTable(
                name: "UserPermissions");

            migrationBuilder.DropTable(
                name: "BotStages");

            migrationBuilder.DropColumn(
                name: "IsRegistered",
                table: "TelegramUsers");

            migrationBuilder.DropColumn(
                name: "RegisteredAt",
                table: "TelegramUsers");
        }
    }
}
