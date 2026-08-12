namespace RuinaoSoftwareWpf.Migrations;

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

[DbContext(typeof(CaptureDbContext))]
[Migration("202607100001_InitialSchema")]
internal sealed class InitialSchema : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "users",
            columns: table => new
            {
                id = table.Column<long>(type: "INTEGER", nullable: false).Annotation("Sqlite:Autoincrement", true),
                login_name = table.Column<string>(type: "TEXT", nullable: false, collation: "NOCASE"),
                display_name = table.Column<string>(type: "TEXT", nullable: false),
                role_id = table.Column<int>(type: "INTEGER", nullable: false),
                password_hash = table.Column<string>(type: "TEXT", nullable: false),
                password_salt = table.Column<string>(type: "TEXT", nullable: false),
                must_change_password = table.Column<bool>(type: "INTEGER", nullable: false),
                is_active = table.Column<bool>(type: "INTEGER", nullable: false),
                created_at_unix_ms = table.Column<long>(type: "INTEGER", nullable: false),
                updated_at_unix_ms = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_users", x => x.id));

        migrationBuilder.CreateTable(
            name: "account_audit_logs",
            columns: table => new
            {
                id = table.Column<long>(type: "INTEGER", nullable: false).Annotation("Sqlite:Autoincrement", true),
                operator_user_id = table.Column<long>(type: "INTEGER", nullable: true),
                target_user_id = table.Column<long>(type: "INTEGER", nullable: true),
                action = table.Column<string>(type: "TEXT", nullable: false),
                result = table.Column<string>(type: "TEXT", nullable: false),
                message = table.Column<string>(type: "TEXT", nullable: true),
                created_at_unix_ms = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_account_audit_logs", x => x.id));

        migrationBuilder.CreateTable(
            name: "patients",
            columns: table => new
            {
                id = table.Column<long>(type: "INTEGER", nullable: false).Annotation("Sqlite:Autoincrement", true),
                patient_code = table.Column<string>(type: "TEXT", nullable: false),
                name = table.Column<string>(type: "TEXT", nullable: true),
                gender = table.Column<string>(type: "TEXT", nullable: true),
                birth_date_unix_ms = table.Column<long>(type: "INTEGER", nullable: true),
                id_card_encrypted = table.Column<string>(type: "TEXT", nullable: true),
                phone_encrypted = table.Column<string>(type: "TEXT", nullable: true),
                emergency_contact_name = table.Column<string>(type: "TEXT", nullable: true),
                emergency_contact_phone_encrypted = table.Column<string>(type: "TEXT", nullable: true),
                home_address = table.Column<string>(type: "TEXT", nullable: true),
                clinical_info = table.Column<string>(type: "TEXT", nullable: true),
                created_at_unix_ms = table.Column<long>(type: "INTEGER", nullable: false),
                updated_at_unix_ms = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_patients", x => x.id));

        migrationBuilder.CreateTable(
            name: "app_state",
            columns: table => new
            {
                key = table.Column<string>(type: "TEXT", nullable: false),
                value = table.Column<string>(type: "TEXT", nullable: false),
                updated_at_unix_ms = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_app_state", x => x.key));

        migrationBuilder.CreateTable(
            name: "stimulation_records",
            columns: table => new
            {
                id = table.Column<long>(type: "INTEGER", nullable: false).Annotation("Sqlite:Autoincrement", true),
                patient_code = table.Column<string>(type: "TEXT", nullable: false),
                action = table.Column<string>(type: "TEXT", nullable: false),
                group_title = table.Column<string>(type: "TEXT", nullable: false),
                selected_channel_names = table.Column<string>(type: "TEXT", nullable: false),
                status = table.Column<string>(type: "TEXT", nullable: false),
                event_time_unix_ms = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_stimulation_records", x => x.id));

        migrationBuilder.CreateTable(
            name: "assessment_sessions",
            columns: table => new
            {
                id = table.Column<long>(type: "INTEGER", nullable: false).Annotation("Sqlite:Autoincrement", true),
                session_key = table.Column<string>(type: "TEXT", nullable: false),
                patient_code = table.Column<string>(type: "TEXT", nullable: false),
                started_at_unix_ms = table.Column<long>(type: "INTEGER", nullable: false),
                ended_at_unix_ms = table.Column<long>(type: "INTEGER", nullable: true),
                status = table.Column<string>(type: "TEXT", nullable: false),
                upload_status = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "local_only"),
                upload_batch_id = table.Column<string>(type: "TEXT", nullable: true),
                created_at_unix_ms = table.Column<long>(type: "INTEGER", nullable: false),
                updated_at_unix_ms = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_assessment_sessions", x => x.id));

        migrationBuilder.CreateTable(
            name: "assessment_module_records",
            columns: table => new
            {
                id = table.Column<long>(type: "INTEGER", nullable: false).Annotation("Sqlite:Autoincrement", true),
                session_id = table.Column<long>(type: "INTEGER", nullable: false),
                module_code = table.Column<string>(type: "TEXT", nullable: false),
                module_name = table.Column<string>(type: "TEXT", nullable: false),
                record_type = table.Column<string>(type: "TEXT", nullable: false),
                status = table.Column<string>(type: "TEXT", nullable: false),
                camera_name = table.Column<string>(type: "TEXT", nullable: true),
                output_dir = table.Column<string>(type: "TEXT", nullable: true),
                raw_video_path = table.Column<string>(type: "TEXT", nullable: true),
                normalized_video_path = table.Column<string>(type: "TEXT", nullable: true),
                audio_path = table.Column<string>(type: "TEXT", nullable: true),
                merged_video_path = table.Column<string>(type: "TEXT", nullable: true),
                form_payload_json = table.Column<string>(type: "TEXT", nullable: true),
                result_summary = table.Column<string>(type: "TEXT", nullable: true),
                started_at_unix_ms = table.Column<long>(type: "INTEGER", nullable: false),
                ended_at_unix_ms = table.Column<long>(type: "INTEGER", nullable: true),
                created_at_unix_ms = table.Column<long>(type: "INTEGER", nullable: false),
                updated_at_unix_ms = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_assessment_module_records", x => x.id));

        migrationBuilder.CreateTable(
            name: "assessment_events",
            columns: table => new
            {
                id = table.Column<long>(type: "INTEGER", nullable: false).Annotation("Sqlite:Autoincrement", true),
                session_id = table.Column<long>(type: "INTEGER", nullable: true),
                module_record_id = table.Column<long>(type: "INTEGER", nullable: true),
                event_type = table.Column<string>(type: "TEXT", nullable: false),
                event_time_unix_ms = table.Column<long>(type: "INTEGER", nullable: false),
                started_at_unix_ms = table.Column<long>(type: "INTEGER", nullable: true),
                ended_at_unix_ms = table.Column<long>(type: "INTEGER", nullable: true),
                Message = table.Column<string>(type: "TEXT", nullable: true),
                payload_json = table.Column<string>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_assessment_events", x => x.id);
                table.ForeignKey("FK_assessment_events_assessment_module_records_module_record_id", x => x.module_record_id, "assessment_module_records", "id");
            });

        migrationBuilder.CreateTable(
            name: "sensor_samples",
            columns: table => new
            {
                id = table.Column<long>(type: "INTEGER", nullable: false).Annotation("Sqlite:Autoincrement", true),
                session_id = table.Column<long>(type: "INTEGER", nullable: true),
                module_record_id = table.Column<long>(type: "INTEGER", nullable: true),
                source_type = table.Column<string>(type: "TEXT", nullable: false),
                source_name = table.Column<string>(type: "TEXT", nullable: true),
                sample_time_unix_ms = table.Column<long>(type: "INTEGER", nullable: false),
                sequence_no = table.Column<long>(type: "INTEGER", nullable: true),
                payload_json = table.Column<string>(type: "TEXT", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_sensor_samples", x => x.id));

        migrationBuilder.CreateTable(
            name: "eeg_recordings",
            columns: table => new
            {
                id = table.Column<long>(type: "INTEGER", nullable: false).Annotation("Sqlite:Autoincrement", true),
                module_record_id = table.Column<long>(type: "INTEGER", nullable: false),
                record_name = table.Column<string>(type: "TEXT", nullable: false),
                output_dir = table.Column<string>(type: "TEXT", nullable: false),
                channel_count = table.Column<int>(type: "INTEGER", nullable: false),
                sample_rate_hz = table.Column<int>(type: "INTEGER", nullable: false),
                page_seconds = table.Column<int>(type: "INTEGER", nullable: false),
                segment_seconds = table.Column<int>(type: "INTEGER", nullable: false),
                data_type = table.Column<string>(type: "TEXT", nullable: false),
                channel_names_json = table.Column<string>(type: "TEXT", nullable: false),
                config_json = table.Column<string>(type: "TEXT", nullable: false),
                started_at_unix_ms = table.Column<long>(type: "INTEGER", nullable: false),
                ended_at_unix_ms = table.Column<long>(type: "INTEGER", nullable: true),
                sample_count = table.Column<long>(type: "INTEGER", nullable: false),
                status = table.Column<string>(type: "TEXT", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_eeg_recordings", x => x.id));

        migrationBuilder.CreateTable(
            name: "eeg_data_segments",
            columns: table => new
            {
                id = table.Column<long>(type: "INTEGER", nullable: false).Annotation("Sqlite:Autoincrement", true),
                eeg_recording_id = table.Column<long>(type: "INTEGER", nullable: false),
                segment_index = table.Column<int>(type: "INTEGER", nullable: false),
                relative_path = table.Column<string>(type: "TEXT", nullable: false),
                start_sample_index = table.Column<long>(type: "INTEGER", nullable: false),
                sample_count = table.Column<long>(type: "INTEGER", nullable: false),
                started_at_unix_ms = table.Column<long>(type: "INTEGER", nullable: false),
                ended_at_unix_ms = table.Column<long>(type: "INTEGER", nullable: true),
                byte_length = table.Column<long>(type: "INTEGER", nullable: false),
                status = table.Column<string>(type: "TEXT", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_eeg_data_segments", x => x.id));

        migrationBuilder.CreateTable(
            name: "eeg_markers",
            columns: table => new
            {
                id = table.Column<long>(type: "INTEGER", nullable: false).Annotation("Sqlite:Autoincrement", true),
                eeg_recording_id = table.Column<long>(type: "INTEGER", nullable: false),
                name = table.Column<string>(type: "TEXT", nullable: false),
                shortcut = table.Column<string>(type: "TEXT", nullable: false),
                color_hex = table.Column<string>(type: "TEXT", nullable: false),
                event_time_unix_ms = table.Column<long>(type: "INTEGER", nullable: false),
                experiment_elapsed_ms = table.Column<long>(type: "INTEGER", nullable: false),
                sample_index = table.Column<long>(type: "INTEGER", nullable: false),
                page_index = table.Column<int>(type: "INTEGER", nullable: false),
                page_sample_index = table.Column<int>(type: "INTEGER", nullable: false),
                source = table.Column<string>(type: "TEXT", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_eeg_markers", x => x.id));

        migrationBuilder.CreateIndex("IX_users_login_name", "users", "login_name", unique: true);
        migrationBuilder.CreateIndex("IX_account_audit_logs_created_at_unix_ms", "account_audit_logs", "created_at_unix_ms");
        migrationBuilder.CreateIndex("IX_account_audit_logs_operator_user_id", "account_audit_logs", "operator_user_id");
        migrationBuilder.CreateIndex("IX_patients_patient_code", "patients", "patient_code", unique: true);
        migrationBuilder.CreateIndex("IX_stimulation_records_patient_code_event_time_unix_ms", "stimulation_records", new[] { "patient_code", "event_time_unix_ms" });
        migrationBuilder.CreateIndex("IX_assessment_sessions_session_key", "assessment_sessions", "session_key", unique: true);
        migrationBuilder.CreateIndex("IX_assessment_module_records_session_id_module_code", "assessment_module_records", new[] { "session_id", "module_code" });
        migrationBuilder.CreateIndex("IX_assessment_events_event_time_unix_ms", "assessment_events", "event_time_unix_ms");
        migrationBuilder.CreateIndex("IX_assessment_events_module_record_id_event_time_unix_ms", "assessment_events", new[] { "module_record_id", "event_time_unix_ms" });
        migrationBuilder.CreateIndex("IX_sensor_samples_source_type_sample_time_unix_ms", "sensor_samples", new[] { "source_type", "sample_time_unix_ms" });
        migrationBuilder.CreateIndex("IX_eeg_recordings_module_record_id", "eeg_recordings", "module_record_id");
        migrationBuilder.CreateIndex("IX_eeg_recordings_started_at_unix_ms", "eeg_recordings", "started_at_unix_ms");
        migrationBuilder.CreateIndex("IX_eeg_data_segments_eeg_recording_id_segment_index", "eeg_data_segments", new[] { "eeg_recording_id", "segment_index" }, unique: true);
        migrationBuilder.CreateIndex("IX_eeg_markers_eeg_recording_id_sample_index", "eeg_markers", new[] { "eeg_recording_id", "sample_index" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("account_audit_logs");
        migrationBuilder.DropTable("app_state");
        migrationBuilder.DropTable("assessment_events");
        migrationBuilder.DropTable("assessment_sessions");
        migrationBuilder.DropTable("eeg_data_segments");
        migrationBuilder.DropTable("eeg_markers");
        migrationBuilder.DropTable("eeg_recordings");
        migrationBuilder.DropTable("patients");
        migrationBuilder.DropTable("sensor_samples");
        migrationBuilder.DropTable("stimulation_records");
        migrationBuilder.DropTable("users");
        migrationBuilder.DropTable("assessment_module_records");
    }
}
