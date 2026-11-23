# 更新日志

写给使用者看，每次推送 main 前整理。最上面一节是下一个正式版的版本号，测试版的改动也写在这一节。

- 每条写成用户能感受到的变化，用书面中文，页面上叫「房间」就写「房间」。下面缩进一行 `en:`，写对应的英文。
- 只改 CI、文档、测试，用户感受不到的，不写。
- 客户端更新后，第一次打开面板时按界面语言显示还没看过的条目。看过与否按中文比对：改了中文会再显示一次，只改英文不会。

## 0.1.5

- 新增 Linux 版（测试版，x64 的 AppImage），和 Windows、macOS 用户在同一个房间里互相看到状态。需要系统里有 GTK 4 和 WebKitGTK 6.0。使用 Wayland 的 GNOME 要在「设置 → 插件 → Linux 内置」里安装 GNOME 扩展并重新登录；装上 DeskPal 浏览器扩展（同一处获取）后可以按网站分类；其他 Wayland 桌面需要一个提供当前窗口的插件。Linux 版没有置顶卡片。
  en: DeskPal is now available for Linux (beta, an x64 AppImage). Linux, Windows, and Mac users see each other in the same room. GTK 4 and WebKitGTK 6.0 must be installed. On GNOME with Wayland, install the GNOME extension from Settings → Plugins → Linux built-in and sign in again; install the DeskPal browser extension (from the same place) to classify browsing by site; other Wayland desktops need a plugin that supplies the active window. The Linux app has no pinned cards.
- 设置里新增「插件」：当前窗口、网站、暂离、锁屏和正在播放的媒体都由插件提供。每个系统自带一个内置插件；也可以把自己写的插件放进插件文件夹，排在内置插件上面，替换其中某几项。插件默认关闭，开启前会确认。编写方法见仓库里的插件编写指南。
  en: Settings has a new Plugins section. The active window, the site, away, lock, and playing media all come from plugins. Each system has a built-in plugin, and you can put your own plugins in the plugins folder and rank them above the built-in one to replace some of these. New plugins start turned off and ask before turning on. See the plugin guide in the repository for how to write one.
- ChatGPT、OpenCode、WorkBuddy 归为工作中。
  en: ChatGPT, OpenCode, and WorkBuddy now count as working.

- 按安装目录识别为游戏时，名称改为取 Steam 库里的游戏名或游戏文件夹名，不再显示「xxx-Win64-Shipping」这类文件名；不在游戏库目录里的 Unreal 引擎游戏也能识别为在玩游戏。
  en: Games recognized by their install folder now show the name from the Steam library or the game folder instead of file names like "xxx-Win64-Shipping". Unreal Engine games outside game libraries are also recognized as playing a game.

- 同意分享使用数据后，按安装目录推测出类别的应用也会上传，用于改进默认分类。同意分享的说明已相应更新。
  en: If you share usage data, apps whose category was guessed from the install folder are also uploaded to improve the defaults. The sharing notice is updated to say so.

- 在面板和登录窗口里右键不再弹出网页的右键菜单。
  en: Right-clicking in the panel and sign-in window no longer opens a web page context menu.

- 首页的房间成员中，离线超过 1 分钟和已屏蔽的成员自动排在后面，重新上线后回到你拖动排好的位置；短暂断线重连不会改变顺序。
  en: Members on Home who have been offline for over a minute, and blocked members, now move to the bottom of the list. They return to the place you dragged them to when they come back online, and a brief reconnect doesn't change the order.

## 0.1.4

- 修复使用统计排行里各项使用时长没有对齐的问题。
  en: Fixed usage times in the stats ranking not lining up.

- 首页成员头像的右下角显示对方正在使用的客户端（Windows 或 macOS）。首页的成员卡片改为统一高度，名字、状态、应用和标题的位置固定，不随文字长短移动；应用名和标题合并为一行，完整内容悬停查看。房间页的成员列表不再显示实时状态，改为显示加入日期。
  en: Member avatars on Home now show which app the member is using (Windows or macOS) in the corner. Member cards on Home now all have the same height, and the name, status, app and title stay in place regardless of text length; the app and title share one line, and you can hover to see the full text. The member list on Rooms no longer shows live status and shows the join date instead.

- 首页「纠正分类」和「恢复默认」按钮改为图标，悬停显示名称。
  en: Reclassify and Restore default on Home are now icon buttons. Hover to see their names.

- 统计页按标题排行时，浏览器在标题末尾加的「正在播放音频」「内存用量高 - 917 MB」这类标签页状态不再拆成多行，同一个网页合并成一行，已有记录也会合并。
  en: In stats ranked by title, tab states a browser adds to the end of a title, such as "Audio playing" or "High memory usage - 917 MB", no longer split one page into many rows. Existing records are merged too.

