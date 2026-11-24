# DeskPal 插件样例（Windows，系统自带的 Windows PowerShell 5.1）：Steam 游戏内状态。
# 只提供「补充标题」（detail）：Steam 游戏在前台时，把窗口标题换成你在 Steam 好友列表里显示的游戏内状态
# （Rich Presence），例如「故障机器人 - 巢穴 进阶3」「机器人：宙斯（29级）」。文字的语言跟随 Steam 客户端。
# 当前窗口、网站、暂离等仍由 Windows 内置插件提供，这个插件放在列表里任何位置都可以。
# 用法：复制到 %APPDATA%\DeskPal\plugins\，在 DeskPal「设置 → 插件」里点“刷新”并开启它。
#
# 怎么读到的：Steam 客户端的界面是内嵌的 Chromium（steamwebhelper）。Steam 目录里有空文件
# .cef-enable-remote-debugging 时，Steam 启动后在 127.0.0.1:8080 开放 Chrome 调试接口。插件连到界面里的
# SharedJSContext 页面，只读你自己的「正在玩的 AppID」和翻译好的游戏内状态，不读好友、聊天和账号信息。
# 没有这个文件时插件会创建它，然后提示重启 Steam。
# 注意：调试接口打开后，本机任何程序都能通过它操作 Steam 界面（包括已登录的商店和社区页面）。
# 不想要了：删掉 Steam 目录里的 .cef-enable-remote-debugging，重启 Steam。
#
# 游戏进程：按 Steam 库清单（appmanifest_<AppID>.acf 的 installdir）找到游戏目录，前台程序在这个目录里就算这个游戏。
# 已在 Steam 客户端（2026-11）、Slay the Spire 2 和 Dota 2 上验证。Steam 改版后读不到时什么都不报，
# DeskPal 显示原来的窗口标题。
#
# 文件必须存成带 BOM 的 UTF-8，否则 Windows PowerShell 5.1 读错中文。

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)

