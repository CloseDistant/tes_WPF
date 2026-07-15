namespace RuinaoHardwareDebugWpf.Migrations;

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

        migrationBuilder.Sql(
            """
            DROP INDEX IF EXISTS IX_stimulation_records_patient_code_event_time_unix_ms;
            ALTER TABLE stimulation_records RENAME TO stimulation_records_before_patient_ownership;
            CREATE TABLE stimulation_records (
                id INTEGER NOT NULL CONSTRAINT PK_stimulation_records PRIMARY KEY AUTOINCREMENT,
                operator_user_id INTEGER NULL,
                patient_code TEXT NULL,
                action TEXT NOT NULL,
                group_title TEXT NOT NULL,
                selected_channel_names TEXT NOT NULL,
                status TEXT NOT NULL,
                event_time_unix_ms INTEGER NOT NULL
            );
            INSERT INTO stimulation_records (
                id,
                patient_code,
                action,
                group_title,
                selected_channel_names,
                status,
                event_time_unix_ms)
            SELECT
                id,
                patient_code,
                action,
                group_title,
                selected_channel_names,
                status,
                event_time_unix_ms
            FROM stimulation_records_before_patient_ownership;
            DROP TABLE stimulation_records_before_patient_ownership;
            CREATE INDEX IX_stimulation_records_patient_code_event_time_unix_ms
                ON stimulation_records (patient_code, event_time_unix_ms);
            CREATE INDEX IX_stimulation_records_operator_user_id_event_time_unix_ms
                ON stimulation_records (operator_user_id, event_time_unix_ms);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_patients_owner_user_id",
            table: "patients");
        migrationBuilder.DropColumn(
            name: "owner_user_id",
            table: "patients");

        migrationBuilder.Sql(
            """
            DROP INDEX IF EXISTS IX_stimulation_records_patient_code_event_time_unix_ms;
            DROP INDEX IF EXISTS IX_stimulation_records_operator_user_id_event_time_unix_ms;
            ALTER TABLE stimulation_records RENAME TO stimulation_records_with_optional_patient;
            CREATE TABLE stimulation_records (
                id INTEGER NOT NULL CONSTRAINT PK_stimulation_records PRIMARY KEY AUTOINCREMENT,
                patient_code TEXT NOT NULL,
                action TEXT NOT NULL,
                group_title TEXT NOT NULL,
                selected_channel_names TEXT NOT NULL,
                status TEXT NOT NULL,
                event_time_unix_ms INTEGER NOT NULL
            );
            INSERT INTO stimulation_records (
                id,
                patient_code,
                action,
                group_title,
                selected_channel_names,
                status,
                event_time_unix_ms)
            SELECT
                id,
                COALESCE(patient_code, ''),
                action,
                group_title,
                selected_channel_names,
                status,
                event_time_unix_ms
            FROM stimulation_records_with_optional_patient;
            DROP TABLE stimulation_records_with_optional_patient;
            CREATE INDEX IX_stimulation_records_patient_code_event_time_unix_ms
                ON stimulation_records (patient_code, event_time_unix_ms);
            """);
    }
}
