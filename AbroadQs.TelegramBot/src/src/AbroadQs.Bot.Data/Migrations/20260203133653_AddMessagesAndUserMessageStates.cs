using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AbroadQs.Bot.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMessagesAndUserMessageStates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_TelegramUsers_TelegramUserId",
                table: "TelegramUsers",
                column: "TelegramUserId");

            migrationBuilder.CreateTable(
                name: "Messages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TelegramMessageId = table.Column<long>(type: "bigint", nullable: false),
                    TelegramChatId = table.Column<long>(type: "bigint", nullable: false),
                    TelegramUserId = table.Column<long>(type: "bigint", nullable: true),
                    IsFromBot = table.Column<bool>(type: "bit", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(max)", maxLength: 4096, nullable: true),
                    MessageType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    SentAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EditedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReplyToMessageId = table.Column<int>(type: "int", nullable: true),
                    ForwardFromChatId = table.Column<long>(type: "bigint", nullable: true),
                    ForwardFromMessageId = table.Column<long>(type: "bigint", nullable: true),
                    HasReplyKeyboard = table.Column<bool>(type: "bit", nullable: false),
                    HasInlineKeyboard = table.Column<bool>(type: "bit", nullable: false),
                    InlineKeyboardId = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ShouldEdit = table.Column<bool>(type: "bit", nullable: false),
                    ShouldDelete = table.Column<bool>(type: "bit", nullable: false),
                    ShouldForward = table.Column<bool>(type: "bit", nullable: false),
                    ShouldKeepForEdit = table.Column<bool>(type: "bit", nullable: false),
                    DeleteNextMessages = table.Column<bool>(type: "bit", nullable: false),
                    UpdateId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Messages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Messages_Messages_ReplyToMessageId",
                        column: x => x.ReplyToMessageId,
                        principalTable: "Messages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Messages_TelegramUsers_TelegramUserId",
                        column: x => x.TelegramUserId,
                        principalTable: "TelegramUsers",
                        principalColumn: "TelegramUserId",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "UserMessageStates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TelegramUserId = table.Column<long>(type: "bigint", nullable: false),
                    LastBotMessageId = table.Column<int>(type: "int", nullable: true),
                    LastBotTelegramMessageId = table.Column<long>(type: "bigint", nullable: true),
                    ShouldEdit = table.Column<bool>(type: "bit", nullable: false),
                    ShouldReply = table.Column<bool>(type: "bit", nullable: false),
                    ShouldKeepStatic = table.Column<bool>(type: "bit", nullable: false),
                    DeleteNextMessages = table.Column<bool>(type: "bit", nullable: false),
                    LastAction = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    LastActionAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserMessageStates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserMessageStates_Messages_LastBotMessageId",
                        column: x => x.LastBotMessageId,
                        principalTable: "Messages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_UserMessageStates_TelegramUsers_TelegramUserId",
                        column: x => x.TelegramUserId,
                        principalTable: "TelegramUsers",
                        principalColumn: "TelegramUserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Messages_ReplyToMessageId",
                table: "Messages",
                column: "ReplyToMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_Messages_SentAt",
                table: "Messages",
                column: "SentAt");

            migrationBuilder.CreateIndex(
                name: "IX_Messages_TelegramChatId_TelegramMessageId",
                table: "Messages",
                columns: new[] { "TelegramChatId", "TelegramMessageId" });

            migrationBuilder.CreateIndex(
                name: "IX_Messages_TelegramUserId",
                table: "Messages",
                column: "TelegramUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserMessageStates_LastBotMessageId",
                table: "UserMessageStates",
                column: "LastBotMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_UserMessageStates_TelegramUserId",
                table: "UserMessageStates",
                column: "TelegramUserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserMessageStates");

            migrationBuilder.DropTable(
                name: "Messages");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_TelegramUsers_TelegramUserId",
                table: "TelegramUsers");
        }
    }
}
