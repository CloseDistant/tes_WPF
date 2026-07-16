# RuinaoTesProtocol

This project is an independent protocol API DLL for the tES communication protocol V1.0.

## Current scope

- Build complete protocol frames as `byte[]`.
- Parse received protocol frames.
- Support handshake, ACK, register read/write, reboot, power-off, and stimulation channel start/stop.
- Support provisional 200 ms impedance/temperature acquisition-control frames and realtime report parsing.
- Keep transport independent. The caller can send bytes through serial, USB, TCP, or another hardware communication layer.

## Important hardware-confirmation items

The protocol document is not complete enough for final hardware integration. These items must be confirmed with the hardware side:

- CRC16 polynomial, initial value, final XOR, and byte order. The current code temporarily uses CRC-16/IBM(Modbus), init `0xFFFF`, and writes the CRC bytes high-byte first because the protocol table says CRC is high bit first.
- Exact payload layouts for product model, board model, stimulation parameters, acquisition parameters, impedance, and temperature.
- The current acquisition-control and realtime-report payloads are provisional because protocol V1.0 only says impedance/temperature can be acquired or reported, but does not define payload bytes.
- USB or serial transport details, including port type, baud rate if serial, endpoint format if USB, timeout, and retry policy.
- ACK response status or error-code payload, if any.

## Provisional acquisition payloads

Until hardware returns the final definition, the DLL uses this temporary payload for register `0x0021` acquisition parameters:

```text
target register  ushort  0x0010 impedance or 0x0011 temperature
enable           byte    1=start, 0=stop
periodMs         ushort  default 200
channelMask      uint    bit0=CH1, bit1=CH2 ...; 0 means all channels
```

Realtime response payloads are parsed as:

```text
temperature report register 0x0011:
  repeated 6-byte item: channel, electrode, role, reserved, temperatureCentiCelsius int16

impedance report register 0x0010:
  repeated 6-byte item: channel, anode, cathode, reserved, impedanceOhm uint16
```

## Example

```csharp
using RuinaoTesProtocol;

var api = new TesProtocolApi();

byte[] handshake = api.BuildHandshake();
byte[] readTemperature = api.BuildReadRegister(TesRegister.Temperature);
byte[] startTemperature = api.BuildStartTemperatureAcquisition(periodMs: 200);
byte[] startImpedance = api.BuildStartImpedanceAcquisition(periodMs: 200);
byte[] startChannel1 = api.BuildStartStimChannel(1);
byte[] stopChannel1 = api.BuildStopStimChannel(1);

if (api.TryParseFrame(receivedBytes, out var frame, out var error))
{
    // Use frame.Command, frame.SourceAddress, frame.Payload, etc.
    if (api.TryParseRealtimeReport(frame, out var report, out var reportError))
    {
        // Use report.Temperatures or report.Impedances.
    }
}
```