- 修复 macOS 版安装后无法启动或登录窗口空白的问题。
  en: Fixed an issue where DeskPal could quit at startup or show a blank sign-in window after installation on macOS.

- 新增 macOS 版（测试版，Apple 芯片和 Intel 各一个安装包），和 Windows 用户在同一个房间里互相看到状态，功能和 Windows 版相同。图标在菜单栏。第一次打开时 macOS 会请求「辅助功能」权限，用来读取窗口标题和浏览器网址。macOS 15.4 及以上读不到正在播放的媒体，全屏播放和网页视频显示为当前应用，不显示「在看视频」。
  en: DeskPal is now available for macOS (beta, one installer for Apple silicon and one for Intel). Mac and Windows users see each other in the same rooms, with the same features. The icon lives in the menu bar. On first launch macOS asks for Accessibility access, which DeskPal uses to read window titles and browser addresses. On macOS 15.4 and later media playback can't be read, so full-screen playback and web videos show the current app instead of Watching video.

- 左侧导航的「分类」改名为「规则」并更换图标，「不记录标题」从统计页移到这里。命中规则时，房间成员和首页也只显示类别和名称，不再显示标题，适合远程控制软件的识别码这类内容。
  en: Categories in the left navigation is now Rules with a new icon, and Don't record titles has moved there from Stats. When a rule matches, room members and Home also show only the category and name, never the title, which helps with things like remote access codes.

- 设置中新增「个人数据」：个人分类、不记录标题和偏好设置默认只保存在本机，开启「同步到账号」后才保存到服务器并在其他电脑同步，关闭时立即删除服务器上的数据。之前已在服务器上保存过个人分类或偏好设置的账号保持开启。
  en: Settings now has Personal data. Categories, no-title rules and preferences stay on this computer by default and are saved on the server and synced to other computers only after you turn on Account sync. Turning it off deletes them from the server right away. Accounts that already kept categories or preferences on the server stay on.

- 个人数据可以导出、导入，也可以通过同步文件夹在电脑之间同步，同步文件夹可以和使用统计用同一个。
  en: Personal data can be exported and imported, or synced between computers through a sync folder, which can be the same folder as usage stats.

- 修复更新后、刚启动或从托盘打开面板时，偶尔整个窗口全黑或全白、需要重启程序才能恢复的问题。面板没有正常加载时会自动重新加载。
  en: Fixed the panel sometimes staying completely black or white after an update, at startup, or when opened from the tray, until DeskPal was restarted. The panel now reloads itself when it doesn't load.

- 双击托盘图标会打开面板，不再打开后又立即隐藏。
  en: Double-clicking the tray icon now opens the panel instead of opening and hiding it again.

- 下载更新时，进度不再长时间停在 70%：下载完成后显示「正在准备新版本」。
  en: The update progress no longer stays at 70% for a long time. After the download finishes, it shows Preparing version.

- 日统计的柱状图可以点击或拖动，查看所选时段的使用情况。按住 Ctrl 可以多选。
  en: The day chart can be clicked or dragged to show usage for the selected hours. Hold Ctrl to select more than one range.

- 暂停分享期间的使用统计默认不向房间成员公开，只显示为暂停分享。可以在房间设置中打开「包含暂停期间」。
  en: Usage while sharing is paused is hidden from room members by default and shown only as paused. Include time while paused can be turned on in the room settings.

- 查看房间成员的使用统计时，按对方的日期和当地时间显示，并注明对方所在时区及与你的时差。使用统计按记录时的当地时间归入日期，更换时区后，已有记录的日期和时段不变。
  en: Room members' usage stats now follow their own date and local time, with their time zone and the difference from yours. Usage stats stay on the date and time they were recorded, even after you change time zones.

- 时区不同时，使用统计里时间的悬停提示会在第二行显示对应的你的时间。
  en: When the time zones differ, hovering a time in usage stats shows the matching time for you on a second line.

- 设置中可以选择「切换等待」：新状态保持所选时间不变后才同步给房间成员，可选不等待。使用统计不受影响。
  en: Settings now has Switch delay: a new status is shared with the room after it stays the same for the chosen time, or right away. Usage stats aren't affected.

- 开启「同步到账号」后，切换等待、夜间模式、语言、关闭窗口时、窗口置顶和成员排序跟随账号，在其他电脑登录后保持一致。开机自启和加入测试版仍按每台电脑分别设置。
  en: With Account sync on, switch delay, theme, language, the close option, always on top, and member order follow your account to other computers. Start with Windows and beta updates are still set on each computer.

