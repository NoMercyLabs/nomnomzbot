using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddMassBanBatches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AcceptsModeratorMassBans",
                table: "Channels",
                type: "INTEGER",
                nullable: false,
                defaultValue: true
            );

            migrationBuilder.CreateTable(
                name: "MassBanBatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false, collation: "NOCASE"),
                    OperatorUserId = table.Column<Guid>(
                        type: "TEXT",
                        nullable: false,
                        collation: "NOCASE"
                    ),
                    OperatorDisplayName = table.Column<string>(
                        type: "TEXT",
                        maxLength: 100,
                        nullable: false
                    ),
                    ChannelTwitchId = table.Column<string>(
                        type: "TEXT",
                        maxLength: 50,
                        nullable: false
                    ),
                    ChannelLogin = table.Column<string>(
                        type: "TEXT",
                        maxLength: 100,
                        nullable: false
                    ),
                    ChannelId = table.Column<Guid>(
                        type: "TEXT",
                        nullable: true,
                        collation: "NOCASE"
                    ),
                    HoldWhileLive = table.Column<bool>(type: "INTEGER", nullable: false),
                    RequestedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    NoticeSentAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    DeclinedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    DecidedByDisplayName = table.Column<string>(
                        type: "TEXT",
                        maxLength: 100,
                        nullable: true
                    ),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MassBanBatches", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "MassBanBatchTarget",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false, collation: "NOCASE"),
                    MassBanBatchId = table.Column<Guid>(
                        type: "TEXT",
                        nullable: false,
                        collation: "NOCASE"
                    ),
                    TwitchUserId = table.Column<string>(
                        type: "TEXT",
                        maxLength: 50,
                        nullable: false
                    ),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Banned = table.Column<bool>(type: "INTEGER", nullable: false),
                    Error = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MassBanBatchTarget", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MassBanBatchTarget_MassBanBatches_MassBanBatchId",
                        column: x => x.MassBanBatchId,
                        principalTable: "MassBanBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_MassBanBatches_ChannelId_CompletedAt",
                table: "MassBanBatches",
                columns: new[] { "ChannelId", "CompletedAt" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_MassBanBatches_CompletedAt_RequestedAt",
                table: "MassBanBatches",
                columns: new[] { "CompletedAt", "RequestedAt" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_MassBanBatchTarget_MassBanBatchId_ProcessedAt",
                table: "MassBanBatchTarget",
                columns: new[] { "MassBanBatchId", "ProcessedAt" }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "MassBanBatchTarget");

            migrationBuilder.DropTable(name: "MassBanBatches");

            migrationBuilder.DropColumn(name: "AcceptsModeratorMassBans", table: "Channels");
        }
    }
}
