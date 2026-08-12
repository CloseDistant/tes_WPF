namespace RuinaoSoftwareWpf;

public sealed record StimulationTreatmentRecord(
    long Id,
    string RunId,
    string PatientDisplay,
    string StimulationType,
    DateOnly TreatmentDate,
    string PrescriptionName,
    string AdverseReactionRecord,
    PrescriptionDefinition ParameterRecord,
    IReadOnlyList<StimulationChannelTreatmentRecord> Channels);
