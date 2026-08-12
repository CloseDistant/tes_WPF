namespace RuinaoSoftwareWpf;

public sealed class StimulationRecordRow
{
    public StimulationRecordRow(int rowNumber, StimulationTreatmentRecord record)
    {
        RowNumber = rowNumber;
        Id = record.Id;
        PatientDisplay = record.PatientDisplay;
        StimulationType = record.StimulationType;
        TreatmentDate = record.TreatmentDate.ToString("yyyy-MM-dd");
        PrescriptionName = record.PrescriptionName;
        AdverseReactionRecord = record.AdverseReactionRecord;
        ParameterRecord = record.ParameterRecord;
    }

    public int RowNumber { get; }
    public long Id { get; }
    public string PatientDisplay { get; }
    public string StimulationType { get; }
    public string TreatmentDate { get; }
    public string PrescriptionName { get; }
    public string AdverseReactionRecord { get; }
    public PrescriptionDefinition ParameterRecord { get; }
}
