namespace RuinaoTesProtocol.V14;

/// <summary>V1.4普通寄存器项：2字节地址和4字节内容，均按大端序传输。</summary>
public readonly record struct TesV14RegisterValue(ushort Address, uint Value);
