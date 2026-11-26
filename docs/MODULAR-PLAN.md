# Lyricard 模块化重写 · 计划与纪律

> 2026-11-26 主人定调：一步只做一件事、逐步验收、不发明产品决策、新逻辑进独立文件、纯 C# 5 / 无 XAML / 无依赖 / 单文件输出，并且**要为将来加元素、加模块、加自定义功能留口子**。
> 这份文档是「规矩 + 路线图」，每个迁移步骤都在这里登记验收标准；没登记的不许开工。

---

## 0. 现状盘点（2026-11-26 实测）

```
总行数 10,514
CardWindow.cs   2220   卡片：窗口/布局/时钟/歌词渲染/菜单/交互/外观应用
MainWindow.cs   1475   主页面：四页 UI + 控件工厂 + 动效原语 + 设置装配
Lyrics.cs       1194   取词：三源并行 + 匹配打分 + 缓存 + LRC 解析 + 导出
SettingsWindow.cs 846  旧设置面板（已退役，仅 CardWindow.OpenSettings 的兜底）
Smtc.cs          663   WinRT 会话读取 + 绑定 + 幽灵检测 + 位置可信度
Players.cs       483   播放器名单/检测/绑定
Program.cs       478   启动装配 + 全部调试开关
Config.cs        441   键值读写 + 钳制 + 预设
ColorLibraryWindow 287 色彩库
Strings.cs       276   中英字典
Updater.cs       274   自更新
Native.cs        258   P/Invoke
AudioPeak.cs     237   WASAPI 出声判断
Tray.cs          223   托盘 + 同一套菜单
CandidateWindow  215   歌词候选面板
Json.cs          194   迷你 JSON
MenuTheme.cs     183   菜单主题
其它             ~700  Uninstall / Shortcut / NetEaseQueue / Net / AmProbe / Smoke
```

**病灶**（不是「代码乱」，是**职责叠加**）：

1. `CardWindow` 一个类里同时有：窗口特效、布局、元素样式、位置时钟、歌词状态机、右键菜单、鼠标交互、外观应用、提示卡文案。任何新元素（音量条、频谱、评分、下一首预告）都只能往这个文件里塞。
2. `MainWindow` 同时是：窗口壳、导航、控件工厂、动效库、四个页面的业务。加一个新设置项要改这个文件。
3. `Config` 是所有键的平铺仓库 + 钳制 + 预设，新增一个键要在 4 个地方（字段/解析/写出/UI）手工同步。
4. 取词（`Lyrics`）把「provider 调用」「相似度匹配」「缓存」「LRC 解析」「导出」揉在一个文件里，加一个歌词源要改这个文件。

---

## 1. 目标分层

```
              ┌──────────────── App（装配层，只做接线）
              │  Program.cs  AppHost.cs
              ├──────────────── 表现层
              │  Card/  Main/  Tray/  Pickers/
              ├──────────────── 领域层
              │  Playback/  Lyrics/  Look/  Update/
              ├──────────────── 基础设施
              │  Core/  (Config/Net/Json/Log/Strings/Native/Anim)
              └────────────────
```

**铁律**：装配层不写实现；表现层不写业务；领域层不碰 `System.Windows.Controls`（只有 `Card/` 与 `Main/` 可以）。

### 1.1 文件清单（新增/拆分，全部独立文件）

**基础设施 `src/Core/`**

| 文件 | 职责 |
| --- | --- |
| `Core/Log.cs` | 现有 `LyricsService.Log` 抽出来，统一时间戳/滚动/敏感信息过滤 |
| `Core/Net.cs` | 已有（TLS 1.2 引导）；再收拢超时/UA/代理 |
| `Core/Json.cs` | 已有 |
| `Core/Strings.cs` | 已有 |
| `Core/Native.cs` | 已有 |
| `Core/Anim.cs` | 把 `MainWindow` 里的 `AnimateColor/AnimateOpacity/AnimateScale/PressScale/HoverFill` 抽成公共动效库 |
| `Core/Settings.cs` + `Core/SettingDef.cs` | **声明式设置**：每个模块声明 `key/type/default/min/max/label/group/onChange`，写读钳制与 UI 自动生成都由它驱动（见 2.1） |

**领域层 `src/Domain/`**

