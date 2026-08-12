namespace RuinaoSoftwareWpf;

internal interface IStimulationImpedanceChannel
{
    string Name { get; }

    decimal? ImpedanceOhms { get; }

    StimulationImpedanceStatus ImpedanceStatus { get; }
}
