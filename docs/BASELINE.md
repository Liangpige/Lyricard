# Step 0 · 基线固化（pre-modular）

> 目的：把「现在能用的样子」冻结成可复现的证据。S1–S8 每一步都拿这里的东西对比，**对不上就不许进下一步**。
> 冻结日期：2026-11-26 · 版本：v0.2.7 · tag：`pre-modular`

---

## 1. 三件套

| 证据 | 位置 | 说明 |
| --- | --- | --- |
| 默认卡片截图（三档缩放） | `docs/shots/1x-card.png`、`055x-card.png`、`22x-card.png` | 1× / 0.55× / 2.2×；用 `PrintWindow(PW_RENDERFULLCONTENT)` 直接渲染卡窗，任何窗口都盖不住它。卡片是透明底，所以底子是全黑 |
| 旧配置样例 | `docs/baseline/config-pre-modular.ini` | 冻结时的真实 `config.ini`（含所有外观键与绑定播放器） |
| 源码快照 | git tag `pre-modular` | 任何一步出问题都能 `git diff pre-modular -- src/` 看总账 |

**复现一条命令**（`tools/baseline-shot.ps1` 会改 `Scale=`、重启、按进程取最大可见窗口截图）：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\baseline-shot.ps1 -Scale 1.0  -Name 1x
powershell -NoProfile -ExecutionPolicy Bypass -File tools\baseline-shot.ps1 -Scale 0.55 -Name 055x
powershell -NoProfile -ExecutionPolicy Bypass -File tools\baseline-shot.ps1 -Scale 2.2  -Name 22x
```

> 前置条件：**必须有播放器会话**（卡片在无播放器时是隐藏的，这是设计），并且正在放同一首歌——否则截图内容无法逐像素对比。
> 截完记得把 `config.ini` 的 `Scale` 改回原值（基线是 `0.79`），并重启。

### 1.1 截图时的确切状态（复现必须一致）

- 时间：2026-11-26 12:26，版本 v0.2.7，DPI 125%（`wa=0,0,2048,1104 dpi=1.25`）
- 曲目：**Rising Tide — Jack Wall & Big Giant Circles**（Call of Duty: Black Ops Cold War OST），`dur=201`，**暂停**在 `pos=16.35`
- 歌词：尚未取到，卡片显示固定串 `♪ Waiting for music`——**这反而是好事**：文案固定，三档之间除缩放外没有任何变量
- 截图时临时把卡片停在桌面左下（`Left=40, Top=620`）再重启，拍完已还原成 `Left=1637.6 / Top=0 / Scale=0.79`
- 窗口 X 平时由 `Config.Clamp` 按宽度贴边（1× `left=1528.8`、0.55× `left=1637.6`、2.2× `left=907.2`），Y 恒为 0

### 1.2 布局数值基线（比截图更硬的判据）

`layout` 行是**缩放前的 DIP 值**，与 `Scale` 无关，所以三档完全一致——任何一步改了排版，这一行必然变：

```
title=19.7 lyric=19.1 bottom=30.0 art=96.0 barW=320.0 colW=320.0
edgeT=edgeL=edgeB=edgeC=2368.0 (1×) / 2298.0 (0.55×) / 2140.0 (2.2×)
edgeTL=2147.0 / 2177.0 / 1653.0   align=right
leftT=2147.0 leftL=2200.0 leftC=2252.0 btnW=96.0 leftB=1982.0   (1×)
bar track y=265.0 h=3.8 | fill y=265.0 h=3.8 w=0.0
```

回归时：同一条歌、同一状态 → `grep "layout title=" amc.log` 的 `title/lyric/bottom/art/barW/colW/btnW` 必须**逐字相同**，`edge*`/`left*` 只允许因窗口贴边位置变化而整体平移。

---

## 2. 行为清单（后续每一步逐条勾）

图例：**代码依据**= 现在由哪段实现；**回归做法**= 怎么验；**期望**= 什么样算对。

### 2.1 交互穿透（原「锁定位置」）

- 代码依据：`src/CardWindow.cs` `SetClickThrough` / `ApplyClickThrough`（`WS_EX_TRANSPARENT = 0x20`）、`src/Config.cs` 键 `ClickThrough`。
- 回归做法：主页开关打开 → 点卡片应落到下方窗口；看日志 `click-through on/off ex=0x…`（含 `0x20` = 开）。
- 期望：开启后卡片不拖、不弹菜单、不响应 seek；关闭后全部恢复；`config.ini` 落盘 `ClickThrough=True/False`。
- 注意：卡片是 layered window，`WindowFromPoint` 受 alpha 影响，**不能**拿它当判据，只看 ex-style。

### 2.2 拖拽

- 代码依据：`OnCardMouseDown` → `DragMove()`，`if (_cfg.ClickThrough) return;` 挡住穿透态。
- 回归做法：按住卡片文字区/封面拖动 → 松手后 `config.ini` 的 `Left`/`Top` 变化，重启后位置保持。
- 期望：拖动跟手、无残影；穿透开启时完全不能拖。

### 2.3 缩放

- 代码依据：右键菜单「放大 / 缩小」→ `Config.Scale`（步长 0.05，范围约 0.55–2.2，`Config.Clamp` 钳制）；布局按 `CardW 470 × CardH 120` 乘 `Scale`。
- 回归做法：连点放大 3 次 / 缩小 3 次，看卡片尺寸与字重变化；重启后缩放保持。
- 期望：三档截图（1× / 0.55× / 2.2×）与基线**逐像素**一致（同 DPI、同一首歌、同一位置）。

### 2.4 进度条点击 seek

- 代码依据：`src/CardWindow.cs:1010` `_smtc.Seek(frac * np.Dur.TotalSeconds);`
- 回归做法：点进度条 25% / 75% 处，看日志 `seek` 与播放器跳转；位置在 1–2 拍内对齐。
- 期望：卡片进度立即跳、歌词行随之跳到对应行；未知总长（`Dur < 1`）时不响应。

### 2.5 换歌

- 代码依据：`settle`（同 key 稳 400 ms）+ `_endHold`（曲末锁存 2.5 s 宽限）+ 900 ms 切歌冷却窗。
- 回归做法：播放器里点「下一曲」，以及**让一首歌自然播完**（曲末自动切歌是老 bug 现场）。
- 期望：旧标题 → Waiting → 新标题，中间不闪回旧歌词、不出现 `idx=-1` 抖动；封面不残留上一首。

### 2.6 歌词行数 / 三态变化

- 代码依据：卡片高度固定 `CardH 120`（单行歌词），三态文案 `♪ Waiting for music` / `♪ Instrumental · no lyrics` / `♪ No lyrics available` 由 `Lyrics.Sanitize` 归一。
- 回归做法：依次放「有词」「纯音乐」「无词」「前奏 18 秒」四类歌。
- 期望：只有文字内容变化，**布局零位移**（标题 / 艺人 / 进度条 / 按钮位置不动）；三态文案不出现中文与英文混排错误。

### 2.7 托盘

- 代码依据：`src/Tray.cs` `ShowMenu()`（WPF `ContextMenu` + `MenuTheme.Apply` + 光标锚定 `PlacementMode.AbsolutePoint`）；双击图标 = 打开主页面。
- 回归做法：托盘右键 → 与卡片右键并排看；双击 → 主页面出现且能关。
- 期望：两处菜单同一套深色圆角/分隔线/勾选；条目一致（托盘多「显示/隐藏」「打开主页面」「退出」）；**没有**「透明背景 / 封面旋转 / 监听所有播放器 / 卸载」。

---

## 3. 冻结时的既知事实（写下来免得以后误判成回归）

1. 卡片在**没有任何播放器会话**时是隐藏的（空桌面是设计，不是 bug）。
2. 未知总长（`Dur < 1`）时进度条保留外观但不填充。
3. 「最后一句 + 5 s」估时长只对**时间轴不可信**的播放器生效。
4. AIMP 不发 SMTC、Spotify 未登录时 `sessions=0`、浏览器不计入。
5. `config.ini` 里 `BoundPlayers=qqmusic,aimp,foobar,apple-music`（冻结时值）；**空值 = 谁都不监听**。
6. 版本 v0.2.7，exe 344,064 B sha256 `a20d295e…`。