- 程序图标改成了深色背景上的字母 D，托盘、任务栏、开始菜单和安装程序都会显示。
  en: The icon is now a letter D on a dark background, shown in the tray, the taskbar, the Start menu, and the installer.

- 公开了使用统计的成员离线后，首页的「查看统计」会随离线状态一起消失。
  en: When a member who shares usage stats goes offline, View stats on Home disappears with the offline status.

- 每个房间创建时就有邀请码，在房间页的「当前房间」中可以查看和复制。邀请码不再过期，只有所有者可以重新生成，重新生成后旧邀请码失效。
  en: Every room gets an invite code when it is created, shown under Current room on the Rooms page, ready to copy. Invite codes no longer expire. Only the owner can create a new one, which replaces the old code.

- Claude 和 Codex 显示为工作中。
  en: Claude and Codex now show as Working.

- 关闭窗口时可以选择直接退出，设置里的「关闭窗口时」也可以选这一项。
  en: Closing the window can quit DeskPal, and the same choice is available in Settings.

- 设置页的账号显示头像；更新和反馈合并为「关于」，可以打开日志文件夹；注册邀请码改为卡片列表
  en: The account in Settings shows an avatar. Updates and feedback are grouped under About, where you can open the log folder. Signup invite codes are shown as a card list

- 分类页可以按类别筛选，并直接修改类别；待归类也显示在这里，选好类别即保存
  en: On the Rules page you can filter by category and change a category in place. Unclassified apps appear there too, and choosing a category saves it

- 房间页改为整块卡片：当前房间单独成卡，成员显示实时状态，屏蔽和踢出收进菜单
  en: The Rooms page uses one card per list. The current room has its own card, members show their live status, and block and kick are in a menu

- 首页「我的状态」中，自己的名字不再带「（我）」
  en: Your name in My status no longer includes "(you)"

- 检查更新过于频繁时，提示几点后可以再试，到时间之前不再检查；5 分钟内重复检查直接显示上次的结果
  en: If update checks are too frequent, DeskPal tells you when to try again and waits until then. Checking again within 5 minutes shows the last result

## 0.1.3

- 首页的「房间成员」标题不再显示人数；「拖动可调整顺序」和「待归类」的说明收进标题旁的问号，悬停查看；「我的状态」去掉了问号，识别依据改为悬停状态时显示
  en: The Members heading on Home no longer shows a count; the reorder tip and the Unclassified note moved into a ? next to the heading; in My status, hover the status to see how it was recognized
- 标题栏的房间切换显示房间色块和在线人数，菜单里用色块标出当前房间
  en: The room switcher in the title bar shows a color badge and how many people are online, and the menu marks the current room with its badge
- 左侧导航的「状态」改名为「首页」，并更换了「首页」和「房间」的图标
  en: In the left navigation, Status is now Home, with new icons for Home and Rooms
- 识别为「其他」的应用改为显示「其他活动」，应用名显示在下一行，和工作中、在聊天等状态的排版一致
  en: Apps in the Other category now show "Other activity" with the app name on the next line, matching the layout of other statuses
- 面板左上角改为 DeskPal 文字标志
  en: The panel's top-left corner now shows the DeskPal wordmark

## 0.1.2

- 「其他」状态改用绿色，不再和离线一样是灰色；房间成员和自己的状态卡片按状态显示颜色，更容易一眼看出谁在做什么
  en: The Other status is now green instead of gray like Offline. Your status card and room members are colored by status, so you can tell at a glance what everyone is doing
- 浅色模式改用柔和的浅灰底色，不再是纯白
  en: Light mode now uses soft gray backgrounds instead of pure white
- 房间成员可以拖动调整顺序，顺序只保存在本机
  en: You can drag room members to reorder them. The order is saved on this computer only
- DeskPal 有了自己的图标，托盘、任务栏、开始菜单和安装程序都使用新图标
  en: DeskPal has its own icon, shown in the tray, the taskbar, the Start menu, and the installer
- 去掉「保留时长」，使用统计的时间轴和标题一直保留，不再只留每天的总时长
  en: "Keep details for" has been removed. The timeline and titles in usage stats are kept, not just the daily totals
- 同步到服务器暂时只对部分账号开放。其他账号已开启的同步已关闭，服务器上的数据已删除，本机数据保留
  en: Server sync is now available to some accounts only. For other accounts, sync has been turned off and the server copy deleted. Data on this computer is kept
- 同步到服务器不再限制使用统计的总大小，也不限制每一天的大小
  en: Server sync no longer limits the total size of usage stats, or the size of a single day
- 导入使用统计不再限制文件大小
  en: Importing usage stats no longer limits the file size
