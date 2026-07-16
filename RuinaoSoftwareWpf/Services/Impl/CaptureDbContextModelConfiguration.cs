namespace RuinaoSoftwareWpf;

using Microsoft.EntityFrameworkCore;

/// <summary>
/// 采集工作台 EF Core 表映射配置。
/// 单独拆出，避免 DbContext 文件承担过多表结构细节。
/// </summary>
internal static class CaptureDbContextModelConfiguration
{
    public static void Configure(ModelBuilder modelBuilder)
    {
        ConfigurePatient(modelBuilder);
        ConfigureAppState(modelBuilder);
        ConfigureFeatureVisibility(modelBuilder);
        ConfigurePrescription(modelBuilder);
        ConfigureStimulationRecord(modelBuilder);
        ConfigureAssessmentSession(modelBuilder);
        ConfigureAssessmentModuleRecord(modelBuilder);
        ConfigureAssessmentEvent(modelBuilder);
        ConfigureSensorSample(modelBuilder);
        ConfigureSessionTimelineEvent(modelBuilder);
        ConfigureEegRecording(modelBuilder);
        ConfigureEegDataSegment(modelBuilder);
        ConfigureEegMarker(modelBuilder);
    }

    private static void ConfigureSessionTimelineEvent(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<SessionTimelineEventEntity>();
        entity.ToTable("session_timeline_events");
        entity.HasKey(item => item.Id);
        entity.HasIndex(item => new { item.SessionId, item.SequenceNo }).IsUnique();
        entity.HasIndex(item => new { item.SessionKey, item.SessionElapsedMs });
        entity.HasIndex(item => new { item.ModuleCode, item.EventTimeUnixMs });
        entity.Property(item => item.SessionId).HasColumnName("session_id");
        entity.Property(item => item.SessionKey).HasColumnName("session_key");
        entity.Property(item => item.ModuleCode).HasColumnName("module_code");
        entity.Property(item => item.EventType).HasColumnName("event_type");
        entity.Property(item => item.SequenceNo).HasColumnName("sequence_no");
        entity.Property(item => item.EventTimeUnixMs).HasColumnName("event_time_unix_ms");
        entity.Property(item => item.SessionElapsedMs).HasColumnName("session_elapsed_ms");
        entity.Property(item => item.MonotonicTicks).HasColumnName("monotonic_ticks");
        entity.Property(item => item.MonotonicFrequency).HasColumnName("monotonic_frequency");
        entity.Property(item => item.SourceTimeUnixMs).HasColumnName("source_time_unix_ms");
        entity.Property(item => item.Message).HasColumnName("message");
        entity.Property(item => item.PayloadJson).HasColumnName("payload_json");
    }

    private static void ConfigurePatient(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<PatientEntity>();
        entity.ToTable("patients");
        entity.HasKey(item => item.Id);
        entity.HasIndex(item => item.PatientCode).IsUnique();
        entity.HasIndex(item => item.OwnerUserId);
        entity.Property(item => item.OwnerUserId).HasColumnName("owner_user_id");
        entity.Property(item => item.PatientCode).HasColumnName("patient_code");
        entity.Property(item => item.Name).HasColumnName("name");
        entity.Property(item => item.Gender).HasColumnName("gender");
        entity.Property(item => item.BirthDateUnixMs).HasColumnName("birth_date_unix_ms");
        entity.Property(item => item.IdCardEncrypted).HasColumnName("id_card_encrypted");
        entity.Property(item => item.PhoneEncrypted).HasColumnName("phone_encrypted");
        entity.Property(item => item.EmergencyContactName).HasColumnName("emergency_contact_name");
        entity.Property(item => item.EmergencyContactPhoneEncrypted).HasColumnName("emergency_contact_phone_encrypted");
        entity.Property(item => item.HomeAddress).HasColumnName("home_address");
        entity.Property(item => item.ClinicalInfo).HasColumnName("clinical_info");
        entity.Property(item => item.CreatedAtUnixMs).HasColumnName("created_at_unix_ms");
        entity.Property(item => item.UpdatedAtUnixMs).HasColumnName("updated_at_unix_ms");
    }

