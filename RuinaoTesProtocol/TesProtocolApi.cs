namespace RuinaoTesProtocol;

/// <summary>
/// High-level API used by upper software.
/// It hides sequence generation and common payload packing, then returns the final byte[] that should be sent to hardware.
/// </summary>
public sealed class TesProtocolApi
{
    private ushort nextSequence = 1;

    public byte SourceAddress { get; }

    public byte DestinationAddress { get; set; }

    public TesProtocolApi(
        byte sourceAddress = TesProtocolConstants.HostAddress,
        byte destinationAddress = TesProtocolConstants.BackplaneAddress)
    {
        SourceAddress = sourceAddress;
        DestinationAddress = destinationAddress;
    }

    public byte[] BuildHandshake(bool ackRequired = true)
    {
        var control = ackRequired ? TesFrameControl.AckRequired : TesFrameControl.None;
        return Build(control, TesCommand.Handshake, ReadOnlySpan<byte>.Empty);
    }

    public byte[] BuildAck(ushort ackSequence)
    {
        return TesProtocolCodec.BuildFrame(
            TesFrameControl.None,
            TesCommand.Ack,
            SourceAddress,
            DestinationAddress,
            NextSequence(),
            ackSequence,
            ReadOnlySpan<byte>.Empty);
    }

    public byte[] BuildReadRegister(TesRegister register, bool ackRequired = true)
    {
        Span<byte> payload = stackalloc byte[2];
        var offset = 0;
        TesProtocolCodec.WriteUInt16LE(payload, ref offset, (ushort)register);

        var control = ackRequired ? TesFrameControl.AckRequired : TesFrameControl.None;
        return Build(control, TesCommand.Request, payload);
    }

    public byte[] BuildWriteRegister(TesRegister register, ReadOnlySpan<byte> registerData, bool ackRequired = true)
    {
        var payload = new byte[2 + registerData.Length];
        var offset = 0;
        TesProtocolCodec.WriteUInt16LE(payload, ref offset, (ushort)register);
        registerData.CopyTo(payload.AsSpan(offset));

        var control = TesFrameControl.Write;
        if (ackRequired)
        {
            control |= TesFrameControl.AckRequired;
        }

        return Build(control, TesCommand.Request, payload);
    }

    public byte[] BuildReboot(bool ackRequired = true)
    {
        return BuildWriteRegister(TesRegister.Reboot, ReadOnlySpan<byte>.Empty, ackRequired);
    }

    public byte[] BuildPowerOff(bool ackRequired = true)
    {
        return BuildWriteRegister(TesRegister.PowerOff, ReadOnlySpan<byte>.Empty, ackRequired);
    }

    public byte[] BuildStartStimChannel(byte channelNumber, bool ackRequired = true)
    {
        return BuildStimChannelOutput(new[] { new StimChannelOperation(channelNumber, true) }, ackRequired);
    }

    public byte[] BuildStopStimChannel(byte channelNumber, bool ackRequired = true)
    {
        return BuildStimChannelOutput(new[] { new StimChannelOperation(channelNumber, false) }, ackRequired);
    }

    public byte[] BuildStimChannelOutput(IEnumerable<StimChannelOperation> operations, bool ackRequired = true)
    {
        var operationList = operations.ToList();
        if (operationList.Count == 0)
        {
            throw new ArgumentException("At least one channel operation is required.", nameof(operations));
        }

        var registerData = new byte[operationList.Count * 4];
        var offset = 0;
        foreach (var operation in operationList)
        {
            registerData[offset++] = operation.ChannelNumber;
            registerData[offset++] = operation.Start ? (byte)1 : (byte)0;
            registerData[offset++] = 0;
            registerData[offset++] = 0;
        }

        return BuildWriteRegister(TesRegister.OutputStimulationChannel, registerData, ackRequired);
    }

    public byte[] BuildStartTemperatureAcquisition(ushort periodMs = 200, uint channelMask = 0, bool ackRequired = true)
    {
        return BuildSetAcquisition(new TesAcquisitionControl(TesAcquisitionKind.Temperature, true, periodMs, channelMask), ackRequired);
    }

    public byte[] BuildStopTemperatureAcquisition(uint channelMask = 0, bool ackRequired = true)
    {
        return BuildSetAcquisition(new TesAcquisitionControl(TesAcquisitionKind.Temperature, false, 0, channelMask), ackRequired);
    }

    public byte[] BuildStartImpedanceAcquisition(ushort periodMs = 200, uint channelMask = 0, bool ackRequired = true)
    {
        return BuildSetAcquisition(new TesAcquisitionControl(TesAcquisitionKind.Impedance, true, periodMs, channelMask), ackRequired);
    }

    public byte[] BuildStopImpedanceAcquisition(uint channelMask = 0, bool ackRequired = true)
    {
        return BuildSetAcquisition(new TesAcquisitionControl(TesAcquisitionKind.Impedance, false, 0, channelMask), ackRequired);
    }