| 文件 | 职责 |
| --- | --- |
| `Domain/Playback/TrackState.cs` | 一次快照的纯数据（标题/艺人/专辑/AUMID/位置/总长/播放态/封面字节/可信度） |
| `Domain/Playback/PlaybackService.cs` | 现在的 `Smtc`：会话发现、绑定、幽灵、位置可信度 |
| `Domain/Playback/PlayerRegistry.cs` | 现在的 `Players`：名单、检测、绑定 |
| `Domain/Playback/PositionClock.cs` | **把 `CardWindow.Tick` 里的位置数学整段搬出来**：锚点/自走/纠偏/曲末锁存/切歌冷却 |
| `Domain/Playback/AudioProbe.cs` | 现有 `AudioPeak` |
| `Domain/Lyrics/ILyricsProvider.cs` | 歌词源接口：`Name / Priority(id) / Search(title,artist,dur) → candidates` |
| `Domain/Lyrics/Providers/QqProvider.cs`、`NeteaseProvider.cs`、`LrclibProvider.cs`、`LocalFileProvider.cs` | 每个源一个文件 |
| `Domain/Lyrics/LyricsMatcher.cs` | 纯函数：`Similar / StripDecorations / Score / Accept` |
| `Domain/Lyrics/LyricsCache.cs` | 缓存读写、导出、自动清理 |
| `Domain/Lyrics/LrcParser.cs` | LRC 解析 + `♪` 三态归一化 |
| `Domain/Lyrics/LyricsService.cs` | 编排：并行、超时、翻盘宽限、手动换源 |
| `Domain/Look/LookConfig.cs` + `LookPresets.cs` | 外观键 + 预设（从 `Config` 抽出） |
| `Domain/Update/Updater.cs` | 已有 |

**表现层**

| 文件 | 职责 |
| --- | --- |
| `Card/CardWindow.cs` | **只做装配**：建窗口、订阅服务、把元素挂进 `CardLayout`、转发菜单/交互 |
| `Card/CardLayout.cs` | 声明式布局引擎：元素声明区域/对齐/优先级，负责测量与冲突处理 |
| `Card/ICardElement.cs` | 元素接口：`Id / DefaultVisible / Build() / Update(TrackState, LookState) / ApplyLook()` |
| `Card/Elements/TitleElement.cs`、`ArtistElement.cs`、`LyricElement.cs`、`ProgressElement.cs`、`ControlsElement.cs`、`ArtworkElement.cs`、`HintElement.cs` | 现有七块视觉拆成七个元素，一行注册即生效 |
| `Card/CardMenu.cs` | 右键菜单 |
| `Main/MainWindow.cs` | 只做：窗口壳 + 导航 + 切页 |
| `Main/PageHost.cs` | 页面容器与转场 |
| `Main/Widgets.cs` | `Toggle/Pill/Segmented/Stepper/ColorRow/Rule/Section/Row/Hint`（从 MainWindow 抽出） |
| `Main/Pages/HomePage.cs`、`LookPage.cs`、`LyricsPage.cs`、`AboutPage.cs` | 四页各自独立 |
| `Tray/Tray.cs`、`Tray/MenuTheme.cs` | 已有，收敛命名空间 |
| `Pickers/CandidateWindow.cs`、`Pickers/ColorLibraryWindow.cs` | 已有 |

---

## 2. 为「以后再加东西」留的四个口子

### 2.1 声明式设置（加一个开关 = 一个 `SettingDef` + 一行注册）

```csharp
Settings.Register(new SettingDef {
    Key = "ShowBitrate", Group = "card.meta", Type = SettingType.Toggle,
    Default = false, Label = "show.bitrate",
    Apply = delegate(object v) { card.SetBitrateVisible((bool)v); }
});
```

- 「外观」页按 `Group` 自动长出行，**不用改 `MainWindow`**；
- `config.ini` 的读写/钳制/默认值由 `Settings` 统一生成，不再手写 4 处；
- 预设胶囊按「属于外观组的键」自动收集，新增键自动进预设。

### 2.2 卡片元素注册表（加一个元素 = 一个文件 + 一行注册）

```csharp
CardElements.Register(new TitleElement());   // 装配层一行
```

- 布局：元素声明 `Slot`（`Top/Left/Bottom/Right/Overlay`）+ `Order` + `Grow`，`CardLayout` 负责排；默认模板等价于今天的手工坐标，**截图必须逐像素回得来**；
- 元素只拿到 `TrackState` + `LookState`，不碰窗口、不碰时钟；
- 新元素想加设置项 → 在它的 `Build()` 里 `Settings.Register(...)`（2.1）。

### 2.3 歌词源注册表（加一个源 = 一个文件 + 一行注册）

```csharp
LyricsService.Register(new KugouProvider());
```
- 排序规则仍由「歌名是否含 CJK」决定，provider 只声明自己的 `Priority(cjk)`；
- 匹配与门槛是公共纯函数，源不参与打分。

### 2.4 播放器适配（加一家 = 一个 `PlayerDef`）

`PlayerRegistry` 里一条 `PlayerDef { Key, Display, AumidHints[], ProcessHints[], KnownLimitations }`，`IsLaggyTimeline` 也从散落的判断变成 `PlayerDef.Timeline = Coarse`。

---

## 3. 迁移步骤（每步单独验收，不通过不进下一步）

