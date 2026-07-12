using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRequestWizardFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AssignedUserId",
                table: "WorkflowStages",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IpAddress",
                table: "RequestServers",
                type: "varchar(45)",
                maxLength: 45,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<bool>(
                name: "IsPhysical",
                table: "RequestServers",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Os",
                table: "RequestServers",
                type: "varchar(100)",
                maxLength: 100,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "Department",
                table: "Requests",
                type: "varchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "Requests",
                type: "text",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "EndDate",
                table: "Requests",
                type: "datetime(6)",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "ProjectCode",
                table: "Requests",
                type: "varchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "ProjectName",
                table: "Requests",
                type: "varchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "Sponsor",
                table: "Requests",
                type: "varchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "StartDate",
                table: "Requests",
                type: "datetime(6)",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "Title",
                table: "Requests",
                type: "varchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "AttachmentPaths",
                table: "Justifications",
                type: "json",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowStages_AssignedUserId",
                table: "WorkflowStages",
                column: "AssignedUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_WorkflowStages_Users_AssignedUserId",
                table: "WorkflowStages",
                column: "AssignedUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WorkflowStages_Users_AssignedUserId",
                table: "WorkflowStages");

            migrationBuilder.DropIndex(
                name: "IX_WorkflowStages_AssignedUserId",
                table: "WorkflowStages");

            migrationBuilder.DropColumn(
                name: "AssignedUserId",
                table: "WorkflowStages");

            migrationBuilder.DropColumn(
                name: "IpAddress",
                table: "RequestServers");

            migrationBuilder.DropColumn(
                name: "IsPhysical",
                table: "RequestServers");

            migrationBuilder.DropColumn(
                name: "Os",
                table: "RequestServers");

            migrationBuilder.DropColumn(
                name: "Department",
                table: "Requests");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "Requests");

            migrationBuilder.DropColumn(
                name: "EndDate",
                table: "Requests");

            migrationBuilder.DropColumn(
                name: "ProjectCode",
                table: "Requests");

            migrationBuilder.DropColumn(
                name: "ProjectName",
                table: "Requests");

            migrationBuilder.DropColumn(
                name: "Sponsor",
                table: "Requests");

            migrationBuilder.DropColumn(
                name: "StartDate",
                table: "Requests");

            migrationBuilder.DropColumn(
                name: "Title",
                table: "Requests");

            migrationBuilder.DropColumn(
                name: "AttachmentPaths",
                table: "Justifications");
        }
    }
}