    public byte[] BuildSetAcquisition(TesAcquisitionControl control, bool ackRequired = true)
    {
        if (control.Enabled && control.PeriodMs == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(control), "Enabled acquisition requires PeriodMs > 0.");
        }

        Span<byte> registerData = stackalloc byte[9];
        var offset = 0;
        TesProtocolCodec.WriteUInt16LE(registerData, ref offset, (ushort)control.Kind);
        registerData[offset++] = control.Enabled ? (byte)1 : (byte)0;
        TesProtocolCodec.WriteUInt16LE(registerData, ref offset, control.PeriodMs);
        WriteUInt32LE(registerData, ref offset, control.ChannelMask);

        return BuildWriteRegister(TesRegister.AcquisitionParameters, registerData, ackRequired);
    }

    public bool TryParseRealtimeReport(TesFrame frame, out TesRealtimeReport? report, out string error)
    {
        report = null;

        if (frame.Command != TesCommand.Response)
        {
            error = $"Frame command is {frame.Command}, not Response.";
            return false;
        }

        if (frame.Payload.Length < 2)
        {
            error = "Response payload is too short to contain a register address.";
            return false;
        }

        var offset = 0;
        var register = (TesRegister)TesProtocolCodec.ReadUInt16LE(frame.Payload, ref offset);
        var data = frame.Payload.AsSpan(offset);

        switch (register)
        {
            case TesRegister.Temperature:
                return TryParseTemperatureReport(frame, data, out report, out error);
            case TesRegister.Impedance:
                return TryParseImpedanceReport(frame, data, out report, out error);
            default:
                error = $"Register 0x{(ushort)register:X4} is not a realtime temperature or impedance report.";
                return false;
        }
    }

    public bool TryParseFrame(ReadOnlySpan<byte> bytes, out TesFrame? frame, out string error)
    {
        return TesProtocolCodec.TryParseFrame(bytes, out frame, out error);
    }

    private byte[] Build(TesFrameControl control, TesCommand command, ReadOnlySpan<byte> payload)
    {
        return TesProtocolCodec.BuildFrame(
            control,
            command,
            SourceAddress,
            DestinationAddress,
            NextSequence(),
            0,
            payload);
    }

    private ushort NextSequence()
    {
        var value = nextSequence++;
        if (nextSequence == 0)
        {
            nextSequence = 1;
        }

        return value;
    }

    private static bool TryParseTemperatureReport(
        TesFrame frame,
        ReadOnlySpan<byte> data,
        out TesRealtimeReport? report,
        out string error)
    {
        report = null;
        if (data.Length % 6 != 0)
        {
            error = $"Temperature payload length must be a multiple of 6, actual {data.Length}.";
            return false;
        }

        var samples = new List<TesTemperatureSample>();
        var offset = 0;
        while (offset < data.Length)
        {
            var channel = data[offset++];
            var electrode = data[offset++];
            var role = (TesTemperatureSensorRole)data[offset++];
            offset++;
            var centiCelsius = ReadInt16LE(data, ref offset);
            samples.Add(new TesTemperatureSample(channel, electrode, role, centiCelsius / 100.0));
        }

        report = new TesRealtimeReport(
            TesAcquisitionKind.Temperature,
            frame.SendSequence,
            DateTimeOffset.Now,
            samples,
            Array.Empty<TesImpedanceSample>());
        error = string.Empty;
        return true;
    }

    private static bool TryParseImpedanceReport(
        TesFrame frame,
        ReadOnlySpan<byte> data,
        out TesRealtimeReport? report,
        out string error)
    {
        report = null;
        if (data.Length % 6 != 0)
        {
            error = $"Impedance payload length must be a multiple of 6, actual {data.Length}.";
            return false;
        }

        var samples = new List<TesImpedanceSample>();
        var offset = 0;
        while (offset < data.Length)
        {
            var channel = data[offset++];
            var anode = data[offset++];
            var cathode = data[offset++];
            offset++;
            var ohms = TesProtocolCodec.ReadUInt16LE(data, ref offset);
            samples.Add(new TesImpedanceSample(channel, anode, cathode, ohms));
        }

        report = new TesRealtimeReport(
            TesAcquisitionKind.Impedance,
            frame.SendSequence,
            DateTimeOffset.Now,
            Array.Empty<TesTemperatureSample>(),
            samples);
        error = string.Empty;
        return true;
    }

    private static short ReadInt16LE(ReadOnlySpan<byte> buffer, ref int offset)
    {
        return unchecked((short)TesProtocolCodec.ReadUInt16LE(buffer, ref offset));
    }

    private static void WriteUInt32LE(Span<byte> buffer, ref int offset, uint value)
    {
        buffer[offset++] = (byte)(value & 0xFF);
        buffer[offset++] = (byte)((value >> 8) & 0xFF);
        buffer[offset++] = (byte)((value >> 16) & 0xFF);
        buffer[offset++] = (byte)((value >> 24) & 0xFF);
    }
}
