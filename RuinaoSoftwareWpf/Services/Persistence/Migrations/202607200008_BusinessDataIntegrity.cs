namespace RuinaoSoftwareWpf.Migrations;

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

[DbContext(typeof(CaptureDbContext))]
[Migration("202607200008_BusinessDataIntegrity")]
internal sealed class BusinessDataIntegrity : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<long>(
            name: "updated_by_user_id",
            table: "patients",
            type: "INTEGER",
            nullable: true);
        migrationBuilder.AddColumn<long>(
            name: "created_by_user_id",
            table: "prescriptions",
            type: "INTEGER",
            nullable: true);
        migrationBuilder.AddColumn<long>(
            name: "updated_by_user_id",
            table: "prescriptions",
            type: "INTEGER",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn("updated_by_user_id", "patients");
        migrationBuilder.DropColumn("created_by_user_id", "prescriptions");
        migrationBuilder.DropColumn("updated_by_user_id", "prescriptions");
    }
}
