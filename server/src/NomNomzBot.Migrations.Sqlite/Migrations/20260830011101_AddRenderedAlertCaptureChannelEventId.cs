using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddRenderedAlertCaptureChannelEventId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ChannelEventId",
                table: "RenderedAlertCaptures",
                type: "TEXT",
                maxLength: 50,
                nullable: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_RenderedAlertCapture_BroadcasterId_ChannelEventId",
                table: "RenderedAlertCaptures",
                columns: new[] { "BroadcasterId", "ChannelEventId" }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RenderedAlertCapture_BroadcasterId_ChannelEventId",
                table: "RenderedAlertCaptures"
            );

            migrationBuilder.DropColumn(name: "ChannelEventId", table: "RenderedAlertCaptures");
        }
    }
}
