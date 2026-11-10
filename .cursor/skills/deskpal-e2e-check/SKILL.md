---
name: deskpal-e2e-check
description: 在本机对 DeskPal 做端到端自检：启动 Server，用 REST 创建测试账号和 Room，以测试账号启动 WPF 客户端，再用一个模拟 Room 成员的 SignalR 小程序收发状态和“戳一下”，最后清理现场。改了采集器、PresenceHub、RoomState、PresenceCoordinator 或界面后，或者用户说自检、联调、端到端测试时使用。
---

# DeskPal 端到端自检

单元测试和 `PresenceHubTests` 覆盖不到真实的采集和界面，这个流程补上这一块。

```
进度：
- [ ] 1. 编译并启动 Server
- [ ] 2. 备份本机已有的客户端数据
- [ ] 3. 创建测试账号和 Room
- [ ] 4. 写入测试账号的客户端设置
- [ ] 5. 启动模拟成员和客户端
- [ ] 6. 验证
- [ ] 7. 清理并恢复
```

所有临时文件放 `.local/tmp/e2e/`。PowerShell 命令按用户规则写（不用 Bash 语法，写文件用无 BOM 的 UTF-8）。

## 1. 编译并启动 Server

```powershell
dotnet build DeskPal.slnx
cd src/DeskPal.Server; dotnet run          # 放到后台，block_until_ms: 0
Invoke-RestMethod http://localhost:5180/health
```

## 2. 备份本机已有的客户端数据

`%APPDATA%\DeskPal` 是用户真实的登录信息。存在就整个移动到 `.local/tmp/e2e/appdata-backup/`，第 7 步再移回来。不要直接覆盖。

## 3. 创建测试账号和 Room

请求体要按 UTF-8 字节发送，否则中文 Room 名在 Windows PowerShell 5 里会乱码：

```powershell
function Post($path, $body, $token) {
  $h = @{}; if ($token) { $h.Authorization = "Bearer $token" }
  Invoke-RestMethod -Method Post -Uri "http://localhost:5180$path" -Headers $h `
    -Body ([System.Text.Encoding]::UTF8.GetBytes(($body | ConvertTo-Json))) -ContentType 'application/json; charset=utf-8'
}
$sfx = Get-Random -Maximum 99999
$a = Post '/api/auth/register' @{ userName = "e2e_alice$sfx"; displayName = 'Alice'; password = 'password123' }
$b = Post '/api/auth/register' @{ userName = "e2e_bob$sfx"; displayName = 'Bob'; password = 'password123' }
$room = Post '/api/rooms' @{ name = '测试 Room' } $a.token
$inv = Post "/api/rooms/$($room.id)/invites" @{} $a.token
Post '/api/rooms/join' @{ code = $inv.code } $b.token | Out-Null
```

把两个 token 和 `roomId` 存到 `.local/tmp/e2e/accounts.json`，不要贴进对话。

## 4. 写入测试账号的客户端设置

Token 用 DPAPI 加密，和 `AppSettings.SetToken` 的做法一致：

```powershell
Add-Type -AssemblyName System.Security
$protected = [Convert]::ToBase64String([System.Security.Cryptography.ProtectedData]::Protect(
  [System.Text.Encoding]::UTF8.GetBytes($a.token), $null, 'CurrentUser'))
$settings = [ordered]@{ serverUrl = 'http://localhost:5180'; userId = $a.userId; userName = $a.userName;
  displayName = 'Alice'; protectedToken = $protected; selectedRoomId = $room.id; idleMinutes = 5; stableSeconds = 30 } | ConvertTo-Json
```

写到 `%APPDATA%\DeskPal\settings.json`。

## 5. 启动模拟成员和客户端

按 [bot-template.md](bot-template.md) 在 `.local/tmp/e2e/BobBot/` 建项目并编译。参数依次是：Server 地址、bob 的 token、roomId、运行秒数。

```powershell
dotnet run --no-build -- http://localhost:5180 <bobToken> <roomId> 150   # 后台运行
Start-Process src\DeskPal.Client\bin\Debug\net10.0-windows10.0.19041.0\DeskPal.exe
```

## 6. 验证

读模拟成员的输出，并用 computer-use 截图看客户端界面：

| 检查项 | 预期 |
|---|---|
| 客户端顶部 | “已连接” |
| Bob 的卡片 | “在看视频 · 哔哩哔哩：【猫猫】合集”，“已持续 12 分钟” |
| “我的状态” | 识别出当前前台应用和规则来源 |
| 模拟成员收到 alice 的状态 | 首条状态不是空的 `Other`；30 秒内收到稳定后的状态 |
| 点“戳一下”并输入文字 | 模拟成员收到 `interaction.poke`，文字一致 |
| 勾选“暂停分享” | 模拟成员立即收到 `Paused` |

改了哪一块，就加测哪一块。例如改了浏览器采集：打开 B 站，看“本机识别”是不是“在看视频 · 哔哩哔哩”。

## 7. 清理并恢复

1. 结束 `DeskPal`、`DeskPal.Server` 和模拟成员进程。
2. **先把终端的工作目录切回仓库根目录**，否则删除 `.local` 会提示目录正在使用。
3. 删除 `%APPDATA%\DeskPal`，再把第 2 步的备份移回去。
4. 删除 `src/DeskPal.Server/storage/`（测试账号都在这个数据库里）。路径要写全，不要写成 `data`，在 Windows 上它和源码目录 `Data` 是同一个目录。
5. 删除 `.local/tmp/e2e/`。