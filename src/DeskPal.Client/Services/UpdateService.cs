using System.Reflection;
using DeskPal.Client.Core.Updates;
using Velopack;
using Velopack.Sources;

namespace DeskPal.Client.Services;

public enum UpdateCheckKind
{
    UpToDate,
    Ready,
    DowngradeAvailable,
    NotInstalled,
    Failed,
}

public sealed record UpdateCheckResult(UpdateCheckKind Kind, string Message, string? Version);

public sealed class UpdateService
{
    public const string NotInstalledMessage =
        "当前是直接运行的程序，不能自动更新。请从 GitHub Release 安装带版本号的安装包（DeskPal-版本号-Setup.exe）。";

    private readonly AppSettings _settings;
    private readonly SettingsStore _store;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _state = new();
    private readonly bool _installed;
    private readonly bool _installedPrerelease;
    private readonly InstalledUpdateManager? _locator;
    private int _channelEpoch;
    private InstalledUpdateManager? _manager;
    private VelopackAsset? _ready;
    private bool _readyIsDowngrade;
    private string? _activity;
    private int _interactive;

    public UpdateService(AppSettings settings, SettingsStore store)
    {
        _settings = settings;
        _store = store;
        try
        {
            _locator = new InstalledUpdateManager(new GithubSource(UpdatePolicy.RepositoryUrl, null, false));
            _installed = _locator.IsInstalled;
            _installedPrerelease = _locator.CurrentVersion?.IsPrerelease == true;
            VersionLabel = _locator.CurrentVersion?.ToString() ?? AssemblyVersion();
        }
        catch (Exception)
        {
            _installed = false;
            VersionLabel = AssemblyVersion();
        }
    }

    public string VersionLabel { get; }

    public bool UpdateReady
    {
        get
        {
            lock (_state)
            {
                return _ready is not null && UpdatePolicy.ApplyWithoutAsking(_readyIsDowngrade);
            }
        }
    }

    public event Action? Changed;

    public string? ActivityText
    {
        get
        {
            lock (_state)
            {
                return _activity;
            }
        }
    }

    /// <summary>由 App 赋值。参数为 true 时重启并安装已下载的更新。</summary>
    public Func<bool, Task>? Exit { get; set; }

