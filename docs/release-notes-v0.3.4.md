# v0.3.4 — 换专辑，封面跟着换

一句话：这一版把 issue #1 报的三层问题一次修完——**真封面被字节数当成占位图丢掉**、**补图把 A 歌配上 B 歌的专辑**、**同名换专辑时封面完全无感知**。

前两层是「取错了图」，第三层是「压根不取」，三者叠在一起，看起来就像「封面卡住了，重启就好了」。

## 一、6 KB 的尺寸门，把真封面拦下来了

`LooksLikePlaceholder` 的第一条判据原来是：

```csharp
// Kugou's tile is a four-and-a-bit kilobyte PNG; a real cover at this size never is.
if (bytes != null && bytes.Length > 0 && bytes.Length <= 6000) return true;
```

这句话对酷狗成立，对网易云**不成立**——它的正常封面就是 1.5–5.3 KB。于是日志里成片出现：

```
21:09:03.364  art ok 1511 bytes changed=True
21:09:03.394  art placeholder yes (small=1511)          ← 播放器给的图是好的，被我们丢了
21:09:06.506  cover fallback ok 9090 bytes mid=003PTZBu0IXqg2
```

现在改成两条：

- **≤512 字节**才是真垃圾（`junk=`）；
- **≤6 KB 且是 PNG** 仍然按占位盘处理（`png-tile=`）——酷狗的占位盘是 PNG，网易云的真封面是 JPEG。

之所以不直接删掉尺寸门：那会把酷狗的四种占位盘重新放回来。之所以不把尺寸门套在所有格式上：那正是这个 bug。占位图本体交给已有的像素判据（`grey` / `tinted` / `rot` / `edge`），它们只看画面，不看字节数。

## 二、补图把「下一首歌的专辑」配给了这首歌

QQ 的歌曲对象里，`albummid` 在最前、`songname` 在最后：

```
{"albumid":6208985,"albummid":"00130l800I5J5b","albumname":"If I Ain't Got You", … ,"songname":"If I Ain't Got You"}
```

而旧正则写的是 `"songname"…{0,4000}?"albummid"`——**永远跨到下一条 hit**。所以 `If I Ain't Got You / Alicia Keys` 取到的是下一首单曲的 mid，而不是正在播的那张专辑。

现在按 `albummid` 切分对象：两个 `albummid` 之间的跨度正好是一首歌，`songname` / `albumname` / `singer` 都从自己那块里读。评分也是新的：

```
标题必须命中（完全相等 1.0 / 互相包含 0.85）
同专辑        +2.0    ← 正在播的那张，压过一切
异专辑        −0.4    ← 同一首歌的另一张 release
艺人命中      +0.6
```

`Fetch(title, artist, album)`、缓存键含专辑、`--cover` 支持第三段。超时从 4 s / 5 s 提到 8 s——实测这个接口要 3.5 s，原来的阈值经常踩线。

## 三、换同名专辑，元数据一点都没变

封面身份原来是 `Aumid|Title`：

```csharp
string trackId = (np.Aumid ?? "") + "|" + (np.Title ?? "");
_artWatchUntil = changed ? DateTime.MinValue : DateTime.Now.AddSeconds(30);
```

网易云**压根不上报 `AlbumTitle`**，所以「单曲版 → 专辑版」这次切换，key 一个字都不变；而慢看在一次成功之后又被关成了 `MinValue`。两件事相乘：**缩略图一次都不重读**，封面永远停在上一张。

现在：

- 身份是 `Aumid|Title|Album`，上报专辑的播放器立刻能感知；
- 慢看**常驻**（成功之后不再关闭），`art late` 也会继续保持；
- 节奏做了节流：封面 ≤64 KB 每 1.5 s 看一次，更大的每 4 s——Apple Music 的封面有 288 KB，1.5 s 一遍等于每秒近 200 KB 的重复读取。

## 四、补图时机

联网补图的等待从 1.2 s 提到 **2.6 s**，排在 SMTC 的 2 s 定格窗之后。原来的时序会让补图抢在播放器自己的真图前面落地，先显示一张「长得像」的封面，再被真图顶掉。

## 验证

```
--cover "If I Ain't Got You|Alicia Keys|The Diary Of Alicia Keys (Expanded Edition)"
→ cover fallback ok 15179 bytes mid=001XseH30Vphgv      ← 整张专辑

--cover "If I Ain't Got You|Alicia Keys|If I Ain't Got You"
→ cover fallback ok 15588 bytes mid=00130l800I5J5b      ← 单曲版
```

同一首歌、两个 release、两个不同的 mid。288 KB 的真封面走的是 `placeholder no (rot=75.7 edge=13.3)`，没有被误判。

## 致谢

问题定位、复现步骤和补丁都来自 [@TiaoLia](https://github.com/TiaoLia)（issue #1）。尺寸门、`albummid` 切分、慢看常驻三条都出自他的分析；本版的两处偏离是：尺寸门保留 PNG 版本（否则酷狗占位盘回归）、慢看按封面体积分档（否则大封面每秒被读一遍）。

## 安装

下载 `Lyricard-v0.3.4.zip`，解压到任意目录（建议 `%LOCALAPPDATA%\Lyricard`），运行 `Lyricard.exe`。`Make shortcut.cmd` 会生成桌面快捷方式。已有用户直接覆盖 exe 即可，`data\` 不动。
