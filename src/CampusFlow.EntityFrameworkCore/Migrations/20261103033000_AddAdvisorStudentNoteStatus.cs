using System;
using CampusFlow.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CampusFlow.Migrations;

[DbContext(typeof(CampusFlowDbContext))]
[Migration("20261103033000_AddAdvisorStudentNoteStatus")]
public sealed class AddAdvisorStudentNoteStatus : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(
            name: "StudentNoteWrittenAt",
            table: "AppCourseReviewSubmissions",
            type: "timestamp without time zone",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "StudentNoteWrittenAt",
            table: "AppCourseReviewSubmissions");
    }
}
