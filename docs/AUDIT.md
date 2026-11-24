# 代码安全 / 隐私 / 相似度自查

对象：`src/`（Lyricard v0.1.1，纯代码 WPF，C# 5）
日期：2026-11-23 · 方法：静态阅读 + 源码/二进制字符串 grep + 发行包内容核对

结论先说：**没有发现隐私外泄、没有可疑权限、没有引入任何第三方源码（因此不涉及 GPL/AGPL 传染），可以自由分发。** 三条需要维护者自己决定的小事在最后。

---

## 一、隐私

### 1.1 网络请求：只有 4 个只读端点，没有遥测

| 端点 | 用途 | 带出去的内容 |
| --- | --- | --- |
| `music.163.com/api/search/get/web` | 网易云搜歌 | 歌名/艺人关键词 |
| `music.163.com/api/song/lyric` | 网易云取词 | 歌曲 id |
| `c.y.qq.com/soso/fcgi-bin/client_search_cp` | QQ 搜歌 | 歌名/艺人关键词 |
| `c.y.qq.com/lyric/fcgi-bin/fcg_query_lyric_new.fcg` | QQ 取词 | `songmid` |
| `lrclib.net/api/get` | lrclib 取词 | 歌名/艺人/专辑/时长 |

- 全部是 **GET + 只读**，不做上传、不 POST、不带 `Cookie`、不带账号、不带设备 id/机器名/用户名。
- 请求头只有 `Accept-Language: zh-CN,zh;q=0.9,en;q=0.8` 和（QQ 要求的）`Referer: https://y.qq.com/`。
- 唯一"出得去"的信息就是**你在放的那首歌的歌名与艺人** —— 这是自动取词的硬前提，不可避免；但它等于"告诉第三方你正在听什么"，介意的话把 `data\lrc\` 放本地歌词、或在断网环境使用。
- 没有任何统计/崩溃上报/host 心跳；`grep -E "http"` 全源码只有上面这几处。

### 1.2 本地写入：只在程序自己的 `data\`

`config.ini`、`amc.log`（300 KB 自动截断）、`lyrics\`、`lrc\`、`lrc-export\`。写不进去时回退 `%APPDATA%\Lyricard\`。卸载时整目录删除。

### 1.3 注册表

- **写**：只有 `HKCU\...\Run\Lyricard`（开机自启，可关）。
- **读**：`HKLM/HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths`（只读，用来发现装了哪些播放器）。
- 不碰服务、不碰驱动、不碰 HKLM 写入。

### 1.4 日志里的信息

`data\amc.log` 会记 `fetch start t=歌名 | a=艺人`，也会记 `token intg=MEDIUM(8192) admin=False`（这是 Windows 令牌完整性级别，不是密码/令牌串）。**不记账号、不记路径以外的个人信息**。日志只在本机。

### 1.5 二进制与文档里的个人标识

用 `strings` 扫过发布版 exe：本机用户名 / 私人称谓 / 本机安装路径 命中数 **0**。`README.md` / `INSTALL.md` 同样干净（没有本机路径、没有私人称谓）。

### 1.6 权限

`src/app.manifest` 没有 `requestedExecutionLevel` → 默认 **asInvoker**，不申请管理员。全部功能（托盘、置顶、媒体会话、自启）都在普通用户权限内完成。

---

## 二、代码安全

### 2.1 原生调用清单（P/Invoke）

| DLL | 用途 |
| --- | --- |
| `user32.dll` | `SetWindowPos`（置顶）、`GetForegroundWindow`/`GetClassNameW`（避开开始菜单）、`SetWindowsHookEx`（仅菜单期间） |
| `kernel32.dll` | `GetCurrentProcess`（查令牌） |
| `advapi32.dll` | `OpenProcessToken` / `GetTokenInformation`（只读查询完整性级别） |

**没有**：`SendInput` 键盘注入、`WriteProcessMemory`、`CreateRemoteThread`、驱动加载、全局键盘钩子、屏幕抓取、剪贴板读取。

### 2.2 鼠标钩子的边界（唯一"敏感"能力）

`Native.BeginOutsideClickWatch()` 安装 `WH_MOUSE_LL`，目的只有一个：右键菜单打开时，**点到菜单外面就把菜单关掉**（并吞掉那一次点击，和真正的菜单行为一致）。

- 只在菜单打开期间安装，菜单一关就 `UnhookWindowsHookEx` 解除（`EndOutsideClickWatch()`）。
- 钩的是鼠标、不是键盘；回调里只做坐标命中判断，不记录、不落盘、不外发。

### 2.3 不可信输入的处理

- 第三方 JSON / LRC 用自写解析器（`J`、`Lrc`），只做字符串切分与正则，**没有 `eval`、没有反射实例化、没有脚本引擎**。
- `XamlReader.Parse` 只解析程序内置的常量字符串（滚动条样式），不接受任何外部输入，无 XAML 注入面。
- `IsGhost()` 只读枚举进程名，不打开句柄、不注入。
- 卸载只删「自己的 Run 键 + 自己的 data 目录 + 自己的 exe」，有二次确认，`--silent` 也要显式带上。

### 2.4 可能的薄弱点（如实记录）

1. 搜歌/取词走明文 HTTPS，但没有证书固定（HPKP/pinning）；被中间人只能替换歌词文本，影响面限于显示。
2. 歌词文本直接进 `TextBlock.Text`，不是富文本，无注入风险；但超长行会被截断显示（功能问题，不是安全问题）。
3. 未做 Authenticode 签名 → 首次运行会被 SmartScreen 提示"未知发布者"。

---

## 三、与其它项目的相似度 / 许可证

### 3.1 依赖面

- **零第三方库**：只引用系统自带的 .NET Framework 4.x 和 Windows 的 WinRT 元数据（`Windows.Media.Control` 等 `.winmd`，编译期引用，**发行包里没有**）。
- `libs\sdkcontracts.nupkg` 是 Microsoft 的 SDK contracts 包（开发用），**不进发行包**。
- 发行包 `dist\Lyricard-v0.1.1.zip` 实含 5 个文件：`INSTALL.md`、`Lyricard.exe`、`Lyricard.lnk`、`Make shortcut.cmd`、`README.md` —— 没有 dll、没有 nupkg、没有 winmd、没有源码。

### 3.2 是"抄"还是"自己写"

- 全源码 grep `GPL|LGPL|AGPL|Apache|copyright|Lyricify|adapted|ported|based on` → **无命中**，没有任何拷贝进来的代码块或署名片段。
- 与其它项目"撞"的只有**公开 API 的用法**：
  - `GlobalSystemMediaTransportControlsSessionManager`（微软文档的标准 SMTC 用法）；
  - `IAudioMeterInformation`（COM 标准接口，查峰值电平）；
  - LRC 时间戳正则 `\[\d{1,3}:\d{1,2}(?:[.:]\d{1,3})?\]`（通行写法，人人一样）。
  这三类属于"事实/接口用法"，不是受版权保护的表达。
- 反过来，本项目的特色部分都是自己写的：SMTC 锚点 + 自走时钟的三层同步、`IsGhost()` 幽灵会话判定、`FitTitle` 量宽截断、封面逐像素调色、`ThinScrollBarXaml`、`icon.cs` 生成 ICO。

### 3.3 需要澄清的一个"授权"风险（不是代码的）

网易云 / QQ 音乐的搜索与歌词接口是**非官方接口**，不是公开 API 合同。代码本身没有授权问题，但**服务条款**上属于灰色地带：只读、无登录、无批量爬取、仅本地显示，风险等级低；README 已经写明"第三方非官方接口，仅供个人同步显示"。

---

## 四、建议（留待维护者决定）

1. **加个 LICENSE**：目前仓库里没有 LICENSE 文件。要公开分发建议补一份 MIT（与本项目"零依赖自写"的性质最匹配）。
2. **加 Authenticode 签名**（哪怕自签名证书），能少一次 SmartScreen 拦截提示；不加也能用。
3. **日志脱敏开关**：现在 `amc.log` 会记歌名与艺人（本机文件）。如果你打算把日志贴到网上求助，我可以加一个 `LogTitles=False` 的开关，把 `t=` / `a=` 换成 `***`。

---

## 五、第二轮审计（2026-11-23 17:40，针对「时间同步」这一批新代码）

范围：`src\NetEaseQueue.cs`（新增）、`src\AudioPeak.cs`（新增按进程计量）、`src\Players.cs`（绑定/检测）、`src\Smtc.cs`（音量纠正、幽灵会话、位置信任）、`src\CardWindow.cs`（播放按钮与时钟）。

### 1. 隐私：结论不变，但多了一处「读别的程序的本地文件」

- **新增读取**：`%LOCALAPPDATA%\NetEase\CloudMusic\webdata\file\playingList`（`NetEaseQueue.cs:29-40`）。
  只读、只取 `track.name / track.artists / track.duration` 用来补「总时长」，内容不落盘、不出网、不进日志（日志只写匹配到的歌名，见下）。
- **新增读取**：默认音频设备的 WASAPI 会话枚举与峰值（`AudioPeak.cs`），只读音量表，不录音、不抓音频数据。
- **网络**：仍然只有 `Lyrics.cs:943` 一处 `HttpWebRequest`；目标仍是 5 个公开歌词接口（music.163.com 两个、c.y.qq.com 两个、lrclib.net 一个），无 Cookie、无登录态、无遥测上报。全文 grep `Socket/UdpClient/WebClient` 无命中。
- **加密/混淆**：唯一命中是 `Lyrics.cs:235` 的 `MD5`，用途是把「歌名|艺人|时长」哈希成缓存文件名，属本地命名，不涉及账号或传输。
- **日志**：`amc.log` 会写歌名/艺人（`fetch start t=… a=…`、`netease dur from queue … <歌名>`、`pos untrusted t=…`）与 `transport play/pause`。全部在本机 `data\` 下；发行 zip 不含 data 目录。

### 2. 权限与系统改动

| 面 | 状态 |
| --- | --- |
| 提权 | `src\app.manifest` 无 `requestedExecutionLevel` → asInvoker，从不请求管理员 |
| 注册表写 | 仅 `HKCU\…\Run`（开机自启，`Native.cs:207/218/230`） |
| 注册表读 | `HKLM/HKCU\…\App Paths` 与开始菜单快捷方式（`Players.cs:237-269`），只读 |
| 全局钩子 | `SetWindowsHookEx(WH_MOUSE_LL)`（`Native.cs:55/105`）仅在菜单展开期间安装，用于「点菜单外关闭」，只读坐标、不拦截不改写，`EndOutsideClickWatch()` 解除 |
| 进程查询 | `Process.GetProcessesByName`（幽灵会话/按进程音量），只取进程名 |

### 3. 相似度与许可证

- 全树 grep `GPL|LGPL|Apache License|copyright (c)|ported from|adapted from|Lyricify|based on` → **0 命中**（源码与文档）。
- 无 vendored 二进制：源码树只有 `.cs`、`.sh`、`.md`、`.ico`、`.manifest`；发行 zip 实含 5 个文件（`INSTALL.md / Lyricard.exe / Lyricard.lnk / Make shortcut.cmd / README.md`），不含 dll / winmd / pdb。
- 引用的 winmd 仅在编译期由 `build.sh:38-39` 指向本机 `libs/pkg`，不随包分发。
- P/Invoke 声明（user32/kernel32/advapi32/ole32）是公开 Win32 API 的签名，不含他人实现代码；COM 接口 vtable 顺序来自微软公开头文件。
- 代码与 UI 文案为原创；此前已确认 `exe` 与文档 grep 本机路径/私人称谓命中 0（本轮复测仍 0）。

### 4. 结论

- 隐私外泄：**无**（无账号、无 Cookie、无遥测、无上传）。
- 越权行为：**无**（asInvoker，仅 HKCU 写入）。
- 第三方/GPL 代码：**无**。
- 需要在文档里讲清楚的：①会读网易云本地队列文件；②日志含歌名；③会枚举本机音频会话音量。三条都已写进 `README.md`「已知限制」。

### 5. 本轮新发现的小事

- 工作区里有测试残留 `bin/t.dll`（按进程音量的验证程序编出来的），不随包发布，可删。
- `src\smoke.exe` 是自测产物，同上。

## 六、第三轮审计（2026-11-24，自更新）

新增 `src/Updater.cs`（约 270 行）后重新核对网络、写入与权限三件事。

**新增的网络请求（唯一一处）**

- `GET https://api.github.com/repos/Liangpige/Lyricard/releases/latest`，公开只读接口，匿名（不登录、无 token）。
- 请求头只有 `User-Agent: Lyricard-Updater (+https://github.com/Liangpige/Lyricard)`、`Accept: application/vnd.github+json`；不带账号、不带机器指纹、不带任何自定义 ID。
- 触发时机只有两个：启动时一次静默检查（可在「关于」页关闭）、用户点「检查更新」；**没有任何轮询定时器**。匿名 GitHub API 上限 60 次/小时，正常使用远低于此。
- 沿用系统代理（`WebRequest.DefaultWebProxy`），与浏览器行为一致；不绕过、不硬编码代理。
- 下载走 `browser_download_url`（GitHub 资源，非我方服务器），下载完成后按 Release 元数据里的 `digest`（sha256）校验，不符即删除临时文件并报错。

**新增的文件写入**

- `%TEMP%\Lyricard-updater\`：临时目录，放下载的 zip 与解包出的 `Lyricard.exe`，完成或失败后删除。
- 程序目录：`Lyricard.exe` → `Lyricard.exe.old` 改名，新 exe 移入，随后删除 `.old`；失败时写 `update-failed.txt`（仅含异常文本）。这些都在用户自己的安装目录里，不触碰系统目录。
- 不写注册表（`HKCU\...\Run` 的开机自启逻辑与之前相同，仅在用户勾选时写）。

**权限**

- `--apply-update` 由本进程以同样权限启动自己（`UseShellExecute=false`），不请求提权；`app.manifest` 仍是 asInvoker。
- 未做代码签名（自知之明：这会触发 SmartScreen 提示，已在 README「更新」一节写明）。

**结论**：新增代码没有引入遥测、没有第三方库、没有新的系统级权限；唯一的对外请求是公开的 GitHub Release 查询，唯一的写入是用户安装目录与临时目录。