Add-Type -Namespace DeskPalPlugin -Name SteamWin32 -MemberDefinition @"
[DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
[DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
"@

$DebugUrl = 'http://127.0.0.1:8080'
$PluginName = 'Steam 游戏状态'
# 只读自己的状态。current_game_rich_presence 是 Steam 好友列表显示的、已经翻译好的文字。
$Expression = @"
(() => { try {
  const self = g_FriendsUIApp.FriendStore.self;
  return JSON.stringify({ appid: self.m_persona.m_unGamePlayedAppID || 0, status: self.current_game_rich_presence || '' });
} catch (e) { return JSON.stringify({ error: String(e) }); } })()
"@

function Emit($message) {
    [Console]::Out.WriteLine(($message | ConvertTo-Json -Compress))
    [Console]::Out.Flush()
}

function Stop-Plugin($text) {
    # 插件退出时，最后一行错误显示在 DeskPal 的插件列表里；DeskPal 稍后会再启动插件。
    [Console]::Error.WriteLine($text)
    exit 1
}

function Get-SteamRoot {
    $key = Get-ItemProperty 'HKCU:\Software\Valve\Steam' -ErrorAction SilentlyContinue
    if ($null -eq $key -or -not $key.SteamPath) { return $null }
    return [System.IO.Path]::GetFullPath($key.SteamPath)
}

function Get-Text($url) {
    $client = New-Object System.Net.WebClient
    $client.Proxy = $null
    $client.Encoding = [System.Text.Encoding]::UTF8
    try { return $client.DownloadString($url) } finally { $client.Dispose() }
}

# 在 SharedJSContext 里执行一次表达式，返回字符串结果。连不上时抛出异常。
function Invoke-SteamScript($expression) {
    if (-not $script:SocketUrl) {
        $targets = (Get-Text "$DebugUrl/json") | ConvertFrom-Json
        $target = @($targets | Where-Object { $_.title -eq 'SharedJSContext' }) | Select-Object -First 1
        if ($null -eq $target) { throw 'SharedJSContext not found' }
        $script:SocketUrl = $target.webSocketDebuggerUrl
    }

    $socket = New-Object System.Net.WebSockets.ClientWebSocket
    $timeout = New-Object System.Threading.CancellationTokenSource 3000
    try {
        [void]$socket.ConnectAsync([Uri]$script:SocketUrl, $timeout.Token).GetAwaiter().GetResult()
        $request = @{ id = 1; method = 'Runtime.evaluate'; params = @{ expression = $expression; returnByValue = $true } } | ConvertTo-Json -Compress
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($request)
        [void]$socket.SendAsync((New-Object System.ArraySegment[byte] -ArgumentList @(, $bytes)),
            [System.Net.WebSockets.WebSocketMessageType]::Text, $true, $timeout.Token).GetAwaiter().GetResult()

        $buffer = New-Object byte[] 65536
        while ($true) {
            $stream = New-Object System.IO.MemoryStream
            do {
                $result = $socket.ReceiveAsync((New-Object System.ArraySegment[byte] -ArgumentList @(, $buffer)), $timeout.Token).GetAwaiter().GetResult()
                if ($result.MessageType -eq [System.Net.WebSockets.WebSocketMessageType]::Close) { throw 'closed by Steam' }
                $stream.Write($buffer, 0, $result.Count)
            } while (-not $result.EndOfMessage)
            $reply = [System.Text.Encoding]::UTF8.GetString($stream.ToArray()) | ConvertFrom-Json
            if ($reply.id -eq 1) {
                if ($null -ne $reply.result.exceptionDetails) { throw "script failed: $($reply.result.exceptionDetails.text)" }
                return [string]$reply.result.result.value
            }
        }
    } catch {
        # 页面换了（Steam 重启、界面重载）就重新找一次。
        $script:SocketUrl = $null
        throw
    } finally {
        $socket.Dispose()
        $timeout.Dispose()
    }
}

# 你自己正在玩的 AppID 和游戏内状态；连不上 Steam 时返回 $null。
function Read-SteamStatus {
    try {
        $state = (Invoke-SteamScript $Expression) | ConvertFrom-Json
    } catch {
        return $null
    }
    if ($null -ne $state.error) {
        if ($script:LastScriptError -ne $state.error) {
            [Console]::Error.WriteLine("reading the Steam status failed: $($state.error)")
            $script:LastScriptError = $state.error
        }
        return @{ appid = 0; status = '' }
    }
    return @{ appid = [int64]$state.appid; status = ([string]$state.status).Trim() }
}

# 游戏的安装目录（带结尾的 \），找不到时返回 $null。按 AppID 缓存。
function Get-GameFolder($appid) {
    if ($script:FolderCache.ContainsKey($appid)) { return $script:FolderCache[$appid] }
    $folder = $null
    $root = Get-SteamRoot
    if ($root) {
        $libraries = @($root)
        $vdf = Join-Path $root 'steamapps\libraryfolders.vdf'
        if (Test-Path -LiteralPath $vdf) {
            foreach ($m in [regex]::Matches([System.IO.File]::ReadAllText($vdf), '"path"\s+"([^"]+)"')) {
                $libraries += $m.Groups[1].Value.Replace('\\', '\')
            }
        }
        foreach ($library in ($libraries | Select-Object -Unique)) {
            $manifest = Join-Path $library "steamapps\appmanifest_$appid.acf"
            if (-not (Test-Path -LiteralPath $manifest)) { continue }
            $m = [regex]::Match([System.IO.File]::ReadAllText($manifest), '"installdir"\s+"([^"]+)"')
            if ($m.Success) {
                $folder = [System.IO.Path]::GetFullPath((Join-Path $library "steamapps\common\$($m.Groups[1].Value)")).TrimEnd('\') + '\'
                break
            }
        }
    }
    $script:FolderCache[$appid] = $folder
    return $folder
}

# 前台窗口所属的程序：@{ name; path }，读不到时返回 $null。
function Read-Foreground {
    $hwnd = [DeskPalPlugin.SteamWin32]::GetForegroundWindow()
    if ($hwnd -eq [IntPtr]::Zero) { return $null }
    $procId = [uint32]0
    [void][DeskPalPlugin.SteamWin32]::GetWindowThreadProcessId($hwnd, [ref]$procId)
    $owner = Get-Process -Id $procId -ErrorAction SilentlyContinue
    if ($null -eq $owner) { return $null }
    try { $path = $owner.Path } catch { $path = $null }
    if (-not $path) { return $null }
    return @{ name = $owner.ProcessName; path = $path }
}

# 连不上调试接口时，判断是不是要提示用户。
function Test-DebugPort {
    $steam = @(Get-Process steam -ErrorAction SilentlyContinue) | Select-Object -First 1
    if ($null -eq $steam) { $script:Failures = 0; return }
    $root = Get-SteamRoot
    if (-not $root) { return }
    $flag = Join-Path $root '.cef-enable-remote-debugging'
    if (-not (Test-Path -LiteralPath $flag)) {
        try {
            [System.IO.File]::WriteAllText($flag, '')
        } catch {
            Stop-Plugin "Steam 没有开启调试接口，也无法自动开启：请在 $root 里新建空文件 .cef-enable-remote-debugging，然后重启 Steam。"
        }
        Stop-Plugin '已开启 Steam 调试接口，完全退出并重新打开 Steam 后生效。'
    }
    try { $started = $steam.StartTime } catch { $started = $null }
    if ($null -ne $started -and $started -lt (Get-Item -LiteralPath $flag).LastWriteTime) {
        Stop-Plugin 'Steam 调试接口要在重启 Steam 后生效：请完全退出并重新打开 Steam。'
    }
    # Steam 刚启动时界面还没加载完，等半分钟再报错。
    if (++$script:Failures -ge 15) {
        Stop-Plugin "连不上 Steam 调试接口 $DebugUrl。端口 8080 可能被其他程序占用。"
    }
}

$script:FolderCache = @{}
$script:SocketUrl = $null
$script:LastScriptError = $null
$script:Failures = 0

$apps = New-Object 'System.Collections.Generic.List[string]'
$appid = 0
$last = $null

function Send-Hello {
    Emit @{ v = 1; type = 'hello'; name = $PluginName; version = '0.1'; provides = @('detail'); apps = @($apps) }
}

Send-Hello
# 没开游戏时也说明插件在正常运行。收回一个不存在的标题，DeskPal 不会有任何变化。
Emit @{ v = 1; type = 'detail'; process = 'steam'; title = $null }

while ($true) {
    $state = Read-SteamStatus
    if ($null -eq $state) {
        Test-DebugPort
        $state = @{ appid = 0; status = '' }
    } else {
        $script:Failures = 0
    }

    if ($state.appid -ne $appid) {
        # 换了游戏或退出了游戏：收回上一个游戏的标题，清空程序列表。
        if ($null -ne $last) {
            Emit @{ v = 1; type = 'detail'; process = $last.process; title = $null }
            $last = $null
        }
        if ($apps.Count -gt 0) {
            $apps.Clear()
            Send-Hello
        }
        $appid = $state.appid
    }

    if ($appid -gt 0) {
        $folder = Get-GameFolder $appid
        $foreground = Read-Foreground
        if ($null -ne $folder -and $null -ne $foreground -and $foreground.path.StartsWith($folder, [StringComparison]::OrdinalIgnoreCase)) {
            # 同一个游戏可能有启动器和本体几个程序，都记下来；DeskPal 只用前台那个。
            if (-not ($apps -contains $foreground.name) -and $apps.Count -lt 32) {
                $apps.Add($foreground.name)
                Send-Hello
            }
            $title = if ($state.status.Length -gt 0) { $state.status } else { $null }
            if ($null -eq $last -or $last.process -ne $foreground.name -or $last.title -ne $title) {
                Emit @{ v = 1; type = 'detail'; process = $foreground.name; title = $title }
                $last = @{ process = $foreground.name; title = $title }
            }
        }
    }

    Start-Sleep -Seconds 2
}