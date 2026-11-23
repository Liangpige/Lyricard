# Lyricard · 浮声 — 安装与卸载

一个放在桌面上的 Apple Music 歌词卡片：曲名 / 作者 / 单行歌词 / 进度条 / 上一曲-暂停-下一曲 / 圆形封面，浮在桌面最上层，跟着 Apple Music 播放。

- 程序：`Lyricard.exe`（单文件，免安装，约 290 KB）
- 配置与歌词缓存：同目录 `data\`
- 无需管理员权限，不写系统目录，除开机自启外不碰注册表
- 许可证：**MIT**，全文见包内 `LICENSE`；随包还有 `README.md`（功能与已知限制）
- 包内文件：`Lyricard.exe`、`Make shortcut.cmd`、`README.md`、`INSTALL.md`、`LICENSE`（**不含**预生成的快捷方式——双击 `Make shortcut.cmd` 会按你自己的路径建一个）

---

## 一、系统要求

| 项目 | 要求 |
| --- | --- |
| 系统 | Windows 10 1809 及以上、Windows 11（需要系统的媒体会话 API） |
| 运行时 | .NET Framework 4.6.2+（Win10 1809 之后系统自带 4.7/4.8，通常不用另装） |
| 播放器 | Apple Music（Windows 版）。想用别的播放器见第五节 |
| 字体 | 中文界面需要系统有「微软雅黑」；没有会自动回退，只是字形不那么好看 |
| 网络 | 可选。没网也能跑，只是没缓存过的歌取不到歌词 |

## 二、安装（三步）

1. 把 `Lyricard-v0.2.0.zip` 解压到一个**你自己有写权限**的目录，例如
   `C:\Users\<你的用户名>\Apps\Lyricard\`。
   > 不建议放在 `C:\Program Files\` 下 —— 那里普通权限写不了 `data\`，配置和缓存会存不下来。
2. 双击 `Lyricard.exe`。
3. 第一次启动时 Windows SmartScreen 可能弹「Windows 已保护你的电脑」：
   点「更多信息」→「仍要运行」。程序没有代码签名证书，这是正常现象。

启动后卡片出现在**屏幕右上角**（默认外观：无底板、只有文字和封面）。

## 三、第一次运行会发生什么

- 如果 Apple Music 开着：卡片显示「♪ 等待播放」，一开始播放就切到正常卡片。
- 如果 Apple Music 没开：8 秒后托盘弹一个提示，告诉你需要 Apple Music（或打开「监听所有播放器」）。
- 开机自启**默认不开**。

## 四、怎么用

| 操作 | 做法 |
| --- | --- |
| 移动 | 按住文字或封面拖动（空白处是鼠标穿透的，抓不到） |
| 右键菜单 | 在文字、封面、进度条上右键 |
| 缩放 / 透明度 | 控制页面「放大 / 缩小 / 更透明 / 更不透明」，或设置面板里的滑块 |
| 外观微调 | 控制页面 →「外观设置…」（字号、颜色、对齐、进度条、封面、预设、语言） |
| 托盘菜单 | 任务栏托盘的小图标，功能与右键菜单基本相同，另外多「显示 / 隐藏」「卸载」 |
| 快捷键 | 无全局快捷键，避免和别人冲突 |

## 五、用其他播放器

卡片读的是 Windows 的媒体会话（SMTC），**凡是把播放状态交给系统的播放器都能用**：媒体播放器、Spotify、网易云、QQ 音乐、浏览器里的网页播放器等等。

但程序是给 Apple Music 写的，选会话的规则是：

1. 只要 Apple Music 有会话（**哪怕暂停着**），就用 Apple Music；
2. 只有 Apple Music 完全没有会话时，打开「监听所有播放器」才会用系统的「正在播放」会话。

所以要用别的播放器，请**把 Apple Music 完全退出**（不是暂停），再打开「监听所有播放器」。

不支持的：不向系统发布媒体会话的老播放器（如未装插件的旧版 foobar2000、千千静听等），以及独占全屏游戏里的声音。

## 六、设置与数据

全部在 exe 同目录的 `data\`：

| 路径 | 内容 |
| --- | --- |
| `data\config.ini` | 所有设置（位置、外观、语言、预设、自启…），纯文本，可直接编辑 |
| `data\lyrics\` | 歌词缓存（`.lrc` + `.miss` 标记） |
| `data\lrc\` | **你自己放歌词的地方**，优先级最高（`歌名.lrc` 或 `歌名 - 艺人.lrc`） |
| `data\lrc-export\` | 菜单「导出歌词（分享用）」的输出目录 |
| `data\amc.log` | 运行日志（超过 300 KB 会重建），出问题先看它 |

想换一台机器接着用：把整个目录拷过去即可（位置会按新屏幕自动校正）。

## 七、卸载

**方法一（推荐）**：托盘图标右键 →「卸载…」→ 确认。
它会：关掉所有正在运行的副本 → 删除开机自启项 → 删除 `data\` 目录 → 删除 exe 本身（最后一步交给一个临时 cmd 进程，所以窗口会先消失）。

**方法二（命令行）**：

```bat
Lyricard.exe --uninstall          :: 带确认框
Lyricard.exe --uninstall --silent :: 静默卸载
```

**方法三（手动）**：退出程序 → 删掉整个目录 → 如果之前开过开机自启，再删掉注册表 `HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run` 下的 `Lyricard`。

## 八、常见问题

**Q：卡片没出现？**
先看 `data\amc.log`。若是 `am running=False` 且没有媒体会话 → Apple Music 没开；若是 `intg=LOW` 之类 → 程序被放在了受限目录里。

**Q：装好了，但桌面上一张卡片都没有？**
说明这台机器上没有装、或者没有启动 Apple Music —— 这时桌面卡片会完全隐藏（不弹任何提示）。入口是**双击托盘图标**，或者再双击一次快捷方式打开主页面。若 Apple Music 开着但只是没播放，卡片会显示 `♪ Waiting for music`。想用别的播放器，在主页「监听的播放器」里把它勾上就行（勾了谁就听谁；一个都不勾＝谁都不听）。

**Q：关掉网易云，卡片怎么还赖在桌面上？**
两种可能，看日志就分得清：①网易云默认**关闭主面板时最小化到托盘**，进程还在、会话还挂着（停在最后一首），这时卡片留着是对的 —— 托盘右键「退出」才真的收；②它退出后留下了**幽灵会话**（SMTC 条目没注销干净）。程序有 `IsGhost()` 兜底：进程不在 + 没声音，约 1 秒就会收起卡片，日志里能看到 `smtc ghost dropped aumid=…`。两者都不是的话，把那段日志发我。

**Q：网易云的进度条走着走着不动了 / 总时长是 0:00？**
新版已修好大半：总时长优先用 SMTC，拿不到就读它自己的播放队列 `%LOCALAPPDATA%\NetEase\CloudMusic\webdata\file\playingList`（UTF-8 JSON，每条带毫秒时长），按歌名 + 艺人匹配；播放状态改用"该播放器自己那条音频会话的峰值"判定，别的程序放声音不会再误判。**剩余限制**：队列里没有播放位置，进度条仍靠自走秒表（精度不如 Apple Music）；放电台 / 云盘 / 本地导入时队列里可能没这首，会回退到「歌词末句 + 5 秒」估算。

**Q：有歌但显示「♪ Instrumental · no lyrics」/「♪ No lyrics available」？**
后者是四个歌词源都没找到；前者是找到了、但内容本身标注为纯音乐。右键「换一个歌词源」可强制换源重试。

**Q：歌词比声音快/慢半秒？**
右键菜单「歌词提前 0.1s / 歌词延后 0.1s」，或者直接改 `config.ini` 的 `LyricOffset`。

**Q：四个歌词源安全吗？**
歌词来自 QQ 音乐 / 网易云音乐 / lrclib 的非官方接口，只用于显示，不涉及账号登录。介意的话把 `data\lrc\` 放自己的文件，程序会优先用本地。

**Q：发到其他机器上要重新设置吗？**
不用。默认外观就是作者调好的那套；位置按目标屏幕自动校正。

---

# Install (English, short)

1. Unzip `Lyricard-v0.2.0.zip` into a folder you can write to (e.g. `%USERPROFILE%\Apps\Lyricard`).
   Do **not** use `C:\Program Files` - the app stores `data\` next to the exe.
2. Run `Lyricard.exe`. If SmartScreen complains, choose "More info" → "Run anyway" (the exe is unsigned).
3. The card appears at the top-right corner. It shows "♪ Waiting for music" until Apple Music starts playing.

Requirements: Windows 10 1809+ / Windows 11, .NET Framework 4.6.2+ (bundled with Windows), Apple Music for Windows.

Other players: the card reads the Windows media session (SMTC). Any player that publishes a session works, but Apple Music always wins while it has a session - quit Apple Music (not just pause) and enable "Listen to all players" to follow something else.

Uninstall: tray icon → "Uninstall…", or run `Lyricard.exe --uninstall`. It removes the startup entry, the `data\` folder and the exe itself.

License: MIT - see `LICENSE` in the package (also `README.md` for features and known limits). Not affiliated with Apple; lyrics come from public endpoints and belong to their respective owners.

Settings live in `data\config.ini`; lyrics cache in `data\lyrics\`; drop your own `Song.lrc` files into `data\lrc\` to override.