    private static void ConfigureAppState(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<AppStateEntity>();
        entity.ToTable("app_state");
        entity.HasKey(item => item.Key);
        entity.Property(item => item.Key).HasColumnName("key");
        entity.Property(item => item.Value).HasColumnName("value");
        entity.Property(item => item.UpdatedAtUnixMs).HasColumnName("updated_at_unix_ms");
    }

    private static void ConfigureFeatureVisibility(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<FeatureVisibilityEntity>();
        entity.ToTable("feature_visibility");
        entity.HasKey(item => item.FeatureKey);
        entity.Property(item => item.FeatureKey).HasColumnName("feature_key");
        entity.Property(item => item.IsVisible).HasColumnName("is_visible");
        entity.Property(item => item.UpdatedByUserId).HasColumnName("updated_by_user_id");
        entity.Property(item => item.UpdatedAtUnixMs).HasColumnName("updated_at_unix_ms");
    }

    private static void ConfigurePrescription(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<PrescriptionEntity>();
        entity.ToTable("prescriptions");
        entity.HasKey(item => item.Id);
        entity.HasIndex(item => item.Name);
        entity.Property(item => item.Id).HasColumnName("id");
        entity.Property(item => item.Name).HasColumnName("name");
        entity.Property(item => item.Indication).HasColumnName("indication");
        entity.Property(item => item.StimulationType).HasColumnName("stimulation_type");
        entity.Property(item => item.CurrentMilliamp).HasColumnName("current_milliamp");
        entity.Property(item => item.DeliveryMode).HasColumnName("delivery_mode");
        entity.Property(item => item.TotalDurationMinutes).HasColumnName("total_duration_minutes");
        entity.Property(item => item.IntervalMinutes).HasColumnName("interval_minutes");
        entity.Property(item => item.SessionDurationMinutes).HasColumnName("session_duration_minutes");
        entity.Property(item => item.Course).HasColumnName("course");
        entity.Property(item => item.RampUpSeconds).HasColumnName("ramp_up_seconds");
        entity.Property(item => item.RampDownSeconds).HasColumnName("ramp_down_seconds");
        entity.Property(item => item.EvidenceGrade).HasColumnName("evidence_grade");
        entity.Property(item => item.IsBuiltin).HasColumnName("is_builtin");
        entity.Property(item => item.CreatedAtUnixMs).HasColumnName("created_at_unix_ms");
        entity.Property(item => item.UpdatedAtUnixMs).HasColumnName("updated_at_unix_ms");
    }

    private static void ConfigureStimulationRecord(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<StimulationRecordEntity>();
        entity.ToTable("stimulation_records");
        entity.HasKey(item => item.Id);
        entity.HasIndex(item => new { item.PatientCode, item.EventTimeUnixMs });
        entity.HasIndex(item => new { item.OperatorUserId, item.EventTimeUnixMs });
        entity.Property(item => item.OperatorUserId).HasColumnName("operator_user_id");
        entity.Property(item => item.PatientCode).HasColumnName("patient_code");
        entity.Property(item => item.Action).HasColumnName("action");
        entity.Property(item => item.GroupTitle).HasColumnName("group_title");
        entity.Property(item => item.SelectedChannelNames).HasColumnName("selected_channel_names");
        entity.Property(item => item.Status).HasColumnName("status");
        entity.Property(item => item.StimulationType).HasColumnName("stimulation_type");
        entity.Property(item => item.PrescriptionName).HasColumnName("prescription_name");
        entity.Property(item => item.AdverseReactionRecord).HasColumnName("adverse_reaction_record");
        entity.Property(item => item.ParameterSnapshotJson).HasColumnName("parameter_snapshot_json");
        entity.Property(item => item.EventTimeUnixMs).HasColumnName("event_time_unix_ms");
    }

