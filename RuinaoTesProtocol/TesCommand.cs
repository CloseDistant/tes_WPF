namespace RuinaoTesProtocol;

/// <summary>
/// 协议命令码。
/// </summary>
public enum TesCommand : byte
{
    Handshake = 0x00,
    Ack = 0x01,
    Request = 0x02,
    Response = 0x03,
}
