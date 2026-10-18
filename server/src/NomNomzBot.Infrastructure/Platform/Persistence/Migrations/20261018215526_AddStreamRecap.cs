using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Infrastructure.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStreamRecap : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StreamRecaps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: false),
                    StreamId = table.Column<string>(
                        type: "character varying(50)",
                        maxLength: 50,
                        nullable: false
                    ),
                    FromAt = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    ToAt = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    DataJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    UpdatedAt = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StreamRecaps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StreamRecaps_Channels_ChannelId",
                        column: x => x.ChannelId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "FK_StreamRecaps_Streams_StreamId",
                        column: x => x.StreamId,
                        principalTable: "Streams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_StreamRecap_Channel_FromAt",
                table: "StreamRecaps",
                columns: new[] { "ChannelId", "FromAt" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_StreamRecap_Channel_Stream",
                table: "StreamRecaps",
                columns: new[] { "ChannelId", "StreamId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_StreamRecaps_StreamId",
                table: "StreamRecaps",
                column: "StreamId"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "StreamRecaps");
        }
    }
}
