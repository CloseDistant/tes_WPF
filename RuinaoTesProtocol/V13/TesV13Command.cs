namespace RuinaoTesProtocol.V13;

public enum TesV13Command : byte
{
    Handshake = 0x00,
    Acknowledgement = 0x01,
    Read = 0x02,
    Write = 0x03,
    Response = 0x04,
}
