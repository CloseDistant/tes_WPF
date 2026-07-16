namespace RuinaoTesProtocol;

/// <summary>
/// Temporary acquisition-control payload for register 0x0021.
/// Protocol V1.0 has not defined the payload yet.
///
/// Current provisional registerData layout:
/// byte[0..1]  target register: 0x0010 impedance or 0x0011 temperature, little-endian
/// byte[2]     enable: 1=start, 0=stop
/// byte[3..4]  report period in milliseconds, little-endian. Default is 200 ms.
/// byte[5..8]  channel mask, little-endian. bit0=CH1, bit1=CH2 ...; 0 means all channels.
/// </summary>
public sealed record TesAcquisitionControl(
    TesAcquisitionKind Kind,
    bool Enabled,
    ushort PeriodMs = 200,
    uint ChannelMask = 0);
