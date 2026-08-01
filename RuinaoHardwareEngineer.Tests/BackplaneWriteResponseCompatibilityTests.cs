using RuinaoTesHardware;
using RuinaoTesProtocol.V14;
using Xunit;

namespace RuinaoHardwareEngineer.Tests;

public sealed class BackplaneWriteResponseCompatibilityTests
{
    [Fact]
    public async Task WriteRegisters_WhenHardwareReturnsFourByteZeroStatus_ContinuesAsAcceptedWrite()
    {
        var transport = new FourByteWriteStatusTransport(0);
        await using var client = new BackplaneClient(new UnusedDiscovery(), transport);
        var requestedRegisters = new[]
        {
            new TesV14RegisterValue(0x3020, 8),
            new TesV14RegisterValue(0x3021, 2_000_000),
        };

        var result = await client.WriteRegistersAsync(
            0x01,
            requestedRegisters,
            new BackplaneConnectionOptions(0x01, TimeSpan.FromSeconds(1)),
            TestContext.Current.CancellationToken);

        Assert.Equal(requestedRegisters, result.Registers);
        Assert.Equal((byte)TesV14Command.Response, result.ResponseCommand);
        Assert.Equal(result.RequestSequence, result.ResponseAckSequence);
        Assert.Equal(BackplaneWriteResponseKind.StatusCode, result.WriteResponseKind);
        Assert.Equal(0U, result.HardwareStatusCode);
    }

    [Fact]
    public async Task WriteRegisters_WhenHardwareReturnsNonZeroStatus_ReportsHardwareFailure()
    {
        var transport = new FourByteWriteStatusTransport(5);
        await using var client = new BackplaneClient(new UnusedDiscovery(), transport);

        var exception = await Assert.ThrowsAsync<BackplaneConnectionException>(
            () => client.WriteRegistersAsync(
                0x01,
                [new TesV14RegisterValue(0x3020, 8)],
                new BackplaneConnectionOptions(0x01, TimeSpan.FromSeconds(1)),
                TestContext.Current.CancellationToken));

        Assert.Contains("0x00000005", exception.Message, StringComparison.Ordinal);
    }

    private sealed class UnusedDiscovery : IUsbBackplaneDiscovery
    {
        public Task<UsbBackplaneDevice?> FindAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<UsbBackplaneDevice?>(null);
    }

    private sealed class FourByteWriteStatusTransport(uint statusCode) : IBackplaneTransport
    {
        public bool IsOpen => true;

        public Task OpenAsync(
            UsbBackplaneDevice device,
            TimeSpan timeout,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<byte[]> ExchangeAsync(
            ReadOnlyMemory<byte> request,
            CancellationToken cancellationToken = default)
        {
            Assert.True(
                TesV14ProtocolCodec.TryParseFrame(request.Span, out var requestFrame, out var error),
                error);
            Assert.NotNull(requestFrame);
            var payload = new byte[4];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(payload, statusCode);
            var response = TesV14ProtocolCodec.BuildFrame(
                TesV14FrameControl.None,
                TesV14Command.Response,
                requestFrame.DestinationAddress,
                requestFrame.SourceAddress,
                27,
                requestFrame.SendSequence,
                payload,
                requestFrame.Version);
            return Task.FromResult(response);
        }

        public Task CloseAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public ValueTask DisposeAsync() =>
            ValueTask.CompletedTask;
    }
}
