namespace RuinaoSoftwareWpf.ApplicationContracts;

public sealed record StimulationChannelParameters(
    int ChannelNumber, int AnodeElectrodeNumber, int CathodeElectrodeNumber,
    decimal CurrentMilliampere, decimal FrequencyHz, int RampUpSeconds,
    int RampDownSeconds, int DurationSeconds, int? IntervalSeconds = null);
