using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Infrastructure.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTtsConfigTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TtsConfigs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    Mode = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: false
                    ),
                    DefaultProvider = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: false
                    ),
                    DefaultVoiceId = table.Column<string>(
                        type: "character varying(255)",
                        maxLength: 255,
                        nullable: true
                    ),
                    ProfanityCensorEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    ModApprovalRequired = table.Column<bool>(type: "boolean", nullable: false),
                    MinBitsToTts = table.Column<int>(type: "integer", nullable: true),
                    MaxCharacters = table.Column<int>(type: "integer", nullable: false),
                    MinPermission = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: false
                    ),
                    SkipBotMessages = table.Column<bool>(type: "boolean", nullable: false),
                    ReadUsernames = table.Column<bool>(type: "boolean", nullable: false),
                    AzureApiKeyCipher = table.Column<string>(type: "text", nullable: true),
                    AzureApiKeyNonce = table.Column<string>(type: "text", nullable: true),
                    AzureKeyVersion = table.Column<int>(type: "integer", nullable: true),
                    AzureRegion = table.Column<string>(
                        type: "character varying(50)",
                        maxLength: 50,
                        nullable: true
                    ),
                    ElevenLabsApiKeyCipher = table.Column<string>(type: "text", nullable: true),
                    ElevenLabsApiKeyNonce = table.Column<string>(type: "text", nullable: true),
                    ElevenLabsKeyVersion = table.Column<int>(type: "integer", nullable: true),
                    SubjectKeyId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    UpdatedAt = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    DeletedAt = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TtsConfigs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TtsConfigs_Channels_BroadcasterId",
                        column: x => x.BroadcasterId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "FK_TtsConfigs_CryptoKeys_SubjectKeyId",
                        column: x => x.SubjectKeyId,
                        principalTable: "CryptoKeys",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_TtsConfigs_BroadcasterId",
                table: "TtsConfigs",
                column: "BroadcasterId",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_TtsConfigs_SubjectKeyId",
                table: "TtsConfigs",
                column: "SubjectKeyId"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "TtsConfigs");
        }
    }
}
