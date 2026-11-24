# DeskPal 探测脚本（Windows）：查看 QQ、微信等程序向 UI Automation（系统的无障碍接口）公开了什么界面结构。
#
# 只输出元素的类型、类名、AutomationId 和位置；名称一律只输出首字和长度（例如「张…(2)」），
# 不输出聊天内容、联系人或群名。不改动任何设置，不联网。
#
# 用法（PowerShell）：
#   powershell -ExecutionPolicy Bypass -File probe-windows.ps1                 # 探测微信和 QQ
#   powershell -ExecutionPolicy Bypass -File probe-windows.ps1 Feishu          # 探测别的程序（写进程名）
#   powershell -ExecutionPolicy Bypass -File probe-windows.ps1 > probe.txt     # 保存结果发给开发者
# 运行前先打开要探测的程序并登录，切到一个聊天界面（窗口不要最小化）。
# 文件必须存成带 BOM 的 UTF-8，否则 Windows PowerShell 5.1 读错中文。

param([string[]]$Targets = @('Weixin', 'WeChat', 'QQ'))

$ErrorActionPreference = 'Continue'
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$A = [System.Windows.Automation.AutomationElement]
$MaxNodes = 1500
$MaxDepth = 45

function Mask($s) { if ([string]::IsNullOrEmpty($s)) { return '' }; $s = $s.Trim(); if ($s.Length -eq 0) { return '' }; return "$($s.Substring(0,1))…($($s.Length))" }

"DeskPal 探测（Windows）。名称只显示首字和长度。"
"系统=$([Environment]::OSVersion.VersionString)"
$walker = [System.Windows.Automation.TreeWalker]::RawViewWalker
foreach ($target in $Targets) {
    $procs = @(Get-Process -Name $target -ErrorAction SilentlyContinue)
    ""
    "== $target"
    if ($procs.Count -eq 0) { "没有在运行。"; continue }
    foreach ($p in $procs) {
        $version = try { $p.MainModule.FileVersionInfo.ProductVersion } catch { '?' }
        "进程 pid=$($p.Id) 版本=$version 主窗口=$($p.MainWindowHandle) 标题=$(Mask $p.MainWindowTitle)"
    }
    foreach ($p in $procs) {
        $cond = New-Object System.Windows.Automation.PropertyCondition($A::ProcessIdProperty, $p.Id)
        $tops = $A::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $cond)
        foreach ($top in $tops) {
            $c = $top.Current
            ""
            "-- 窗口 pid=$($p.Id) class=$($c.ClassName) 标题=$(Mask $c.Name) framework=$($c.FrameworkId) 在屏幕外=$($c.IsOffscreen)"
            # Qt、Chromium 第一次被查询时才建无障碍树，稍等再遍历
            [void]$walker.GetFirstChild($top); Start-Sleep -Milliseconds 1500
            $count = 0
            $stack = New-Object System.Collections.Stack
            $stack.Push(@($top, 0))
            while ($stack.Count -gt 0 -and $count -lt $MaxNodes) {
                $item = $stack.Pop(); $e = $item[0]; $d = $item[1]
                try { $ec = $e.Current } catch { continue }
                $count++
                $r = $ec.BoundingRectangle
                $rect = if ($r.IsEmpty) { '' } else { " rect=$([int]$r.X),$([int]$r.Y),$([int]$r.Width),$([int]$r.Height)" }
                ('  ' * [math]::Min($d, 30)) + "$($ec.ControlType.ProgrammaticName -replace 'ControlType.','') cls=$($ec.ClassName) id=$($ec.AutomationId) name=$(Mask $ec.Name)$rect"
                if ($d -ge $MaxDepth) { continue }
                $kids = @(); $k = $walker.GetFirstChild($e)
                while ($null -ne $k -and $kids.Count -lt 100) { $kids += $k; $k = $walker.GetNextSibling($k) }
                for ($i = $kids.Count - 1; $i -ge 0; $i--) { $stack.Push(@($kids[$i], $d + 1)) }
            }
            "共 $count 个元素" + $(if ($stack.Count -gt 0) { "（达到上限，后面没有列出）" } else { '' })
        }
    }
}
