namespace RuinaoSoftwareWpf.ApplicationContracts;

public sealed record EegMarker(
    string Code, string DisplayName, string Shortcut, string ColorHex,
    long AbsoluteTimestampMilliseconds, TimeSpan ExperimentTime, long SampleIndex, string Source);
