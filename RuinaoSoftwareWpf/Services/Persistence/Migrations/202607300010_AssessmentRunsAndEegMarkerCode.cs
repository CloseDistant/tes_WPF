namespace RuinaoSoftwareWpf.Migrations;

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

[DbContext(typeof(CaptureDbContext))]
[Migration("202607300010_AssessmentRunsAndEegMarkerCode")]
internal sealed class AssessmentRunsAndEegMarkerCode : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<long>(
            name: "assessment_attempt_id",
            table: "assessment_module_records",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "marker_code",
            table: "eeg_markers",
            type: "TEXT",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "assessment_runs",
            columns: table => new
            {
                id = table.Column<long>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                patient_code = table.Column<string>(type: "TEXT", nullable: false),
                status = table.Column<string>(type: "TEXT", nullable: false),
                total_module_count = table.Column<int>(type: "INTEGER", nullable: false),
                next_module_index = table.Column<int>(type: "INTEGER", nullable: false),
                started_at_unix_ms = table.Column<long>(type: "INTEGER", nullable: false),
                ended_at_unix_ms = table.Column<long>(type: "INTEGER", nullable: true),
                created_at_unix_ms = table.Column<long>(type: "INTEGER", nullable: false),
                updated_at_unix_ms = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_assessment_runs", item => item.id));

        migrationBuilder.CreateTable(
            name: "assessment_module_attempts",
            columns: table => new
            {
                id = table.Column<long>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                run_id = table.Column<long>(type: "INTEGER", nullable: false),
                session_key = table.Column<string>(type: "TEXT", nullable: false),
                module_code = table.Column<string>(type: "TEXT", nullable: false),
                module_name = table.Column<string>(type: "TEXT", nullable: false),
                module_index = table.Column<int>(type: "INTEGER", nullable: false),
                attempt_number = table.Column<int>(type: "INTEGER", nullable: false),
                status = table.Column<string>(type: "TEXT", nullable: false),
                result_json = table.Column<string>(type: "TEXT", nullable: true),
                error_code = table.Column<string>(type: "TEXT", nullable: true),
                message = table.Column<string>(type: "TEXT", nullable: true),
                started_at_unix_ms = table.Column<long>(type: "INTEGER", nullable: false),
                ended_at_unix_ms = table.Column<long>(type: "INTEGER", nullable: true),
                created_at_unix_ms = table.Column<long>(type: "INTEGER", nullable: false),
                updated_at_unix_ms = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_assessment_module_attempts", item => item.id);
                table.ForeignKey(
                    name: "FK_assessment_module_attempts_assessment_runs_run_id",
                    column: item => item.run_id,
                    principalTable: "assessment_runs",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_assessment_module_records_assessment_attempt_id",
            table: "assessment_module_records",
            column: "assessment_attempt_id",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_assessment_runs_patient_code_status",
            table: "assessment_runs",
            columns: new[] { "patient_code", "status" });

        migrationBuilder.CreateIndex(
            name: "IX_assessment_runs_patient_active",
            table: "assessment_runs",
            column: "patient_code",
            unique: true,
            filter: "status = 'in_progress'");

        migrationBuilder.CreateIndex(
            name: "IX_assessment_module_attempts_run_id_module_index_attempt_number",
            table: "assessment_module_attempts",
            columns: new[] { "run_id", "module_index", "attempt_number" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_assessment_module_attempts_run_id_status",
            table: "assessment_module_attempts",
            columns: new[] { "run_id", "status" });

    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("assessment_module_attempts");
        migrationBuilder.DropTable("assessment_runs");
        migrationBuilder.DropIndex(
            name: "IX_assessment_module_records_assessment_attempt_id",
            table: "assessment_module_records");
        migrationBuilder.DropColumn(
            name: "assessment_attempt_id",
            table: "assessment_module_records");
        migrationBuilder.DropColumn(
            name: "marker_code",
            table: "eeg_markers");
    }
}
