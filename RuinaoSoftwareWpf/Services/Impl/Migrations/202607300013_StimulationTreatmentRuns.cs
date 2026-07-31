namespace RuinaoSoftwareWpf.Migrations;

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

[DbContext(typeof(CaptureDbContext))]
[Migration("202607300013_StimulationTreatmentRuns")]
internal sealed class StimulationTreatmentRuns : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "stimulation_runs",
            columns: table => new
            {
                id = table.Column<long>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                run_id = table.Column<string>(type: "TEXT", nullable: false),
                operator_user_id = table.Column<long>(type: "INTEGER", nullable: true),
                patient_code = table.Column<string>(type: "TEXT", nullable: true),
                stimulation_type = table.Column<string>(type: "TEXT", nullable: false),
                prescription_name = table.Column<string>(type: "TEXT", nullable: true),
                group_title = table.Column<string>(type: "TEXT", nullable: false),
                status = table.Column<string>(type: "TEXT", nullable: false),
                started_at_unix_ms = table.Column<long>(type: "INTEGER", nullable: false),
                ended_at_unix_ms = table.Column<long>(type: "INTEGER", nullable: true),
                created_at_unix_ms = table.Column<long>(type: "INTEGER", nullable: false),
                updated_at_unix_ms = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_stimulation_runs", item => item.id);
                table.CheckConstraint(
                    "CK_stimulation_runs_status",
                    "status IN ('RUNNING', 'ENDED', 'INCOMPLETE')");
            });

        migrationBuilder.CreateTable(
            name: "stimulation_channel_treatments",
            columns: table => new
            {
                id = table.Column<long>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                stimulation_run_id = table.Column<long>(type: "INTEGER", nullable: false),
                channel_name = table.Column<string>(type: "TEXT", nullable: false),
                status = table.Column<string>(type: "TEXT", nullable: false),
                started_at_unix_ms = table.Column<long>(type: "INTEGER", nullable: false),
                ended_at_unix_ms = table.Column<long>(type: "INTEGER", nullable: true),
                end_type = table.Column<string>(type: "TEXT", nullable: true),
                end_reason_code = table.Column<string>(type: "TEXT", nullable: true),
                end_reason_detail = table.Column<string>(type: "TEXT", nullable: true),
                current_milliamp = table.Column<double>(type: "REAL", nullable: false),
                planned_duration_seconds = table.Column<double>(type: "REAL", nullable: false),
                polarity = table.Column<string>(type: "TEXT", nullable: false),
                parameter_schema_version = table.Column<int>(type: "INTEGER", nullable: false),
                parameter_snapshot_json = table.Column<string>(type: "TEXT", nullable: false),
                planned_total_count = table.Column<long>(type: "INTEGER", nullable: true),
                completed_count = table.Column<long>(type: "INTEGER", nullable: true),
                device_error_code = table.Column<string>(type: "TEXT", nullable: true),
                created_at_unix_ms = table.Column<long>(type: "INTEGER", nullable: false),
                updated_at_unix_ms = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_stimulation_channel_treatments", item => item.id);
                table.CheckConstraint(
                    "CK_stimulation_channel_treatments_status",
                    "status IN ('RUNNING', 'ENDED', 'INCOMPLETE')");
                table.CheckConstraint(
                    "CK_stimulation_channel_treatments_end_type",
                    "end_type IS NULL OR end_type IN ('NORMAL_COMPLETION', 'MANUAL_TERMINATION', 'ABNORMAL_TERMINATION')");
                table.ForeignKey(
                    name: "FK_stimulation_channel_treatments_stimulation_runs_stimulation_run_id",
                    column: item => item.stimulation_run_id,
                    principalTable: "stimulation_runs",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_stimulation_runs_run_id",
            table: "stimulation_runs",
            column: "run_id",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_stimulation_runs_operator_user_id_started_at_unix_ms",
            table: "stimulation_runs",
            columns: new[] { "operator_user_id", "started_at_unix_ms" });

        migrationBuilder.CreateIndex(
            name: "IX_stimulation_runs_patient_code_started_at_unix_ms",
            table: "stimulation_runs",
            columns: new[] { "patient_code", "started_at_unix_ms" });

        migrationBuilder.CreateIndex(
            name: "IX_stimulation_runs_status",
            table: "stimulation_runs",
            column: "status");

        migrationBuilder.CreateIndex(
            name: "IX_stimulation_channel_treatments_stimulation_run_id_channel_name",
            table: "stimulation_channel_treatments",
            columns: new[] { "stimulation_run_id", "channel_name" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_stimulation_channel_treatments_channel_name_status",
            table: "stimulation_channel_treatments",
            columns: new[] { "channel_name", "status" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("stimulation_channel_treatments");
        migrationBuilder.DropTable("stimulation_runs");
    }
}
