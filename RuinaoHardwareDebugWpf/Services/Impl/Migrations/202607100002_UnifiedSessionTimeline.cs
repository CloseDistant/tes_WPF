namespace RuinaoHardwareDebugWpf.Migrations;

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

[DbContext(typeof(CaptureDbContext))]
[Migration("202607100002_UnifiedSessionTimeline")]
internal sealed class UnifiedSessionTimeline : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "session_timeline_events",
            columns: table => new
            {
                id = table.Column<long>(type: "INTEGER", nullable: false).Annotation("Sqlite:Autoincrement", true),
                session_id = table.Column<long>(type: "INTEGER", nullable: false),
                session_key = table.Column<string>(type: "TEXT", nullable: false),
                module_code = table.Column<string>(type: "TEXT", nullable: false),
                event_type = table.Column<string>(type: "TEXT", nullable: false),
                sequence_no = table.Column<long>(type: "INTEGER", nullable: false),
                event_time_unix_ms = table.Column<long>(type: "INTEGER", nullable: false),
                session_elapsed_ms = table.Column<long>(type: "INTEGER", nullable: false),
                monotonic_ticks = table.Column<long>(type: "INTEGER", nullable: false),
                monotonic_frequency = table.Column<long>(type: "INTEGER", nullable: false),
                source_time_unix_ms = table.Column<long>(type: "INTEGER", nullable: true),
                message = table.Column<string>(type: "TEXT", nullable: true),
                payload_json = table.Column<string>(type: "TEXT", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_session_timeline_events", x => x.id));

        migrationBuilder.CreateIndex(
            name: "IX_session_timeline_events_session_id_sequence_no",
            table: "session_timeline_events",
            columns: new[] { "session_id", "sequence_no" },
            unique: true);
        migrationBuilder.CreateIndex(
            name: "IX_session_timeline_events_session_key_session_elapsed_ms",
            table: "session_timeline_events",
            columns: new[] { "session_key", "session_elapsed_ms" });
        migrationBuilder.CreateIndex(
            name: "IX_session_timeline_events_module_code_event_time_unix_ms",
            table: "session_timeline_events",
            columns: new[] { "module_code", "event_time_unix_ms" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("session_timeline_events");
    }
}
