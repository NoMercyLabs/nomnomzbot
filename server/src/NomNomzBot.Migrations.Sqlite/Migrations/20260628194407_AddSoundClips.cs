using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddSoundClips : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SoundClips",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    DisplayName = table.Column<string>(
                        type: "TEXT",
                        maxLength: 100,
                        nullable: false
                    ),
                    StorageKey = table.Column<string>(
                        type: "TEXT",
                        maxLength: 200,
                        nullable: false
                    ),
                    MimeType = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    DurationMs = table.Column<int>(type: "INTEGER", nullable: false),
                    SizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    DefaultVolume = table.Column<int>(
                        type: "INTEGER",
                        nullable: false,
                        defaultValue: 80
                    ),
                    IsEnabled = table.Column<bool>(
                        type: "INTEGER",
                        nullable: false,
                        defaultValue: true
                    ),
                    CreatedByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SoundClips", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SoundClips_Channels_BroadcasterId",
                        column: x => x.BroadcasterId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "FK_SoundClips_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_SoundClips_BroadcasterId",
                table: "SoundClips",
                column: "BroadcasterId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_SoundClips_BroadcasterId_Name",
                table: "SoundClips",
                columns: new[] { "BroadcasterId", "Name" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_SoundClips_CreatedByUserId",
                table: "SoundClips",
                column: "CreatedByUserId"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "SoundClips");
        }
    }
}