- 远程桌面、虚拟机、安卓模拟器、PDF 阅读器、AI 助手、笔记软件、个人邮箱等工作和生活都常用的应用，不再默认算作工作中或在玩游戏，改为显示正在使用该应用
  en: Apps used for both work and personal life, such as remote desktop, virtual machines, Android emulators, PDF readers, AI assistants, note apps, and personal email, no longer count as working or gaming by default. They show the app name instead
- 统计图表固定按类别显示，视频和游戏分开；排行可以切换类别、应用和标题，点图例中的某一类可以只看这一类
  en: Usage stats charts are always grouped by category, with video and games shown separately. The ranking switches between categories, apps, and titles. Click a category in the legend to show only that category
- 房间隐私级别改名为只显示类别、显示应用、显示详情（标题），各级别显示的内容不变
  en: Room privacy levels are renamed to Category only, Include app, and Include titles. What each level shows is unchanged
- 统计里可以更改某个应用或网站的分类，过去的记录也会按新的分类显示
  en: You can change how an app or a site is categorized in Usage stats. Past records follow the new category
- 查看今天的统计时，不再和昨天比较
  en: Today's usage stats are no longer compared with yesterday
- 和上一周期比较时，列出变化最大的几个类别
  en: Comparisons with the previous period list the categories that changed most
- 点击统计排行中的应用或标题，可以查看它的使用次数、平均每次时长、最长一次、最常用时段和常见标题；更改分类也在这里
  en: Click an app or a title in the ranking to see its sessions, average and longest session, peak hour, and top titles. You can change its category there too
- 自己的统计显示首次活动和最后活动时间（凌晨 4 点前算前一天），周和月显示一周节律
  en: Your own stats show when you were first and last active (activity before 4 a.m. counts toward the previous day). Week and month views show your weekly rhythm
- 浏览器里正在播放的视频记为在看视频。全屏游戏不再因为别处正在播放视频而被记成在看视频
  en: A video playing in the browser counts as watching a video. A full-screen game is no longer counted as a video because something else is playing
- 设置里可以选择暂离的时间，并在其他电脑登录后保持一致
  en: You can choose how soon you are shown as away, and that choice follows the account
- 开启同步到服务器后，不记录标题也跟随账号
  en: With sync to the server on, the titles you leave unrecorded follow the account too
- 使用统计可以用 PIN 码加密后同步到服务器，在其他电脑上登录并输入 PIN 码后即可同步；关闭同步时立即删除服务器上的数据
  en: Usage stats can be encrypted with a PIN and synced to the server. Sign in on another computer and enter the PIN to sync there. Turning sync off deletes the server copy immediately
- 使用统计也可以通过网盘等同步工具在电脑之间同步：选择一个同步文件夹，文件用 PIN 码加密，服务器不保存这些数据
  en: Usage stats can also be synced through a cloud drive or sync app. Choose a sync folder; the files are encrypted with a PIN and the server doesn't store them
- 开启任一种同步时，需先关闭同步才能清除使用统计
  en: Turn off sync before clearing usage stats
- 更新后第一次打开面板时，显示这次更新了哪些内容
  en: The first time you open the panel after an update, it shows what changed
- 标题栏的房间菜单可以选择「无房间」，暂时不向任何房间分享状态
  en: Choose No room in the title bar to stop sharing your status
- 删除「戳一下」和手动状态
  en: Poke and custom status have been removed
- 设置里可以修改显示名称，允许重名
  en: You can change your display name. Duplicate names are allowed
- 房间成员的「已持续」时间固定显示在右侧按钮下方，和状态标签同一行；头像右下角不再显示状态圆点
  en: How long a member has been in a status now stays on the right, below the buttons. Avatars no longer show a status dot
- 注册时可以填写注册邀请码。服务器开启后，没有注册邀请码不能注册
  en: You can enter a signup invite code when you register. When the server requires one, you cannot register without it
- 管理员可以在设置里生成、作废注册邀请码，查看谁用哪个码注册
  en: Admins can create and revoke signup invite codes in Settings, and see who used each one
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
- 英文界面里，默认分类识别出的应用和网站显示英文名称，例如微信显示为 WeChat；房间成员各自按自己的界面语言显示
  en: In the English interface, apps and sites recognized by the default categories show English names, such as WeChat. Each member sees names in their own interface language
- 登录页和设置页不再显示服务器地址，连接失败时的提示也不再包含地址
  en: The sign-in page and Settings no longer show the server address, and connection errors no longer include it
- 「待归类」里今天的使用时长在重启 DeskPal 后继续累计，不再从 0 开始
  en: Today's time in Uncategorized keeps adding up after DeskPal restarts instead of starting from zero

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