    public bool ReceiveBeta
    {
        get
        {
            lock (_state)
            {
                return UpdatePolicy.ReceiveBeta(_settings.IncludePrerelease, _installedPrerelease);
            }
        }
        set
        {
            var changed = false;
            lock (_state)
            {
                if (_settings.IncludePrerelease == value)
                {
                    return;
                }

                _settings.IncludePrerelease = value;
                _store.Save(_settings);
                _channelEpoch++;
                _ready = null;
                _readyIsDowngrade = false;
                changed = true;
            }

            if (!changed)
            {
                return;
            }

            // 已下载的测试版包留在本地时，下次启动不能把它装上。
            DeleteDownloadedPackages();
            Changed?.Invoke();
        }
    }

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default, bool interactive = false)
    {
        if (interactive)
        {
            BeginInteractive("正在检查更新…");
        }

        try
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
            if (!_installed || _locator is null)
            {
                return new UpdateCheckResult(UpdateCheckKind.NotInstalled, NotInstalledMessage, VersionLabel);
            }

            int epoch;
            bool beta;
            lock (_state)
            {
                epoch = _channelEpoch;
                beta = UpdatePolicy.ReceiveBeta(_settings.IncludePrerelease, _installedPrerelease);
            }

            var manager = CreateManager(beta);
            UpdateInfo? info;
            try
            {
                info = await manager.CheckForUpdatesAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                return new UpdateCheckResult(UpdateCheckKind.Failed, $"检查更新失败：{ex.Message}", VersionLabel);
            }

            if (info is null)
            {
                ClearReady();
                return new UpdateCheckResult(UpdateCheckKind.UpToDate, $"当前已是最新版本 {VersionLabel}。", VersionLabel);
            }

            var target = info.TargetFullRelease.Version.ToString();
            if (info.IsDowngrade)
            {
                ClearReady();
                return new UpdateCheckResult(
                    UpdateCheckKind.DowngradeAvailable,
                    $"正式版 {target} 低于当前测试版 {VersionLabel}。要切换到正式版并重启吗？不切换会继续使用当前版本，直到下一个更高的正式版。",
                    target);
            }

            try
            {
                if (interactive)
                {
                    ReportDownload(target, null);
                }

                await manager.DownloadUpdatesAsync(
                    info,
                    interactive ? percent => ReportDownload(target, percent) : null,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ForgetReadyIfPackageMissing();
                if (cancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException(cancellationToken);
                }

                return new UpdateCheckResult(UpdateCheckKind.Failed, $"下载更新失败：{ex.Message}", target);
            }

            if (!CommitReady(epoch, manager, info.TargetFullRelease, downgrade: false))
            {
                DeleteDownloadedPackages();
                return new UpdateCheckResult(UpdateCheckKind.UpToDate, "更新通道已改变，这次下载已放弃。", VersionLabel);
            }

            Changed?.Invoke();
            return new UpdateCheckResult(UpdateCheckKind.Ready, $"新版本 {target} 已下载，退出后自动安装。", target);
            }
            finally
            {
                _gate.Release();
            }
        }
        finally
        {
            if (interactive)
            {
                EndInteractive();
            }
        }
    }

    public async Task<string?> DownloadDowngradeAsync()
    {
        BeginInteractive("正在下载正式版…");
        try
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
            if (!_installed)
            {
                return NotInstalledMessage;
            }

            int epoch;
            lock (_state)
            {
                epoch = _channelEpoch;
            }

            var manager = CreateManager(beta: false);
            var info = await manager.CheckForUpdatesAsync().ConfigureAwait(false);
            if (info is not { IsDowngrade: true })
            {
                return "正式版没有变化。";
            }

            var target = info.TargetFullRelease.Version.ToString();
            ReportDownload(target, null);
            await manager.DownloadUpdatesAsync(info, percent => ReportDownload(target, percent)).ConfigureAwait(false);
            if (!CommitReady(epoch, manager, info.TargetFullRelease, downgrade: true))
            {
                DeleteDownloadedPackages();
                return "更新通道已改变，这次下载已放弃。";
            }

            Changed?.Invoke();
            return null;
        }
        catch (Exception ex)
        {
            ForgetReadyIfPackageMissing();
            return $"下载正式版失败：{ex.Message}";
            }
            finally
            {
                _gate.Release();
            }
        }
        finally
        {
            EndInteractive();
        }
    }

    private void BeginInteractive(string text)
    {
        var notify = false;
        lock (_state)
        {
            _interactive++;
            if (_activity != text)
            {
                _activity = text;
                notify = true;
            }
        }

        if (!notify)
        {
            return;
        }

        Changed?.Invoke();
    }

    private void EndInteractive()
    {
        var changed = false;
        lock (_state)
        {
            if (_interactive > 0)
            {
                _interactive--;
            }

            if (_interactive == 0 && _activity is not null)
            {
                _activity = null;
                changed = true;
            }
        }

        if (changed)
        {
            Changed?.Invoke();
        }
    }

    /// <summary>面板显示带百分比的句子。</summary>
    private void ReportDownload(string version, int? percent)
    {
        var text = percent is null
            ? $"正在下载新版本 {version}…"
            : $"正在下载新版本 {version}（{percent}%）";
        lock (_state)
        {
            if (_interactive == 0 || _activity == text)
            {
                return;
            }

            _activity = text;
        }

        Changed?.Invoke();
    }

    public async Task RunPeriodicAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
            while (!cancellationToken.IsCancellationRequested)
            {
                await CheckAsync(cancellationToken).ConfigureAwait(false);
                await Task.Delay(TimeSpan.FromHours(6), cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>调用前要先停掉 Hub 并释放单实例 Mutex。安装成功时这个方法会结束当前进程。</summary>
    public bool ApplyReady(bool restart, bool includeDowngrade)
    {
        InstalledUpdateManager? manager;
        VelopackAsset? asset;
        bool downgrade;
        lock (_state)
        {
            manager = _manager;
            asset = _ready;
            downgrade = _readyIsDowngrade;
        }

        if (manager is null || asset is null || (downgrade && !includeDowngrade) || !PackageExists(manager, asset))
        {
            return false;
        }

        if (restart)
        {
            manager.ApplyUpdatesAndRestart(asset);
        }
        else
        {
            manager.ApplyUpdatesAndExit(asset);
        }

        return true;
    }

    private bool CommitReady(int epoch, InstalledUpdateManager manager, VelopackAsset asset, bool downgrade)
    {
        lock (_state)
        {
            if (epoch != _channelEpoch)
            {
                return false;
            }

            _manager = manager;
            _ready = asset;
            _readyIsDowngrade = downgrade;
            return true;
        }
    }

    private void ClearReady()
    {
        var changed = false;
        lock (_state)
        {
            if (_ready is null)
            {
                return;
            }

            _ready = null;
            _readyIsDowngrade = false;
            changed = true;
        }

        if (changed)
        {
            Changed?.Invoke();
        }
    }

    private void ForgetReadyIfPackageMissing()
    {
        var changed = false;
        lock (_state)
        {
            if (_ready is null)
            {
                return;
            }

            var manager = _manager ?? _locator;
            if (manager is not null && PackageExists(manager, _ready))
            {
                return;
            }

            _ready = null;
            _readyIsDowngrade = false;
            changed = true;
        }

        if (changed)
        {
            Changed?.Invoke();
        }
    }

    private void DeleteDownloadedPackages()
    {
        if (!_installed)
        {
            return;
        }

        var directory = (_manager ?? _locator)?.PackagesDirectory;
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            return;
        }

        foreach (var path in Directory.EnumerateFiles(directory))
        {
            if (!path.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase) && !path.EndsWith(".partial", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static bool PackageExists(InstalledUpdateManager manager, VelopackAsset asset)
    {
        var directory = manager.PackagesDirectory;
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(asset.FileName) && File.Exists(Path.Combine(directory, asset.FileName)))
        {
            return true;
        }

        var version = asset.Version.ToString();
        return Directory.EnumerateFiles(directory, "*.nupkg").Any(path => Path.GetFileName(path).Contains(version, StringComparison.OrdinalIgnoreCase));
    }

    private static InstalledUpdateManager CreateManager(bool beta) => new(
        new GithubSource(UpdatePolicy.RepositoryUrl, null, beta),
        new UpdateOptions
        {
            ExplicitChannel = UpdatePolicy.Channel(beta),
            AllowVersionDowngrade = !beta,
        });

    private static string AssemblyVersion()
    {
        var version = typeof(UpdateService).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrWhiteSpace(version))
        {
            return "0.0.0";
        }

        var plus = version.IndexOf('+');
        return plus >= 0 ? version[..plus] : version;
    }

    private sealed class InstalledUpdateManager : UpdateManager
    {
        public InstalledUpdateManager(IUpdateSource source)
            : base(source)
        {
        }

        public InstalledUpdateManager(IUpdateSource source, UpdateOptions options)
            : base(source, options)
        {
        }

        public string? PackagesDirectory => Locator.PackagesDir;
    }
}
