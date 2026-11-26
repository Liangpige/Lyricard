# Lyricard v0.2.7 · 更新按钮能用了

## 报错：「检查失败：请求被终止，未能创建 SSL/TLS 安全通道」

这不是网络问题，是 **.NET Framework 默认只协商到 TLS 1.0**，而 GitHub 的 API 早已只接受 TLS 1.2 及以上——握手阶段就被拒，所以「检查更新」必然失败。

**改法**：新增 `src\Net.cs`，在第一次发请求前把进程级的 `ServicePointManager.SecurityProtocol` 按位补上 TLS 1.2。

```csharp
ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; // Tls12
```

写数值 `3072` 而不是 `SecurityProtocolType.Tls12`，是因为本工程用 .NET 4.0 的引用程序集编译（单文件、无运行时依赖），那个枚举值在当时还不存在。

## 顺带修好歌词接口

`https://lrclib.net` 和 `music.163.com` 之前也在报同样的 SSL 失败（日志里成片的 `http fail WebException`），现在和更新检查共用同一份 TLS 初始化。中文歌本来就优先走 QQ，所以这个问题只在英文歌、冷门曲目上暴露得明显。

## 新增排查开关

```
Lyricard.exe --updcheck
up to date (v0.2.7)
# 或者
update v0.2.7 asset=Lyricard-v0.2.7.zip size=...
# 失败时会打印异常类型、消息和内层异常
```

排查「连不上 GitHub」和「GitHub 返回格式变了」不用再开 GUI 点按钮。

## 升级

退出托盘里的 Lyricard，解压 `Lyricard-v0.2.7.zip` 覆盖 `Lyricard.exe`，`data\` 保留；或用「关于」页的「检查更新」。

- `Lyricard.exe` — SHA256 `a20d295e7acace7467cea971969966fd8c316614b63611b4d490f9a87fc7c54c`
- `Lyricard-v0.2.7.zip` — SHA256 `317f17e4736b695cfd1d68be0c284ddc6b872076cb194d1c33895a484564813d`

## 已知限制

不变：AIMP 不发系统媒体控制、Spotify 需登录后才有会话、浏览器标签不算播放器、总长未知时不走进度条。
