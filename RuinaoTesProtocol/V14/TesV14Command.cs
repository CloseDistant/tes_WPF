namespace RuinaoTesProtocol.V14;

/// <summary>tES通信协议V1.4命令码。</summary>
public enum TesV14Command : byte
{
    Handshake = 0x00,
    Acknowledgement = 0x01,
    Read = 0x02,
    Write = 0x03,
    Response = 0x04,
}
