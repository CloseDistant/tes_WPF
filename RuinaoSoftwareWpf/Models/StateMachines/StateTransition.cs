namespace RuinaoSoftwareWpf;

public sealed record StateTransition<TState>(
    TState From,
    TState To,
    string Trigger,
    DateTimeOffset Timestamp,
    string OperatorId);
