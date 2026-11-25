# DeskPal 插件模板（Windows，用系统自带的 Windows PowerShell 5.1，不用装别的）。
# 提供“当前窗口”和“暂离”：读前台窗口的标题和进程，以及距上次键盘鼠标输入的时间。
# 用法：复制到 %APPDATA%\DeskPal\plugins\，在 DeskPal「设置 → 插件」里点“刷新”并开启它，放在 Windows 内置插件上面。
# 想改成别的数据来源，只改 Read-Window 和 Read-IdleSeconds。
# 只往标准输出写协议行；调试信息用 [Console]::Error.WriteLine(...)，会进 DeskPal 的日志。

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)

Add-Type -Namespace DeskPalPlugin -Name Win32 -MemberDefinition @'
[DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
[DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int count);
[DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
[StructLayout(LayoutKind.Sequential)] public struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }
[DllImport("user32.dll")] public static extern bool GetLastInputInfo(ref LASTINPUTINFO info);
'@

function Emit($message) {
    [Console]::Out.WriteLine(($message | ConvertTo-Json -Compress))
    [Console]::Out.Flush()
}

# 返回 @{ process; name; title; path }，没有前台窗口时返回 $null。
function Read-Window {
    $hwnd = [DeskPalPlugin.Win32]::GetForegroundWindow()
    if ($hwnd -eq [IntPtr]::Zero) { return $null }
    $text = New-Object System.Text.StringBuilder 512
    [void][DeskPalPlugin.Win32]::GetWindowText($hwnd, $text, $text.Capacity)
    $procId = [uint32]0
    [void][DeskPalPlugin.Win32]::GetWindowThreadProcessId($hwnd, [ref]$procId)
    $process = Get-Process -Id $procId -ErrorAction SilentlyContinue
    if ($null -eq $process -or $procId -eq $PID) { return $null }
    $path = $null
    $name = $process.ProcessName
    try {
        $path = $process.Path
        if ($process.MainModule.FileVersionInfo.FileDescription) { $name = $process.MainModule.FileVersionInfo.FileDescription }
    } catch {
        # 以管理员身份运行的程序读不到路径，只用进程名。
    }
    return @{ process = $process.ProcessName; name = $name; title = $text.ToString(); path = $path }
}

function Read-IdleSeconds {
    $info = New-Object DeskPalPlugin.Win32+LASTINPUTINFO
    $info.cbSize = [System.Runtime.InteropServices.Marshal]::SizeOf($info)
    if (-not [DeskPalPlugin.Win32]::GetLastInputInfo([ref]$info)) { return $null }
    $now = [uint32]([Environment]::TickCount -band 0xFFFFFFFF)
    return [math]::Round((([int64]$now - [int64]$info.dwTime + 4294967296) % 4294967296) / 1000, 1)
}

Emit @{ v = 1; type = 'hello'; name = 'windows-template'; version = '0.1'; provides = @('foreground', 'idle') }

$last = $null
$tick = 0
while ($true) {
    $window = Read-Window
    if ($null -ne $window) {
        $line = @{ v = 1; type = 'foreground'; process = $window.process; name = $window.name; title = $window.title; path = $window.path; fullscreen = $false }
        $key = "$($window.process)|$($window.title)"
        if ($key -ne $last) {
            # 窗口没变化就不输出。
            Emit $line
            $last = $key
        }
    }

    # 暂离：每 5 秒报一次距上次输入的秒数，是否算暂离由 DeskPal 按用户的设置判断。
    if ($tick % 5 -eq 0) {
        $idle = Read-IdleSeconds
        if ($null -ne $idle) { Emit @{ v = 1; type = 'idle'; seconds = $idle } }
    }

    $tick++
    Start-Sleep -Seconds 1
}
