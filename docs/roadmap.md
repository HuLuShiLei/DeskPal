# DeskPal 路线图

按顺序做，前一阶段没验证就不要开始下一阶段。每一项开始前，先在 [progress.md](progress.md) 的“已定”里写清楚口径。

## 阶段 1.5：把原型用起来

- 部署到真实服务器（`deploy/README.md`），两台电脑实际用一周，记录误判。
- 根据误判补内置规则（skill `deskpal-builtin-rules`）。
- 把“还没验证”里的信号和对话框逐个验证。
- 本地日志写入文件（`%LOCALAPPDATA%\DeskPal\logs`），方便排查。
- 代码签名：Azure Artifact Signing 不对中国个人开放；仓库开源的话可以用 Certum 开源证书（约 €49/年）或申请 SignPath Foundation（免费），否则买 OV 证书。签名只去掉“未知发布者”，SmartScreen 信誉仍要靠下载量慢慢积累。自动更新已经用 Velopack 接上 GitHub Release。

## 阶段 2：体验

| 功能 | 要点 |
|---|---|
| 多人 Room、加入多个 Room | 放开 `MaxMembersPerRoom`；界面改成 Room 列表 + 成员列表；Room 规则改成只在当前 Room 生效（见 known-issues） |
| 摸鱼同步提醒 | 客户端检测到 Room 里有人和自己同时 `Slacking`，提示“对方也在摸鱼，聊两句？”；在客户端判断，不需要新消息类型 |
| 对方空闲时提醒 | 订阅对方从 `Working` 变成其他状态，一次性提醒 |
| 当天时间轴 | 客户端按小时聚合类别占比，只存聚合结果；要同步就加密以后再上传 |
| 手机端查看 | Server 提供 PWA 页面，只读 |
| 浏览器扩展 | MV3 扩展 + Native Messaging 上报当前标签的域名，替代 UI Automation |
| 未知应用归类 | 用户同意后，上报未命中的进程名和域名（不含标题）到待归类队列，批量归类（可选 LLM）后合入内置规则 |
| 离线消息 | “戳一下”在对方离线时暂存，上线后送达 |
| Refresh token | 现在的 token 30 天过期，需要重新登录 |

## 阶段 3：对外发布

- 端到端加密：成员本地生成密钥对，Room 密钥在成员之间分发，成员退出时轮换；`Envelope.Enc` 改为加密算法标识，Payload 改为密文。规模大了以后考虑 MLS（RFC 9420）。Server 不需要改。
- 合规：国内《个人信息保护法》（隐私政策、单独同意、数据最小化），海外 GDPR。
- 防滥用：不提供隐藏运行，托盘图标始终可见；Room 内权限对等；不做“管理员查看员工”这种单向模式。
- 数据库按需从 SQLite 换成 PostgreSQL（换 EF Core Provider，重新生成迁移）。

## 阶段 4：游戏化（Bongo Cat 那种挂机打字收集）

- 新增 `Features/Game`（Server）和客户端游戏模块，不改动现有模块。
- 输入次数用 Raw Input 计数，**只计次数，不读按键内容**，作为新的 `ActivitySignal`；游戏模块直接订阅 `ActivityBus`，不进入状态判断（见 skill `deskpal-add-signal`）。
- 道具、排行榜由 Server 判定，防作弊；新增 `game.*` 消息类型（见 skill `deskpal-add-message-type`）。