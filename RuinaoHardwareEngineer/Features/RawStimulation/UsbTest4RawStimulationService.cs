using RuinaoTesHardware;
using RuinaoTesProtocol.V14;

namespace RuinaoHardwareEngineer.Features.RawStimulation;

public sealed record UsbTest4RawConfigurationResult(
    IReadOnlyList<BackplaneRegisterOperationResult> WaveformWrites,
    BackplaneRegisterOperationResult ControlWrite);

/// <summary>
/// 工程师工具专用的usbtest4原始刺激调用服务。
/// 只组织原始寄存器并调用共享DLL的通用读写边界，不向产品软件暴露这些临时参数。
/// </summary>
public sealed class UsbTest4RawStimulationService
{
    private readonly BackplaneClient client;

    public UsbTest4RawStimulationService(BackplaneClient client)
    {
        this.client = client;
    }

    public async Task<UsbTest4RawConfigurationResult> SendConfigurationAsync(
        UsbTest4RawStimulationConfiguration configuration,
        BackplaneConnectionOptions options,
        CancellationToken cancellationToken = default)
    {
        UsbTest4RawStimulationLayout.Validate(configuration);
        var writes = new List<BackplaneRegisterOperationResult>(configuration.Waveforms.Count);
        for (var index = 0; index < configuration.Waveforms.Count; index++)
        {
            var registers = UsbTest4RawStimulationLayout.BuildWaveformRegisters(configuration, index);
            writes.Add(await client.WriteRegistersAsync(
                configuration.BoardAddress,
                registers,
                options,
                cancellationToken));
        }

        var controlRegisters = UsbTest4RawStimulationLayout.BuildControlRegisters(configuration);
        var controlWrite = await client.WriteRegistersAsync(
            configuration.BoardAddress,
            controlRegisters,
            options,
            cancellationToken);
        return new UsbTest4RawConfigurationResult(writes, controlWrite);
    }

    public Task<BackplaneRegisterOperationResult> StartAsync(
        byte boardAddress,
        BackplaneConnectionOptions options,
        CancellationToken cancellationToken = default) =>
        WriteCommandAsync(boardAddress, UsbTest4RawStimulationLayout.StartRegister, 0, options, cancellationToken);

    public Task<BackplaneRegisterOperationResult> StopAsync(
        byte boardAddress,
        BackplaneConnectionOptions options,
        CancellationToken cancellationToken = default) =>
        WriteCommandAsync(boardAddress, UsbTest4RawStimulationLayout.StopRegister, 0, options, cancellationToken);

    public Task<BackplaneRegisterOperationResult> SetAllChannelsHighAsync(
        byte boardAddress,
        BackplaneConnectionOptions options,
        CancellationToken cancellationToken = default) =>
        WriteCommandAsync(boardAddress, UsbTest4RawStimulationLayout.PowerSetHighRegister, 0xFF, options, cancellationToken);

    public Task<BackplaneRegisterOperationResult> SetAllChannelsLowAsync(
        byte boardAddress,
        BackplaneConnectionOptions options,
        CancellationToken cancellationToken = default) =>
        WriteCommandAsync(boardAddress, UsbTest4RawStimulationLayout.PowerSetLowRegister, 0xFF, options, cancellationToken);

    private Task<BackplaneRegisterOperationResult> WriteCommandAsync(
        byte boardAddress,
        ushort registerAddress,
        uint value,
        BackplaneConnectionOptions options,
        CancellationToken cancellationToken)
    {
        if (boardAddress > 0x07)
        {
            throw new ArgumentOutOfRangeException(nameof(boardAddress), "业务板地址必须在0x00到0x07之间。");
        }

        return client.WriteRegistersAsync(
            boardAddress,
            [new TesV14RegisterValue(registerAddress, value)],
            options,
            cancellationToken);
    }
}
