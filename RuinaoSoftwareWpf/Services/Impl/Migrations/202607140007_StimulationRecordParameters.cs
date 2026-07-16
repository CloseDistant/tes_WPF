namespace RuinaoSoftwareWpf.Migrations;

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

[DbContext(typeof(CaptureDbContext))]
[Migration("202607140007_StimulationRecordParameters")]
internal sealed class StimulationRecordParameters : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "stimulation_type",
            table: "stimulation_records",
            type: "TEXT",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "prescription_name",
            table: "stimulation_records",
            type: "TEXT",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "adverse_reaction_record",
            table: "stimulation_records",
            type: "TEXT",
            nullable: false,
            defaultValue: "无不良反应记录");

        migrationBuilder.AddColumn<string>(
            name: "parameter_snapshot_json",
            table: "stimulation_records",
            type: "TEXT",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "stimulation_type",
            table: "stimulation_records");

        migrationBuilder.DropColumn(
            name: "prescription_name",
            table: "stimulation_records");

        migrationBuilder.DropColumn(
            name: "adverse_reaction_record",
            table: "stimulation_records");

        migrationBuilder.DropColumn(
            name: "parameter_snapshot_json",
            table: "stimulation_records");
    }
}
