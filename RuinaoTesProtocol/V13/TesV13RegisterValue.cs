namespace RuinaoTesProtocol.V13;

/// <summary>
/// V1.3 普通寄存器项：2 字节地址和 4 字节内容。
/// </summary>
public readonly record struct TesV13RegisterValue(ushort Address, uint Value);