    private static void ConfigureAssessmentSession(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<AssessmentSessionEntity>();
        entity.ToTable("assessment_sessions");
        entity.HasKey(item => item.Id);
        entity.HasIndex(item => item.SessionKey).IsUnique();
        entity.Property(item => item.SessionKey).HasColumnName("session_key");
        entity.Property(item => item.PatientCode).HasColumnName("patient_code");
        entity.Property(item => item.StartedAtUnixMs).HasColumnName("started_at_unix_ms");
        entity.Property(item => item.EndedAtUnixMs).HasColumnName("ended_at_unix_ms");
        entity.Property(item => item.UploadStatus).HasColumnName("upload_status").HasDefaultValue("local_only");
        entity.Property(item => item.UploadBatchId).HasColumnName("upload_batch_id");
        entity.Property(item => item.CreatedAtUnixMs).HasColumnName("created_at_unix_ms");
        entity.Property(item => item.UpdatedAtUnixMs).HasColumnName("updated_at_unix_ms");
    }

    private static void ConfigureAssessmentModuleRecord(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<AssessmentModuleRecordEntity>();
        entity.ToTable("assessment_module_records");
        entity.HasKey(item => item.Id);
        entity.HasIndex(item => new { item.SessionId, item.ModuleCode });
        entity.Property(item => item.SessionId).HasColumnName("session_id");
        entity.Property(item => item.ModuleCode).HasColumnName("module_code");
        entity.Property(item => item.ModuleName).HasColumnName("module_name");
        entity.Property(item => item.RecordType).HasColumnName("record_type");
        entity.Property(item => item.CameraName).HasColumnName("camera_name");
        entity.Property(item => item.OutputDir).HasColumnName("output_dir");
        entity.Property(item => item.RawVideoPath).HasColumnName("raw_video_path");
        entity.Property(item => item.NormalizedVideoPath).HasColumnName("normalized_video_path");
        entity.Property(item => item.AudioPath).HasColumnName("audio_path");
        entity.Property(item => item.MergedVideoPath).HasColumnName("merged_video_path");
        entity.Property(item => item.FormPayloadJson).HasColumnName("form_payload_json");
        entity.Property(item => item.ResultSummary).HasColumnName("result_summary");
        entity.Property(item => item.StartedAtUnixMs).HasColumnName("started_at_unix_ms");
        entity.Property(item => item.EndedAtUnixMs).HasColumnName("ended_at_unix_ms");
        entity.Property(item => item.CreatedAtUnixMs).HasColumnName("created_at_unix_ms");
        entity.Property(item => item.UpdatedAtUnixMs).HasColumnName("updated_at_unix_ms");
    }

    private static void ConfigureAssessmentEvent(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<AssessmentEventEntity>();
        entity.ToTable("assessment_events");
        entity.HasKey(item => item.Id);
        entity.HasIndex(item => item.EventTimeUnixMs);
        entity.HasIndex(item => new { item.ModuleRecordId, item.EventTimeUnixMs });
        entity.Property(item => item.SessionId).HasColumnName("session_id");
        entity.Property(item => item.ModuleRecordId).HasColumnName("module_record_id");
        entity.Property(item => item.EventType).HasColumnName("event_type");
        entity.Property(item => item.EventTimeUnixMs).HasColumnName("event_time_unix_ms");
        entity.Property(item => item.StartedAtUnixMs).HasColumnName("started_at_unix_ms");
        entity.Property(item => item.EndedAtUnixMs).HasColumnName("ended_at_unix_ms");
        entity.Property(item => item.PayloadJson).HasColumnName("payload_json");
        entity.HasOne(item => item.ModuleRecord)
            .WithMany()
            .HasForeignKey(item => item.ModuleRecordId);
    }

