namespace RuinaoSoftwareWpf.Tests;

using RuinaoTesProtocol.V14;
using Xunit;

public sealed class TesV14RegisterPayloadCodecTests
{
    [Fact]
    public void EncodeRead_UsesTwoByteCountAndBigEndianRegisterEntry()
    {
        var payload = TesV14RegisterPayloadCodec.EncodeRead([0x0900]);

        Assert.Equal(new byte[] { 0x00, 0x01, 0x09, 0x00, 0x00, 0x00, 0x00, 0x00 }, payload);
    }

    [Fact]
    public void EncodeWrite_AndDecode_RoundTripBigEndianValue()
    {
        var payload = TesV14RegisterPayloadCodec.Encode(
            [new TesV14RegisterValue(0x2100, 0x12345678)]);

        Assert.Equal(new byte[] { 0x00, 0x01, 0x21, 0x00, 0x12, 0x34, 0x56, 0x78 }, payload);
        Assert.True(TesV14RegisterPayloadCodec.TryDecode(payload, out var registers, out var error), error);
        Assert.Equal(new TesV14RegisterValue(0x2100, 0x12345678), Assert.Single(registers));
    }

    [Fact]
    public void BuildReadRegisters_ProducesParseableV14ReadFrame()
    {
        var api = new TesV14ProtocolApi(destinationAddress: TesV14ProtocolConstants.BackplaneAddress);

        var bytes = api.BuildReadRegisters([0x0900], out var sequence);

        Assert.True(TesV14ProtocolCodec.TryParseFrame(bytes, out var frame, out var error), error);
        Assert.NotNull(frame);
        Assert.Equal(TesV14Command.Read, frame.Command);
        Assert.Equal(sequence, frame.SendSequence);
        Assert.Equal(TesV14ProtocolConstants.BackplaneAddress, frame.DestinationAddress);
        Assert.Equal(new byte[] { 0x00, 0x01, 0x09, 0x00, 0x00, 0x00, 0x00, 0x00 }, frame.Payload);
    }

    [Fact]
    public void DiagnosticSequence_WrapsFrom65535To1()
    {
        var api = new TesV14ProtocolApi();
        api.SetNextSequenceForDiagnostics(65534);

        api.BuildBackplaneHandshake(out var first);
        api.BuildBackplaneHandshake(out var second);
        api.BuildBackplaneHandshake(out var third);
        api.BuildBackplaneHandshake(out var fourth);

        Assert.Equal(new ushort[] { 65534, 65535, 1, 2 }, new[] { first, second, third, fourth });
        Assert.Equal((ushort)3, api.NextSequenceForDiagnostics);
    }

    [Fact]
    public void DiagnosticSequence_RejectsZero()
    {
        var api = new TesV14ProtocolApi();

        Assert.Throws<ArgumentOutOfRangeException>(() => api.SetNextSequenceForDiagnostics(0));
    }

    [Fact]
    public void Decode_RejectsCountLengthMismatch()
    {
        Assert.False(TesV14RegisterPayloadCodec.TryDecode(
            new byte[] { 0x00, 0x02, 0x09, 0x00, 0x00, 0x00, 0x00, 0x01 },
            out _,
            out var error));
        Assert.Contains("长度不匹配", error);
    }

    [Fact]
    public void ProductInfoTextCodec_Groups32_Utf8Text_RoundTripsWithinSelectedGroup()
    {
        const string text = "123132睿脑";

        var registers = TesV14ProductInfoTextCodec.Encode(TesV14ProductInfoGrouping.Groups32, 31, text);
        var decoded = TesV14ProductInfoTextCodec.Decode(TesV14ProductInfoGrouping.Groups32, 31, registers);

        Assert.Equal(32, registers.Count);
        Assert.Equal(0x04E0, registers[0].Address);
        Assert.Equal(TesV14ProductInfoTextCodec.EndAddress, registers[^1].Address);
        Assert.Equal(0x31323331u, registers[0].Value);
        Assert.Equal(text, decoded);
    }

    [Fact]
    public void ProductInfoTextCodec_Groups16_ClearsUnusedAreaWithZeroes()
    {
        var registers = TesV14ProductInfoTextCodec.Encode(TesV14ProductInfoGrouping.Groups16, 0, "A");

        Assert.Equal(64, registers.Count);
        Assert.Equal(0x41000000u, registers[0].Value);
        Assert.All(registers.Skip(1), register => Assert.Equal(0u, register.Value));
    }

    [Fact]
    public void ProductInfoTextCodec_RejectsTextWithoutTerminatorSpace()
    {
        var layout = TesV14ProductInfoTextCodec.GetLayout(TesV14ProductInfoGrouping.Groups32);
        var oversized = new string('A', layout.CapacityBytes);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TesV14ProductInfoTextCodec.Encode(TesV14ProductInfoGrouping.Groups32, 0, oversized));
    }

    [Theory]
    [InlineData(TesV14ProductInfoGrouping.Groups16, 16, 64, 256, 255)]
    [InlineData(TesV14ProductInfoGrouping.Groups32, 32, 32, 128, 127)]
    public void ProductInfoTextCodec_ExposesBothSupportedLayouts(
        TesV14ProductInfoGrouping grouping,
        int groupCount,
        int registersPerGroup,
        int capacityBytes,
        int maximumTextBytes)
    {
        var layout = TesV14ProductInfoTextCodec.GetLayout(grouping);

        Assert.Equal(groupCount, layout.GroupCount);
        Assert.Equal(registersPerGroup, layout.RegistersPerGroup);
        Assert.Equal(capacityBytes, layout.CapacityBytes);
        Assert.Equal(maximumTextBytes, layout.MaximumTextBytes);
    }

    [Theory]
    [InlineData("ABCDEFGHIJKL111", 4)]
    [InlineData("ABCDEFGHIJKL1111", 5)]
    [InlineData("", 1)]
    public void ProductInfoTextCodec_RequiredRegisterCount_IncludesTerminator(
        string text,
        int expectedRegisters)
    {
        Assert.Equal(expectedRegisters, TesV14ProductInfoTextCodec.GetRequiredRegisterCount(text));
    }

    [Fact]
    public void ProductInfoTextCodec_Groups32_AcceptsExactly127Utf8Bytes()
    {
        var text = new string('A', 127);

        var registers = TesV14ProductInfoTextCodec.Encode(TesV14ProductInfoGrouping.Groups32, 0, text);
        var frame = new TesV14ProtocolApi().BuildWriteRegisters(registers, out _);

        Assert.Equal(32, registers.Count);
        Assert.Equal(32, TesV14ProductInfoTextCodec.GetRequiredRegisterCount(text));
        Assert.Equal(0x41414100u, registers[^1].Value);
        Assert.Equal(216, frame.Length);
        Assert.Equal(text, TesV14ProductInfoTextCodec.Decode(TesV14ProductInfoGrouping.Groups32, 0, registers));
    }

    [Fact]
    public void ProductInfoTextCodec_Groups32_Rejects128Utf8Bytes()
    {
        var text = new string('A', 128);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TesV14ProductInfoTextCodec.Encode(TesV14ProductInfoGrouping.Groups32, 0, text));
    }
}
