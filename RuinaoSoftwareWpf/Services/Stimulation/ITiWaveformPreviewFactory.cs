namespace RuinaoSoftwareWpf;

/// <summary>隔离正式页面与共享硬件DLL计划类型的TI波形预览边界。</summary>
public interface ITiWaveformPreviewFactory
{
    AlternatingCurrentWaveformPreview Create(TiAlternatingCurrentParameters parameters);
}
