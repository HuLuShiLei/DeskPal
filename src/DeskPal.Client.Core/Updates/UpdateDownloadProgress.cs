namespace DeskPal.Client.Core.Updates;

/// <summary>
/// Velopack 增量更新时，下载增量包报 0 到 70，随后由 Update.exe 合成完整安装包，这一段不报进度，合成完直接报 100。
/// 完整下载报 0 到 100。增量失败时 Velopack 改为下载完整安装包，进度从头开始。
/// </summary>
public sealed class UpdateDownloadProgress
{
    public const int DeltaDownloadEnd = 70;

    /// <summary>与 Velopack <c>UpdateOptions.MaximumDeltasBeforeFallback</c> 的默认值一致。</summary>
    public const int MaximumDeltas = 10;

    private bool _delta;
    private int _last;

    public UpdateDownloadProgress(bool delta) => _delta = delta;

    /// <summary>Velopack 先尝试增量更新的条件，不满足时直接下载完整安装包。</summary>
    public static bool UsesDeltas(bool hasBasePackage, int deltaCount, long deltaBytes, long fullBytes) =>
        hasBasePackage && deltaCount is > 0 and <= MaximumDeltas && deltaBytes <= fullBytes;

    /// <summary>返回要显示的下载百分比；null 表示下载已完成，正在合成安装包。</summary>
    public int? Map(int percent)
    {
        if (percent < _last)
        {
            _delta = false;
        }

        _last = percent;
        if (!_delta)
        {
            return percent;
        }

        return percent < DeltaDownloadEnd ? percent * 100 / DeltaDownloadEnd : null;
    }
}