| 步 | 内容 | 产出 | 验收（缺一不可） |
| --- | --- | --- | --- |
| **S0** | 冻结基线 | `docs/BASELINE.md`（旧交互清单 40 条：卡片 20 / 主页面 12 / 托盘 8）+ `docs/shots/*.png`（默认模板、空闲、无词三态、菜单） | 清单逐条人工过一遍并勾选；截图存档 |
| **S1** | 配置模型：`src\CardConfig.cs`（`CardConfig` / `ModuleConfig` / `CardSchema`，模块 Cover·Title·Artist·Album·Lyrics·Progress，字段 Enabled·X·Y·W·H·FontSize·Color·Align·ZIndex，H=-1=Auto），只有序列化/反序列化，**不接 UI** | 1 个新文件 + `tools\cfg-roundtrip.cs`（22 条断言） | ✅ 已完成 2026-11-26：`csc exit=0 warnings=0`；`tools\run-cfgtest.cmd` 22/22 PASS（往返幂等、字节级二次稳定、auto/NaN 语义、未知段与未知键忽略、坏值回落、深拷贝）；启动日志无异常 |
| **S2** | 位置时钟独立 | `Domain/Playback/PositionClock.cs`，`CardWindow.Tick` 只调它 | 连续跟歌 30 分钟无回抽；曲末锁存仍生效（用「侧脸」这类歌验）；截图一致 |
| **S3** | 布局引擎：`src\LayoutEngine.cs`（Auto 高度、模块 Rect、包围盒 Union、cardW/H = bounds + Padding×2、平移归零、`LogicalToScreen`/`ScreenToLogical` 统一处理 Scale），只有计算，不接渲染 | 1 个新文件 + `tools\layout-selftest.cs`（22 条断言） | ✅ 已完成 2026-11-26：`csc exit=0 warnings=0`；默认模板输出 `card 470x122`、`Title 0,21.117 320x19.701`、`Lyrics 0,44.818 320x19.065`、`Progress 0,71.883 320x3`、`Cover 340,0 96x96`，与日志 `title=19.7 / lyric=19.1 / barW=320 / colW=320 / art=96` 及窗口实测 648×213 设备像素（=470.4×122.4 DIP）一致；换算往返 4 缩放 × 3 DPI 全等 |
| **S4** | 主页面拆分 | `Main/Widgets.cs` + 四个 Page 文件 | 四页截图与 S0 一致；每个开关点一下仍即时生效并落盘 |
| **S5** | 外观页改为 Settings 驱动 | `Main/Pages/LookPage.cs` 由 `Settings` 生成 | 26 个外观键**全部**仍可调、预设仍能存/套/删；截图与 S0 一致 |
| **S6** | 歌词分层 | 4 个 provider + matcher + cache + parser + service | 三种语言各测 3 首：命中率与 S0 日志对照不下降；`--lyrtest` 输出格式不变 |
| **S7** | 删除 `SettingsWindow.cs` | 删 846 行 | `--settings` 仍开「外观」页；无任何入口再引用它；`warnings=0` |
| **S8** | 收尾 | `docs/ARCHITECTURE.md` 更新、README 的「源码结构」一节、AGENTS.md 补一句「新功能加在哪」 | 全量回归 S0 清单 |

每步的固定动作：`bash build.sh` → `csc exit=0 warnings=0` → 启动跟歌 → 截图 → 勾清单 → 提交（提交信息写清「S? 步 + 做了/没做什么」）。

---

## 4. 待主人决策（不定就不开工）

**A. 卡片布局：声明式还是固定槽位？**

- A1（推荐）**声明式**：元素声明槽位/顺序，`CardLayout` 排版。默认模板坐标写死成一组 `Slot/Order`，截图能逐像素还原；将来加「音量条」这种东西只需要在 `Bottom` 槽里插一条，不用改别的元素。
- A2 **固定槽位**：`Title/Artist/Lyric/Bar/Controls/Art` 六个位置写死，新元素只能挂在预留的空位。
  - 好处：改动更小、风险更低；坏处：加元素时容易再一次「挤进去」。

**B. 外观页：自动生成还是继续手写？**

- B1（推荐）**Settings 自动生成**：分组、排序、标签、步进区间全部来自 `SettingDef`；新增设置项零改动长出来。
- B2 **手写页 + 模块各自注册一行**：保留今天的手写排版，新项自己 `AddRow`。
  - 好处：视觉完全可控；坏处：每个模块都要懂页面布局，「一行注册」的承诺打折。

**C. 第一步的范围**

- C1（推荐）**S0 基线 + S1 地基**：先立规矩和截图基线，再做 `Core/Settings`，风险最低。
- C2 直接 **S3 卡片元素化**：见效最快，但没有基线和 Settings 撑着，后面每一步都要手工对间距。

**D. 旧 `SettingsWindow.cs`（846 行）**

- D1（推荐）**S7 删掉**：它的入口早就改成「外观」页，留着只是 catch 兜底。
- D2 保留：万一新页崩了还能开旧面板（但它本身也在引用旧结构，重写后未必编得过）。

---

## 5. 不变的东西（重写期间的红线）

1. 单文件绿色、纯 C# 5、无 XAML、无第三方库、`csc` 直接编。
2. `config.ini` 的**旧键必须原样能读**；新键只能在 `Settings` 里新增，不许改名。
3. 歌词三态文案、菜单文案、快捷键（Esc 关面板、托盘双击开主页）不变。
4. 不主动发版（主人明说才发）。
5. 每一步都必须可回滚：一个步骤一个 commit，`git revert` 能单独撤。
