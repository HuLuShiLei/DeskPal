# 更新日志

写给使用者看，每次推送 main 前整理。最上面一节是下一个正式版的版本号，测试版的改动也写在这一节。

- 每条写成用户能感受到的变化，用书面中文，页面上叫「房间」就写「房间」。下面缩进一行 `en:`，写对应的英文。
- 只改 CI、文档、测试，用户感受不到的，不写。
- 客户端更新后，第一次打开面板时按界面语言显示还没看过的条目。看过与否按中文比对：改了中文会再显示一次，只改英文不会。

## 0.1.2

- 更新后第一次打开面板时，显示这次更新了哪些内容
  en: The first time you open the panel after an update, it shows what changed
- 标题栏的房间菜单可以选择「无房间」，暂时不向任何房间分享状态
  en: Choose No room in the title bar to stop sharing your status
- 删除「戳一下」和手动状态
  en: Poke and custom status have been removed
- 设置里可以修改显示名称，允许重名
  en: You can change your display name. Duplicate names are allowed
- 面板可以拖动边框调整大小
  en: The panel can be resized by dragging its edges
- 「我的分类」移到左侧导航单独的「分类」页，可以搜索
  en: My categories is now its own page, and it can be searched
- 「我的状态」按当前房间的隐私设置显示，和房间成员看到的一致
  en: My status uses this room's privacy settings, the same as what members see
- 隐私设置改完立即生效，不用再点保存
  en: Privacy changes apply immediately
- 创建或加入房间时，隐私设置默认只显示状态
  en: A new room shows status only until you change it
- 创建房间时默认填入「显示名称的房间」
  en: A new room starts with your name. You can change it
- 房间人数上限改为 20 人
  en: A room now holds up to 20 members
- 类别名称改为「视频」「游戏」，去掉「放松」前缀
  en: The categories are now Video and Game
- 状态「暂时离开」改为「暂离」
  en: Away uses a shorter label
- 修复窗口较窄时，「分类」页较长的应用或网站名称和类别标签重叠
  en: Long names on the Categories page no longer overlap the category
- 状态页房间成员右侧的大头针可以把该成员置顶到桌面，显示为一张小卡片，可以拖动，双击打开面板；切换房间时自动移除
  en: Pin a member to a small desktop card. Drag it, or double-click to open the panel. It closes when you switch rooms
- 「我的状态」不再显示「房间成员看不到标题」的提示；鼠标指向状态时，显示自己完整的类别、应用和标题
  en: My status no longer says that titles are hidden. Point at it to see your full category, app, and title
- 新状态的倒计时移到「我的状态」右上角，进度条贴在卡片底边，倒计时出现和结束时下方内容不再上下跳动
  en: The countdown sits at the top right of My status, and the bar sits on the bottom edge of the card
- 还没有收到状态的房间成员不再显示「还没有收到状态」
  en: A member with no status yet no longer shows a placeholder
- 房间页可以屏蔽某个成员：你们在这个房间里互相看不到对方的状态，对方不会收到通知，在对方那里你显示为离线；可以随时取消屏蔽
  en: You can block a member. You stop seeing each other's status, they are not told, and you appear offline to them. You can unblock later
- 修复窗口较窄时，「我的状态」里即将变为的新状态显示不全
  en: The upcoming status is no longer cut off in a narrow window
- 同一账号同一时间只能在一台电脑上登录。在另一台电脑登录后，这台电脑会回到登录页并提示
  en: An account can be signed in on one computer. Signing in elsewhere returns this computer to the sign-in page
- 新增「统计」页，像手机的屏幕使用时间一样查看电脑时间花在了哪里：可以按日、周、月查看，按状态、类别或标题分类，显示时间轴、每小时或每天的用时和使用最多的应用。统计只保存在这台电脑上，暂停分享时也会记录
  en: Stats shows where time on this computer went, by day, week, or month. It stays on this computer and keeps recording while sharing is paused
- 统计可以导出、导入和清除；可以设置保留多少天的详细记录；可以按应用名、进程名、网址或标题关键词设置不记录标题，适合网银、病历这类内容
  en: You can export, import, and clear stats, choose how long to keep detail, and skip titles for selected apps, processes, sites, or keywords
- 房间的隐私设置里可以「公开使用统计」，并选择成员可以查看最近 1、3、7 或 30 天，默认关闭。公开后各类别按这个房间的隐私设置显示；只有你在线并且在这个房间时，成员才能查看，内容加密后经服务器转发
  en: A room can share the last 1, 3, 7, or 30 days of stats. It is off by default. Members can view them only while you are in that room, and the server only relays the encrypted data
- 房间成员公开了使用统计时，状态页成员右侧显示统计按钮；统计页可以看到最近 7 天谁查看过你的统计
  en: When a member shares stats, a button appears beside them. Stats lists who viewed yours in the last 7 days
- 支持英文界面。设置里可以选择语言：跟随系统、简体中文或 English，修改后立即生效；安装向导也会按系统语言显示中文或英文
  en: The interface can be English. Choose Follow system, 简体中文, or English. The installer follows the system language
- 说明和提示改为更简短的书面用语；英文按钮和选项缩短，避免窗口较窄时折行或溢出
  en: Explanations are shorter, and English labels are shortened so they fit a narrow window
- 更新内容按界面语言显示
  en: The update list follows the interface language
- 统计页说明改为：数据仅保存至本地，传输时加密转发，不保存到服务器
  en: The stats note now says data is stored locally only, encrypted in transit, and not kept on the server

## 0.1.1

- 点关闭时可以选择最小化，或最小化到系统托盘
  en: Closing the window can minimize it, or minimize it to the notification area
- 状态页显示新状态生效前的倒计时，稳定时间改为 10 秒
  en: The status page counts down before a new status applies. It now settles in 10 seconds
- 设置里可以切换夜间模式
  en: You can choose light mode, dark mode, or follow the system
- 设置里增加「问题与建议」
  en: Settings includes a link for issues and suggestions
- 纠正分类改为个人分类：只影响自己，所有房间和电脑通用
  en: Correcting a category applies only to you, in every room and on every computer
- 界面改为桌面程序样式
  en: The window now looks like a desktop app
- 安装时可以选择安装目录
  en: You can choose the install folder
- 支持多人房间
  en: A room can have more than two members
- 前台是浏览器时显示「正在浏览网页」
  en: A browser in the foreground shows as browsing the web
- 手动检查更新时显示检查和下载进度
  en: Checking for updates shows the check and the download progress

## 0.1.0

- 第一个版本
  en: First release
