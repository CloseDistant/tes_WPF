namespace RuinaoSoftwareWpf.Migrations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

[DbContext(typeof(CaptureDbContext))]
[Migration("202607130003_PatientOwnershipAndOptionalStimulationPatient")]
internal sealed class PatientOwnershipAndOptionalStimulationPatient : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<long>(
            name: "owner_user_id",
            table: "patients",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_patients_owner_user_id",
            table: "patients",
            column: "owner_user_id");

        migrationBuilder.AddColumn<long>(
            name: "operator_user_id",
            table: "stimulation_records",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.AlterColumn<string>(
            name: "patient_code",
            table: "stimulation_records",
            type: "TEXT",
            nullable: true,
            oldClrType: typeof(string),
            oldType: "TEXT");

        migrationBuilder.CreateIndex(
            name: "IX_stimulation_records_operator_user_id_event_time_unix_ms",
            table: "stimulation_records",
            columns: ["operator_user_id", "event_time_unix_ms"]);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        throw new NotSupportedException("正式数据库只支持向前迁移，不支持回退患者归属结构。");
    }

    protected override void BuildTargetModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasAnnotation("ProductVersion", "10.0.0");

        var entity = modelBuilder.Entity<StimulationRecordEntity>();
        entity.Ignore(item => item.StimulationType);
        entity.Ignore(item => item.PrescriptionName);
        entity.Ignore(item => item.AdverseReactionRecord);
        entity.Ignore(item => item.ParameterSnapshotJson);
        entity.ToTable("stimulation_records");
        entity.HasKey(item => item.Id);
        entity.HasIndex(item => new { item.PatientCode, item.EventTimeUnixMs });
        entity.HasIndex(item => new { item.OperatorUserId, item.EventTimeUnixMs });
        entity.Property(item => item.Id)
            .HasColumnName("id")
            .HasColumnType("INTEGER")
            .ValueGeneratedOnAdd()
            .HasAnnotation("Sqlite:Autoincrement", true);
        entity.Property(item => item.OperatorUserId)
            .HasColumnName("operator_user_id")
            .HasColumnType("INTEGER");
        entity.Property(item => item.PatientCode)
            .HasColumnName("patient_code")
            .HasColumnType("TEXT");
        entity.Property(item => item.Action)
            .HasColumnName("action")
            .HasColumnType("TEXT")
            .IsRequired();
        entity.Property(item => item.GroupTitle)
            .HasColumnName("group_title")
            .HasColumnType("TEXT")
            .IsRequired();
        entity.Property(item => item.SelectedChannelNames)
            .HasColumnName("selected_channel_names")
            .HasColumnType("TEXT")
            .IsRequired();
        entity.Property(item => item.Status)
            .HasColumnName("status")
            .HasColumnType("TEXT")
            .IsRequired();
        entity.Property(item => item.EventTimeUnixMs)
            .HasColumnName("event_time_unix_ms")
            .HasColumnType("INTEGER");
    }
}
