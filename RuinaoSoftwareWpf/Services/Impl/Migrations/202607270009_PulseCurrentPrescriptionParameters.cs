namespace RuinaoSoftwareWpf.Migrations;

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

[DbContext(typeof(CaptureDbContext))]
[Migration("202607270009_PulseCurrentPrescriptionParameters")]
internal sealed class PulseCurrentPrescriptionParameters : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "pulse_treatment_duration_seconds",
            table: "prescriptions",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "pulse_width_milliseconds",
            table: "prescriptions",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "pulse_rise_width_milliseconds",
            table: "prescriptions",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "pulse_interval_width_milliseconds",
            table: "prescriptions",
            type: "INTEGER",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn("pulse_treatment_duration_seconds", "prescriptions");
        migrationBuilder.DropColumn("pulse_width_milliseconds", "prescriptions");
        migrationBuilder.DropColumn("pulse_rise_width_milliseconds", "prescriptions");
        migrationBuilder.DropColumn("pulse_interval_width_milliseconds", "prescriptions");
    }
}
