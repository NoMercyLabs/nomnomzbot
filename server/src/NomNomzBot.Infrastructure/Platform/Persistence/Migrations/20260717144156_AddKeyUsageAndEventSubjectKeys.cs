using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace NomNomzBot.Infrastructure.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddKeyUsageAndEventSubjectKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EventSubjectKeys",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "uuid", nullable: true),
                    SubjectIdHash = table.Column<string>(
                        type: "character varying(64)",
                        maxLength: 64,
                        nullable: false
                    ),
                    SubjectKeyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: true
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventSubjectKeys", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "KeyUsageBindings",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    CryptoKeyId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResourceTable = table.Column<string>(
                        type: "character varying(100)",
                        maxLength: 100,
                        nullable: false
                    ),
                    ResourceColumn = table.Column<string>(
                        type: "character varying(100)",
                        maxLength: 100,
                        nullable: false
                    ),
                    BroadcasterId = table.Column<Guid>(type: "uuid", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KeyUsageBindings", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_EventSubjectKeys_BroadcasterId",
                table: "EventSubjectKeys",
                column: "BroadcasterId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_EventSubjectKeys_EventId_SubjectKeyId",
                table: "EventSubjectKeys",
                columns: new[] { "EventId", "SubjectKeyId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_EventSubjectKeys_SubjectIdHash",
                table: "EventSubjectKeys",
                column: "SubjectIdHash"
            );

            migrationBuilder.CreateIndex(
                name: "IX_EventSubjectKeys_SubjectKeyId",
                table: "EventSubjectKeys",
                column: "SubjectKeyId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_KeyUsageBindings_BroadcasterId",
                table: "KeyUsageBindings",
                column: "BroadcasterId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_KeyUsageBindings_CryptoKeyId_ResourceTable_ResourceColumn",
                table: "KeyUsageBindings",
                columns: new[] { "CryptoKeyId", "ResourceTable", "ResourceColumn" },
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "EventSubjectKeys");

            migrationBuilder.DropTable(name: "KeyUsageBindings");
        }
    }
}
