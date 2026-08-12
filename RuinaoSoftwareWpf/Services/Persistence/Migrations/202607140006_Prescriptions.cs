namespace RuinaoSoftwareWpf.Migrations;

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

[DbContext(typeof(CaptureDbContext))]
[Migration("202607140006_Prescriptions")]
internal sealed class Prescriptions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "prescriptions",
            columns: table => new
            {
                id = table.Column<string>(type: "TEXT", nullable: false),
                name = table.Column<string>(type: "TEXT", nullable: false),
                indication = table.Column<string>(type: "TEXT", nullable: false),
                stimulation_type = table.Column<string>(type: "TEXT", nullable: false),
                current_milliamp = table.Column<double>(type: "REAL", nullable: false),
                delivery_mode = table.Column<string>(type: "TEXT", nullable: false),
                total_duration_minutes = table.Column<int>(type: "INTEGER", nullable: false),
                interval_minutes = table.Column<int>(type: "INTEGER", nullable: true),
                session_duration_minutes = table.Column<int>(type: "INTEGER", nullable: true),
                course = table.Column<string>(type: "TEXT", nullable: false),
                ramp_up_seconds = table.Column<int>(type: "INTEGER", nullable: false),
                ramp_down_seconds = table.Column<int>(type: "INTEGER", nullable: false),
                evidence_grade = table.Column<string>(type: "TEXT", nullable: false),
                is_builtin = table.Column<bool>(type: "INTEGER", nullable: false),
                created_at_unix_ms = table.Column<long>(type: "INTEGER", nullable: false),
                updated_at_unix_ms = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_prescriptions", x => x.id));

        migrationBuilder.CreateIndex(
            name: "IX_prescriptions_name",
            table: "prescriptions",
            column: "name");

        migrationBuilder.InsertData(
            table: "prescriptions",
            columns:
            [
                "id",
                "name",
                "indication",
                "stimulation_type",
                "current_milliamp",
                "delivery_mode",
                "total_duration_minutes",
                "interval_minutes",
                "session_duration_minutes",
                "course",
                "ramp_up_seconds",
                "ramp_down_seconds",
                "evidence_grade",
                "is_builtin",
                "created_at_unix_ms",
                "updated_at_unix_ms"
            ],
            columnTypes:
            [
                "TEXT",
                "TEXT",
                "TEXT",
                "TEXT",
                "REAL",
                "TEXT",
                "INTEGER",
                "INTEGER",
                "INTEGER",
                "TEXT",
                "INTEGER",
                "INTEGER",
                "TEXT",
                "INTEGER",
                "INTEGER",
                "INTEGER"
            ],
            values:
            [
                "BUILTIN_TDCS_PROTOCOL1",
                "protocol1",
                "默认直流电刺激",
                "tDCS",
                2.0,
                "连续",
                20,
                null,
                null,
                "10次；前3周5次/周，后7周3次/周",
                30,
                30,
                "内置默认处方",
                true,
                0L,
                0L
            ]);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable("prescriptions");
}
