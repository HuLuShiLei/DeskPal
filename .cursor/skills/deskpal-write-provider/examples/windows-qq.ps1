# DeskPal 插件样例（Windows，系统自带的 Windows PowerShell 5.1）：QQ 当前会话。
# 只提供「补充标题」（detail）：QQ 在前台时，把窗口标题换成正在看的会话名（好友或群的名称）。
# 当前窗口、网站、暂离等仍由 Windows 内置插件提供，这个插件放在列表里任何位置都可以。
# 用法：复制到 %APPDATA%\DeskPal\plugins\，在 DeskPal「设置 → 插件」里点“刷新”并开启它。
#
# 怎么读到的：QQ NT 是 Electron 程序，会向 UI Automation（系统的无障碍接口）公开界面结构。
# 会话顶部的名称是 class 为 chat-header__contact-name 的元素，只读这一个元素的名称，不读消息内容。
# 不注入、不读 QQ 的数据文件。QQ 改版后 class 可能变化，读不到时这个插件什么都不报，DeskPal 显示原来的窗口标题。
# 已在 QQ NT 9.9.37（2026-11）上验证主窗口；单独弹出的聊天窗口用同样的结构，未逐一验证。
#
# 文件必须存成带 BOM 的 UTF-8，否则 Windows PowerShell 5.1 读错中文。

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type -Namespace DeskPalPlugin -Name Win32 -MemberDefinition @"
[DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
[DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int count);
[DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
"@

$Process = 'QQ'
$HeaderClass = 'chat-header__contact-name'
$header = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ClassNameProperty, $HeaderClass)

function Emit($message) {
    [Console]::Out.WriteLine(($message | ConvertTo-Json -Compress))
    [Console]::Out.Flush()
}

# 前台是 QQ 的窗口时返回 @{ hwnd; title }，否则返回 $null。
function Read-QQWindow {
    $hwnd = [DeskPalPlugin.Win32]::GetForegroundWindow()
    if ($hwnd -eq [IntPtr]::Zero) { return $null }
    $procId = [uint32]0
    [void][DeskPalPlugin.Win32]::GetWindowThreadProcessId($hwnd, [ref]$procId)
    $owner = Get-Process -Id $procId -ErrorAction SilentlyContinue
    if ($null -eq $owner -or $owner.ProcessName -ne $Process) { return $null }
    $text = New-Object System.Text.StringBuilder 512
    [void][DeskPalPlugin.Win32]::GetWindowText($hwnd, $text, $text.Capacity)
    return @{ hwnd = $hwnd; title = $text.ToString().Trim() }
}

# 当前会话的名称；不在聊天界面（例如联系人、空间）或读不到时返回 $null。
function Read-ChatName($hwnd) {
    try {
        $root = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)
        $element = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $header)
        if ($null -eq $element) { return $null }
        $name = $element.Current.Name.Trim()
        if ($name.Length -eq 0) { return $null }
        return $name
    } catch {
        [Console]::Error.WriteLine("reading the chat header failed: $($_.Exception.Message)")
        return $null
    }
}

Emit @{ v = 1; type = 'hello'; name = 'QQ 会话'; version = '0.1'; provides = @('detail'); apps = @($Process) }

$last = $null
while ($true) {
    $window = Read-QQWindow
    if ($null -ne $window) {
        $chat = Read-ChatName $window.hwnd
        # window 写上看到的窗口标题，DeskPal 只把会话名用在这个窗口上；没变化就不输出。
        $key = "$($window.title)|$chat"
        if ($key -ne $last) {
            Emit @{ v = 1; type = 'detail'; process = $Process; window = $window.title; title = $chat }
            $last = $key
        }
    }

    Start-Sleep -Seconds 1
}