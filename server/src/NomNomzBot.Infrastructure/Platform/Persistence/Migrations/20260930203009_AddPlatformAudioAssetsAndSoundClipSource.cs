using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Infrastructure.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPlatformAudioAssetsAndSoundClipSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PlatformSourceDefinitionId",
                table: "SoundClips",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "PlatformSourceHash",
                table: "SoundClips",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "PlatformSourceSyncedAt",
                table: "SoundClips",
                type: "timestamp with time zone",
                nullable: true
            );

            migrationBuilder.AddColumn<int>(
                name: "PlatformSourceVersion",
                table: "SoundClips",
                type: "integer",
                nullable: true
            );

            migrationBuilder.CreateTable(
                name: "PlatformAudioAssets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DisplayName = table.Column<string>(
                        type: "character varying(100)",
                        maxLength: 100,
                        nullable: false
                    ),
                    FileName = table.Column<string>(
                        type: "character varying(200)",
                        maxLength: 200,
                        nullable: false
                    ),
                    StorageKey = table.Column<string>(
                        type: "character varying(200)",
                        maxLength: 200,
                        nullable: false
                    ),
                    ContentType = table.Column<string>(
                        type: "character varying(40)",
                        maxLength: 40,
                        nullable: false
                    ),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    DurationMs = table.Column<int>(type: "integer", nullable: false),
                    ContentHash = table.Column<string>(
                        type: "character varying(64)",
                        maxLength: 64,
                        nullable: false
                    ),
                    UploadedByPrincipalId = table.Column<Guid>(type: "uuid", nullable: false),
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
                    DeletedBy = table.Column<Guid>(type: "uuid", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformAudioAssets", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_SoundClip_PlatformSourceDefinitionId",
                table: "SoundClips",
                column: "PlatformSourceDefinitionId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_PlatformAudioAssets_ContentHash",
                table: "PlatformAudioAssets",
                column: "ContentHash"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "PlatformAudioAssets");

            migrationBuilder.DropIndex(
                name: "IX_SoundClip_PlatformSourceDefinitionId",
                table: "SoundClips"
            );

            migrationBuilder.DropColumn(name: "PlatformSourceDefinitionId", table: "SoundClips");

            migrationBuilder.DropColumn(name: "PlatformSourceHash", table: "SoundClips");

            migrationBuilder.DropColumn(name: "PlatformSourceSyncedAt", table: "SoundClips");

            migrationBuilder.DropColumn(name: "PlatformSourceVersion", table: "SoundClips");
        }
    }
}
