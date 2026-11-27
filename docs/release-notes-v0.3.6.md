# Lyricard v0.3.6 — 全屏隐藏不再把进程带走

这是一版单点修复：**「有窗口全屏时隐藏卡片」开关打开后会崩溃，而且开了就再也启动不起来。**

## 现象

- 打开开关后，任意窗口进入全屏（F11 浏览器、无边框全屏游戏、视频全屏），卡片有很高概率直接消失——不是收起，是进程没了。
- 之后每次双击启动，一秒左右又崩，看起来像「插件坏了」。
- 事件日志里三次崩溃签名完全一致：

```
Faulting module name: clr.dll, version: 4.8.9337.0
Exception code: 0xc0000005
Fault offset: 0x0000000000162557
.NET Runtime: 由于 .NET 运行时中出现内部错误，进程终止，退出代码 80131506
```

`0xC0000005` 落在 `clr.dll` 内部、退出码 `80131506`（`COR_E_EXECUTIONENGINE`）意味着 CLR 自身状态已被破坏：托管 `try/catch` 接不住，日志里也不会留栈。

## 根因

`src\FullscreenWatch.cs` 把 Win32 的这段声明写错了：

```csharp
[DllImport("shell32.dll")]
public static extern int SHQueryUserNotificationState();   // 错的
```

真实签名是 **`HRESULT SHQueryUserNotificationState(QUERY_USER_NOTIFICATION_STATE *pquns)`**——状态是从**输出指针**写回去的，函数本身只返回 HRESULT。

声明里没有参数，shell 就把那个枚举写进 **RCX 当前指向的任意地址**（寄存器作为入参未初始化）。于是每做一次全屏检测，就往随机堆地址写 4 个字节，CLR 堆被逐步写坏，几十毫秒后 GC/JIT 摸到坏块，进程在 `clr.dll` 里挂掉。

这也解释了两处看似不同的现象：

- **为什么是「全屏时」崩**：矩形判定不成立时才走到 `SHQueryUserNotificationState`，平时桌面状态下它也会被调用——只是当时写坏的位置恰好还没被用到。
- **为什么重启也崩**：`config.ini` 的 `HideFullscreen=True` 是持久化的，启动 9 个 tick（约 300 ms）后监视器立刻又跑一遍，于是每次都重复同一处破坏。实测旧二进制带这个配置启动，**约 1 秒必死**，日志停在 `fetch start`。

## 修复（提交 `bbf7821`）

```csharp
// HRESULT SHQueryUserNotificationState(QUERY_USER_NOTIFICATION_STATE *pquns)
// —— 它要一个输出指针，不返回状态本身。
[DllImport("shell32.dll")]
public static extern int SHQueryUserNotificationState(out int state);
...
int state; int hr = FullscreenNative.SHQueryUserNotificationState(out state);
if (hr != 0) return false;
```

顺带把 `GetMonitorInfo` 固定为 `EntryPoint = "GetMonitorInfoW", CharSet.Unicode`（原先是 `CharSet.Auto`，这个函数 W/A 变体结构相同、并未出错，只是不再留歧义）。

另加一条**默认关闭**的追踪开关：设环境变量 `AMC_FS_TRACE=1` 才把每次检测的 `fg / rect / moninfo / shqns` 写进日志。这次正是靠它在「所有调用都返回成功」的表象下定位到那一行；正常使用不会有这些日志。

## 顺带修的第二个问题：桌面也被当成全屏

修好崩溃之后立刻发现一个一直藏着的误判：**桌面（`Progman`）本身也是「铺满显示器的窗口」**。点一下壁纸、关掉最后一个窗口、或用 Alt+Tab 回到桌面时，前台窗口就是它，于是卡片会被当成「有应用全屏了」收起来——崩溃修好之后这个误判才显形。

`FullscreenWatch` 现在先做一步排除：前台窗口的类名如果是 `Progman` / `WorkerW` / `Shell_TrayWnd` / `Shell_SecondaryTrayWnd` / `MultitaskingViewFrame` 等 shell 表面，直接判定「不是全屏应用」。

## 验证

| 场景 | 修复前 | 修复后 |
|---|---|---|
| 开关打开，停在桌面 | 启动约 1 秒进程消失 | 持续存活，`fs shqns hr=0 state=5` |
| 打开一个无边框全屏窗口 | 崩溃 | `fullscreen hide=on`，卡片收起，进程存活 |
| 关闭该窗口 | — | `fullscreen hide=off`，卡片回来，进程存活 |
| 桌面获得焦点（点壁纸 / 关掉最后一个窗口） | 卡片被收起 | 不收，日志 `fs fg is shell/desktop` |
| `AMC_FS_TRACE` 未设置 | — | 日志里没有任何 `fs ` 行 |

## 如果你已经被这个问题卡住

升级到 v0.3.6 即可；不需要手动改配置。如果你在旧版本上遇到「一启动就崩」，也可以临时把 `data\config.ini` 里的 `HideFullscreen=True` 改成 `False` 先启动起来，再升级。
