namespace RuinaoHardwareDebugWpf.Migrations;

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

[DbContext(typeof(CaptureDbContext))]
[Migration("202607140005_FeatureVisibility")]
internal sealed class FeatureVisibility : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "feature_visibility",
            columns: table => new
            {
                feature_key = table.Column<string>(type: "TEXT", nullable: false),
                is_visible = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                updated_by_user_id = table.Column<long>(type: "INTEGER", nullable: true),
                updated_at_unix_ms = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_feature_visibility", x => x.feature_key));
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("feature_visibility");
    }
}
