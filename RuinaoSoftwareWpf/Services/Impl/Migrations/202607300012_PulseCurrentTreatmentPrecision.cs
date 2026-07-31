namespace RuinaoSoftwareWpf.Migrations;

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

[DbContext(typeof(CaptureDbContext))]
[Migration("202607300012_PulseCurrentTreatmentPrecision")]
internal sealed class PulseCurrentTreatmentPrecision : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<double>(
            name: "pulse_treatment_duration_seconds_exact",
            table: "prescriptions",
            type: "REAL",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "pulse_treatment_duration_seconds_exact",
            table: "prescriptions");
    }
}
