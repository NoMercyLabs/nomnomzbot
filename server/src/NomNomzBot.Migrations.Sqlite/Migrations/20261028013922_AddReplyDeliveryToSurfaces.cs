using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddReplyDeliveryToSurfaces : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ReplyDeliveryJson",
                table: "Timers",
                type: "TEXT",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "ReplyDeliveryJson",
                table: "EventResponses",
                type: "TEXT",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "ReplyDeliveryJson",
                table: "ChatTriggers",
                type: "TEXT",
                nullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "ReplyDeliveryJson", table: "Timers");

            migrationBuilder.DropColumn(name: "ReplyDeliveryJson", table: "EventResponses");

            migrationBuilder.DropColumn(name: "ReplyDeliveryJson", table: "ChatTriggers");
        }
    }
}
