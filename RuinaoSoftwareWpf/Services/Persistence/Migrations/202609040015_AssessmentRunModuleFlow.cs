namespace RuinaoSoftwareWpf.Migrations;

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

[DbContext(typeof(CaptureDbContext))]
[Migration("202609040015_AssessmentRunModuleFlow")]
internal sealed class AssessmentRunModuleFlow : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "next_module_type_id",
            table: "assessment_runs",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "module_type_id",
            table: "assessment_module_attempts",
            type: "INTEGER",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.DropIndex(
            name: "IX_assessment_module_attempts_run_id_module_index_attempt_number",
            table: "assessment_module_attempts");

        migrationBuilder.CreateTable(
            name: "assessment_run_modules",
            columns: table => new
            {
                id = table.Column<long>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                run_id = table.Column<long>(type: "INTEGER", nullable: false),
                module_type_id = table.Column<int>(type: "INTEGER", nullable: false),
                module_code = table.Column<string>(type: "TEXT", nullable: false),
                sequence = table.Column<int>(type: "INTEGER", nullable: false),
                status = table.Column<string>(type: "TEXT", nullable: false),
                created_at_unix_ms = table.Column<long>(type: "INTEGER", nullable: false),
                updated_at_unix_ms = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_assessment_run_modules", item => item.id);
                table.ForeignKey(
                    name: "FK_assessment_run_modules_assessment_runs_run_id",
                    column: item => item.run_id,
                    principalTable: "assessment_runs",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_assessment_run_modules_run_id_module_type_id",
            table: "assessment_run_modules",
            columns: new[] { "run_id", "module_type_id" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_assessment_run_modules_run_id_sequence",
            table: "assessment_run_modules",
            columns: new[] { "run_id", "sequence" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_assessment_run_modules_run_id_status",
            table: "assessment_run_modules",
            columns: new[] { "run_id", "status" });

        migrationBuilder.Sql(
            """
            UPDATE assessment_module_attempts
            SET module_type_id = CASE module_code
                WHEN 'eye_calibration' THEN 1
                WHEN 'picture_browse' THEN 2
                WHEN 'video_browse' THEN 3
                WHEN 'voice_baseline' THEN 4
                WHEN 'word_reading' THEN 5
                WHEN 'short_text_reading' THEN 6
                WHEN 'emotion_question' THEN 7
                WHEN 'dot_probe' THEN 8
                WHEN 'emotion_oddball' THEN 9
                WHEN 'emotion_letter_search' THEN 10
                WHEN 'emotion_stroop' THEN 11
                WHEN 'basic_info' THEN 12
                WHEN 'questionnaire_a' THEN 13
                WHEN 'questionnaire_b' THEN 14
                WHEN 'questionnaire_c' THEN 15
                WHEN 'questionnaire_d' THEN 16
                WHEN 'questionnaire_e' THEN 17
                WHEN 'questionnaire_f' THEN 18
                WHEN 'questionnaire_g' THEN 19
                WHEN 'questionnaire_h' THEN 20
                WHEN 'questionnaire_i' THEN 21
                WHEN 'questionnaire_j' THEN 22
                WHEN 'sync_test' THEN 23
                ELSE 1000000 + module_index
            END;
            """);

        migrationBuilder.CreateIndex(
            name: "IX_assessment_module_attempts_run_id_module_type_id_attempt_number",
            table: "assessment_module_attempts",
            columns: new[] { "run_id", "module_type_id", "attempt_number" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_assessment_module_attempts_run_id_module_type_id_attempt_number",
            table: "assessment_module_attempts");
        migrationBuilder.DropTable("assessment_run_modules");
        migrationBuilder.DropColumn(
            name: "next_module_type_id",
            table: "assessment_runs");
        migrationBuilder.DropColumn(
            name: "module_type_id",
            table: "assessment_module_attempts");

        migrationBuilder.CreateIndex(
            name: "IX_assessment_module_attempts_run_id_module_index_attempt_number",
            table: "assessment_module_attempts",
            columns: new[] { "run_id", "module_index", "attempt_number" },
            unique: true);
    }
}