    private static void ConfigureSensorSample(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<SensorSampleEntity>();
        entity.ToTable("sensor_samples");
        entity.HasKey(item => item.Id);
        entity.HasIndex(item => new { item.SourceType, item.SampleTimeUnixMs });
        entity.Property(item => item.SessionId).HasColumnName("session_id");
        entity.Property(item => item.ModuleRecordId).HasColumnName("module_record_id");
        entity.Property(item => item.SourceType).HasColumnName("source_type");
        entity.Property(item => item.SourceName).HasColumnName("source_name");
        entity.Property(item => item.SampleTimeUnixMs).HasColumnName("sample_time_unix_ms");
        entity.Property(item => item.SequenceNo).HasColumnName("sequence_no");
        entity.Property(item => item.PayloadJson).HasColumnName("payload_json");
    }

    private static void ConfigureEegRecording(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<EegRecordingEntity>();
        entity.ToTable("eeg_recordings");
        entity.HasKey(item => item.Id);
        entity.HasIndex(item => item.ModuleRecordId);
        entity.HasIndex(item => item.StartedAtUnixMs);
        entity.Property(item => item.ModuleRecordId).HasColumnName("module_record_id");
        entity.Property(item => item.RecordName).HasColumnName("record_name");
        entity.Property(item => item.OutputDir).HasColumnName("output_dir");
        entity.Property(item => item.ChannelCount).HasColumnName("channel_count");
        entity.Property(item => item.SampleRateHz).HasColumnName("sample_rate_hz");
        entity.Property(item => item.PageSeconds).HasColumnName("page_seconds");
        entity.Property(item => item.SegmentSeconds).HasColumnName("segment_seconds");
        entity.Property(item => item.DataType).HasColumnName("data_type");
        entity.Property(item => item.ChannelNamesJson).HasColumnName("channel_names_json");
        entity.Property(item => item.ConfigJson).HasColumnName("config_json");
        entity.Property(item => item.StartedAtUnixMs).HasColumnName("started_at_unix_ms");
        entity.Property(item => item.EndedAtUnixMs).HasColumnName("ended_at_unix_ms");
        entity.Property(item => item.SampleCount).HasColumnName("sample_count");
        entity.Property(item => item.Status).HasColumnName("status");
    }

    private static void ConfigureEegDataSegment(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<EegDataSegmentEntity>();
        entity.ToTable("eeg_data_segments");
        entity.HasKey(item => item.Id);
        entity.HasIndex(item => new { item.EegRecordingId, item.SegmentIndex }).IsUnique();
        entity.Property(item => item.EegRecordingId).HasColumnName("eeg_recording_id");
        entity.Property(item => item.SegmentIndex).HasColumnName("segment_index");
        entity.Property(item => item.RelativePath).HasColumnName("relative_path");
        entity.Property(item => item.StartSampleIndex).HasColumnName("start_sample_index");
        entity.Property(item => item.SampleCount).HasColumnName("sample_count");
        entity.Property(item => item.StartedAtUnixMs).HasColumnName("started_at_unix_ms");
        entity.Property(item => item.EndedAtUnixMs).HasColumnName("ended_at_unix_ms");
        entity.Property(item => item.ByteLength).HasColumnName("byte_length");
        entity.Property(item => item.Status).HasColumnName("status");
    }

    private static void ConfigureEegMarker(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<EegMarkerEntity>();
        entity.ToTable("eeg_markers");
        entity.HasKey(item => item.Id);
        entity.HasIndex(item => new { item.EegRecordingId, item.SampleIndex });
        entity.Property(item => item.EegRecordingId).HasColumnName("eeg_recording_id");
        entity.Property(item => item.Name).HasColumnName("name");
        entity.Property(item => item.Shortcut).HasColumnName("shortcut");
        entity.Property(item => item.ColorHex).HasColumnName("color_hex");
        entity.Property(item => item.EventTimeUnixMs).HasColumnName("event_time_unix_ms");
        entity.Property(item => item.ExperimentElapsedMs).HasColumnName("experiment_elapsed_ms");
        entity.Property(item => item.SampleIndex).HasColumnName("sample_index");
        entity.Property(item => item.PageIndex).HasColumnName("page_index");
        entity.Property(item => item.PageSampleIndex).HasColumnName("page_sample_index");
        entity.Property(item => item.Source).HasColumnName("source");
    }
}
