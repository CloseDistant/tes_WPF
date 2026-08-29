namespace RuinaoSoftwareWpf.Migrations;

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

[DbContext(typeof(CaptureDbContext))]
[Migration("202608290014_TacsPrescriptionParameters")]
internal sealed class TacsPrescriptionParameters : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<double>("tacs_peak_current_milliampere", "prescriptions", "REAL", nullable: true);
        migrationBuilder.AddColumn<double>("tacs_ramp_up_seconds", "prescriptions", "REAL", nullable: true);
        migrationBuilder.AddColumn<double>("tacs_ramp_down_seconds", "prescriptions", "REAL", nullable: true);
        migrationBuilder.AddColumn<int>("tacs_frequency_hz", "prescriptions", "INTEGER", nullable: true);
        migrationBuilder.AddColumn<double>("tacs_total_duration_seconds", "prescriptions", "REAL", nullable: true);
        migrationBuilder.AddColumn<int>("tacs_parameter_version", "prescriptions", "INTEGER", nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn("tacs_peak_current_milliampere", "prescriptions");
        migrationBuilder.DropColumn("tacs_ramp_up_seconds", "prescriptions");
        migrationBuilder.DropColumn("tacs_ramp_down_seconds", "prescriptions");
        migrationBuilder.DropColumn("tacs_frequency_hz", "prescriptions");
        migrationBuilder.DropColumn("tacs_total_duration_seconds", "prescriptions");
        migrationBuilder.DropColumn("tacs_parameter_version", "prescriptions");
    }
}
