namespace RuinaoSoftwareWpf;

/// <summary>隔离tACS页面与共享硬件DLL计划类型的波形预览边界。</summary>
public interface ITacsWaveformPreviewFactory
{
    AlternatingCurrentWaveformPreview Create(TacsParameters parameters);
}
