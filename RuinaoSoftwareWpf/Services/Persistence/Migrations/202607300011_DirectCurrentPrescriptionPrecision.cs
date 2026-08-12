namespace RuinaoSoftwareWpf.Migrations;

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

[DbContext(typeof(CaptureDbContext))]
[Migration("202607300011_DirectCurrentPrescriptionPrecision")]
internal sealed class DirectCurrentPrescriptionPrecision : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<double>(
            name: "direct_current_total_duration_seconds",
            table: "prescriptions",
            type: "REAL",
            nullable: true);
        migrationBuilder.AddColumn<double>(
            name: "direct_current_interval_seconds",
            table: "prescriptions",
            type: "REAL",
            nullable: true);
        migrationBuilder.AddColumn<double>(
            name: "direct_current_single_duration_seconds",
            table: "prescriptions",
            type: "REAL",
            nullable: true);
        migrationBuilder.AddColumn<double>(
            name: "direct_current_ramp_up_seconds",
            table: "prescriptions",
            type: "REAL",
            nullable: true);
        migrationBuilder.AddColumn<double>(
            name: "direct_current_ramp_down_seconds",
            table: "prescriptions",
            type: "REAL",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn("direct_current_total_duration_seconds", "prescriptions");
        migrationBuilder.DropColumn("direct_current_interval_seconds", "prescriptions");
        migrationBuilder.DropColumn("direct_current_single_duration_seconds", "prescriptions");
        migrationBuilder.DropColumn("direct_current_ramp_up_seconds", "prescriptions");
        migrationBuilder.DropColumn("direct_current_ramp_down_seconds", "prescriptions");
    }
}
