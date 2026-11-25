# DeskPal 插件（Windows，系统自带的 Windows PowerShell 5.1）：微信当前会话。
# 只提供「补充标题」（detail）：微信在前台时，把窗口标题换成正在看的会话名（好友或群的名称）。
# 当前窗口、网站、暂离等仍由 Windows 内置插件提供，这个插件放在列表里任何位置都可以。
# 用法：复制到 %APPDATA%\DeskPal\plugins\，在 DeskPal「设置 → 插件」里点“刷新”并开启它。
#
# 怎么读到的：微信 4.x（进程名 Weixin）的界面用 Qt 写成，会向 UI Automation（系统的无障碍接口）公开界面结构。
# 会话顶部的名称是 AutomationId 以 current_chat_name_label 结尾的文本元素，只读这一个元素的名称，
# 不读消息内容（群聊名后面的人数是另一个元素，也不读）。不注入、不读微信的数据文件。
# 微信改版后界面结构可能变化，读不到时这个插件什么都不报，DeskPal 显示原来的窗口标题。
# 已在微信 4.2.0.53（2026-11）的主窗口上验证；单独弹出的聊天窗口未验证。旧版微信 3.x（进程名 WeChat）界面不同，不支持。
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

$Process = 'Weixin'
$NameLabelId = 'content_view.top_content_view.title_h_view.left_v_view.left_content_v_view.left_ui_.big_title_line_h_view.current_chat_name_label'
$nameLabel = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::AutomationIdProperty, $NameLabelId)

# 微信界面里有整个消息列表，每秒在整棵树里找一遍代价不小：找到后记住这个元素，失效了再找；找不到时最多 5 秒找一次。
$script:cachedWindow = [IntPtr]::Zero
$script:cachedLabel = $null
$script:lastSearch = [DateTime]::MinValue

function Emit($message) {
    [Console]::Out.WriteLine(($message | ConvertTo-Json -Compress))
    [Console]::Out.Flush()
}

# 前台是微信的窗口时返回 @{ hwnd; title }，否则返回 $null。
function Read-WeChatWindow {
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

function Find-NameLabel($hwnd) {
    if ($script:cachedWindow -ne $hwnd) {
        $script:cachedWindow = $hwnd
        $script:cachedLabel = $null
        $script:lastSearch = [DateTime]::MinValue
    }
    if ($null -ne $script:cachedLabel) { return $script:cachedLabel }
    if (([DateTime]::UtcNow - $script:lastSearch).TotalSeconds -lt 5) { return $null }
    $script:lastSearch = [DateTime]::UtcNow
    $root = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)
    $script:cachedLabel = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $nameLabel)
    return $script:cachedLabel
}

# 当前会话的名称；不在聊天界面（例如通讯录、收藏）、没有打开会话或读不到时返回 $null。
function Read-ChatName($hwnd) {
    try {
        $label = Find-NameLabel $hwnd
        if ($null -eq $label) { return $null }
        $current = $label.Current
        if ($current.IsOffscreen) { return $null }
        $name = $current.Name.Trim()
        if ($name.Length -eq 0) { return $null }
        return $name
    } catch [System.Windows.Automation.ElementNotAvailableException] {
        # 会话页被重建了，下次重新找。
        $script:cachedLabel = $null
        $script:lastSearch = [DateTime]::MinValue
        return $null
    } catch {
        [Console]::Error.WriteLine("reading the chat header failed: $($_.Exception.Message)")
        $script:cachedLabel = $null
        return $null
    }
}

Emit @{ v = 1; type = 'hello'; name = '微信会话'; version = '0.1'; provides = @('detail'); apps = @($Process) }

$last = $null
while ($true) {
    $window = Read-WeChatWindow
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
