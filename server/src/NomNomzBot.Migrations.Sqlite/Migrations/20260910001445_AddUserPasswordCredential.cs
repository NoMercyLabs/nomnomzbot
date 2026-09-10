using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddUserPasswordCredential : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LoginEmail",
                table: "Users",
                type: "TEXT",
                maxLength: 255,
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "LoginEmailNormalized",
                table: "Users",
                type: "TEXT",
                maxLength: 255,
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "PasswordHash",
                table: "Users",
                type: "TEXT",
                maxLength: 255,
                nullable: true
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "PasswordUpdatedAt",
                table: "Users",
                type: "TEXT",
                nullable: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_User_LoginEmailNormalized",
                table: "Users",
                column: "LoginEmailNormalized",
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_User_LoginEmailNormalized", table: "Users");

            migrationBuilder.DropColumn(name: "LoginEmail", table: "Users");

            migrationBuilder.DropColumn(name: "LoginEmailNormalized", table: "Users");

            migrationBuilder.DropColumn(name: "PasswordHash", table: "Users");

            migrationBuilder.DropColumn(name: "PasswordUpdatedAt", table: "Users");
        }
    }
}
