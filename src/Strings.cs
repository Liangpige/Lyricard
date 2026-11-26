// ---------------------------------------------------------------------------
// Strings.cs
//
/// Every user-visible string, in Chinese and English, behind one lookup. Language is
/// resolved once at startup (auto follows the Windows UI language).
// ---------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Amc
{
    /// <summary>
    /// All user-visible interface text lives here. Adding a language means adding one
    /// dictionary below - nothing else in the UI has to change.
    /// The three lyric placeholder lines are NOT in here: they are intentionally always
    /// English ("Waiting for music" style), see CardWindow.TxtWait/TxtInstr/TxtNone.
    /// </summary>
    public static class Strings
    {
        public const int LangAuto = 0;
        public const int LangZh = 1;
        public const int LangEn = 2;

        public static int Preference = LangAuto;   // what the user picked
        public static int Current = LangZh;        // resolved, never Auto

        static readonly Dictionary<string, string> Zh = new Dictionary<string, string>();
        static readonly Dictionary<string, string> En = new Dictionary<string, string>();

        static void Z(string k, string v) { Zh[k] = v; }
        static void E(string k, string v) { En[k] = v; }
        static void Both(string k, string zh, string en) { Z(k, zh); E(k, en); }


        static Strings()
        {
            Both("app.title", "Lyricard", "Lyricard");
            Both("app.version", "v0.2.7", "v0.2.7");
            Both("app.tip", "Lyricard v0.2.7 · 桌面歌词卡片", "Lyricard v0.2.7 · desktop lyric card");

            Both("menu.showHide", "显示 / 隐藏", "Show / hide");
            Both("menu.settings", "个性化设置…", "Appearance…");
            Both("menu.main", "打开主页面", "Open main page");
            Both("nav.home", "主页", "Home");
            Both("nav.appearance", "外观", "Look");
            Both("nav.lyrics", "歌词", "Lyrics");
            Both("nav.about", "关于", "About");
            Both("nav.body.empty", "页面还是空的", "This page is still empty");
            Both("page.look.more", "更多设置…", "More settings…");
            Both("page.home.language", "界面语言", "Language");
            Both("page.home.clickthrough", "交互穿透（屏蔽卡片交互）", "Click-through (card ignores the mouse)");
            Both("page.home.clickthrough.hint",
                "打开后卡片不再接收任何鼠标操作，点在卡片上的动作会落到它下面的窗口；要关掉就从托盘菜单或这一页关。",
                "With this on the card stops taking mouse input -- clicks go to the window below it. Turn it off from the tray menu or this page.");
            Both("page.home.startup", "启动", "Startup");
            Both("page.home.autostart", "开机自动启动", "Start at logon");
            Both("page.home.autostart.hint",
                "自启时只驻留托盘：中控页面不会弹，桌面卡片也会等到播放器开始播放才出现。",
                "At logon Lyricard stays in the tray: no main window, and the desktop card only appears once a player starts.");
            // Only used when a *known* player is making sound without publishing a session --
            // i.e. its SMTC switch is off. Someone else's audio (a video, a game) never gets here.
            Both("idle.nosession.line", "♪ 没上报曲目，请开启系统媒体控制",
                 "♪ {0} is playing but publishes no track — turn on its SMTC switch");
            Both("idle.unbound.line", "♪ {0} 未绑定 — 在主页面里勾上它", "♪ {0} is not bound — tick it on the Home page");
            Both("page.home.players", "监听的播放器", "Players");
            Both("page.home.players.hint",
                "勾上的播放器才会驱动卡片；一个都不勾，卡片就不出现。浏览器不会自动加入名单。",
                "Only ticked players drive the card. Tick nothing and no card appears. Browsers are never added automatically.");
            Both("page.home.players.rescan", "重新扫描", "Rescan");
            Both("page.home.players.add", "添加其它播放器…", "Add another player…");
            Both("page.home.players.hide", "收起其它播放器", "Hide others");
            Both("page.home.players.running", "运行中", "running");
            Both("page.home.players.installed", "已安装", "installed");
            Both("page.home.players.none", "没找到已安装或正在运行的播放器", "No installed or running player found");
            Both("page.home.players.exe", "或输入程序名（如 spotify.exe）", "or type an executable name (e.g. spotify.exe)");
            Both("page.home.players.custom", "自定义", "custom");
            Both("page.look.hint", "改动立即生效", "Changes apply instantly");
            Both("page.look.scale", "整体缩放", "Scale");
            Both("page.look.opacity", "整体透明度", "Opacity");
            Both("page.look.lyricsize", "歌词字号", "Lyric size");
            Both("page.look.barthick", "进度条粗细", "Bar thickness");
            Both("page.look.clear", "透明背景（只留文字）", "Transparent background");
            Both("page.look.spin", "封面旋转", "Spin artwork");
            Both("page.look.shadow", "文字阴影（浅色壁纸）", "Text shadow (light wallpaper)");
            Both("page.lyr.title", "歌词来源", "Lyric sources");
            Both("page.lyr.order", "优先级：本地文件 → QQ → 网易云 → lrclib", "Order: local file → QQ → NetEase → lrclib");
            Both("page.lyr.cache", "缓存", "Cache");
            Both("page.lyr.cacheval", "首歌词，占用", "tracks, on disk");
            Both("page.lyr.export", "导出歌词…", "Export lyrics…");
            Both("page.lyr.clearcache", "清除歌词缓存", "Clear lyric cache");
            Both("page.lyr.opendir", "打开数据目录", "Open data folder");
            Both("page.lyr.cleared", "已清除", "Cleared");
            Both("page.lyr.confirm", "确定要清掉全部歌词缓存吗？下次播放会重新联网取词。", "Clear the whole lyric cache? They will be fetched again on next play.");
            Both("page.about.blurb", "Lyricard —— 浮在桌面上的 Apple Music 歌词卡片。", "Lyricard — an Apple Music lyric card that floats on your desktop.");
            Both("page.about.credits", "第三方：歌词来自 QQ 音乐 / 网易云音乐 / lrclib 的公开接口，非官方，仅供个人同步显示；图标自绘；字体使用系统字体。", "Third party: lyrics come from the public QQ Music / NetEase / lrclib endpoints — unofficial, for personal display only. Icon drawn in code, system fonts.");
            Both("page.about.shortcuts", "快捷键与入口：托盘双击打开主页面 · 右键卡片出菜单 · Esc 关闭当前面板", "Gestures: double-click the tray icon for this page · right-click the card for its menu · Esc closes the current panel");
            Both("page.about.license", "许可", "License");
            Both("sec.uninstall", "卸载", "Uninstall");
            Both("page.about.uninstall", "卸载会结束程序、删除 data\\（配置、预设与歌词缓存）以及 exe 本身。这些文件都在这个文件夹里，删掉不会再有第二次。", "Uninstalling stops the app, deletes data\\ (settings, presets and the lyric cache) and removes the exe itself. Everything lives in this folder, so there is no second chance.");
            Both("about.uninstall", "卸载 Lyricard…", "Uninstall Lyricard…");
            Both("menu.refetch", "重新获取歌词", "Fetch lyrics again");
            Both("menu.export", "导出歌词（分享用）", "Export lyrics (for sharing)");
            Both("menu.switchSrc", "换一个歌词源", "Try another source");
            Both("menu.pickLyric", "选择歌词…", "Pick lyrics…");
            Both("color.title", "颜色", "Colour");
            Both("color.hint", "下面是完整色库；也可以直接输 #RRGGBB", "Full palette below; a #RRGGBB value works too");
            Both("color.apply", "应用", "Apply");
            Both("color.reset", "恢复内置色", "Built-in");
            Both("color.bad", "颜色写错了，试试 #RRGGBB 或 #RGB", "Unreadable colour, try #RRGGBB or #RGB");
            Both("sec.update", "更新", "Updates");
            Both("upd.check", "检查更新", "Check for updates");
            Both("upd.checking", "正在检查…", "Checking…");
            Both("upd.latest", "已是最新 {0}", "Up to date ({0})");
            Both("upd.found", "发现新版 {0}（{1} KB）", "New version {0} ({1} KB)");
            Both("upd.download", "下载并安装", "Download & install");
            Both("upd.downloading", "正在下载 {0}%", "Downloading {0}%");
            Both("upd.ready", "下载完成，即将重启安装…", "Downloaded, restarting to install…");
            Both("upd.fail", "检查失败：{0}", "Update check failed: {0}");
            Both("upd.skip", "跳过这个版本", "Skip this version");
            Both("upd.skipped", "已跳过 {0}", "Skipped {0}");
            Both("upd.auto", "启动时检查更新", "Check for updates at startup");
            Both("page.lyr.timing", "时间轴微调", "Timing");
            Both("lyr.early05", "提前 0.5s", "-0.5s");
            Both("lyr.early01", "提前 0.1s", "-0.1s");
            Both("lyr.late01", "延后 0.1s", "+0.1s");
            Both("lyr.late05", "延后 0.5s", "+0.5s");
            Both("lyr.offset", "当前偏移：{0} 秒", "Current offset: {0}s");
            Both("page.lyr.autoclean", "定期清理缓存", "Scheduled cleanup");
            Both("page.lyr.autoclean.on", "按间隔自动清理", "Clean up automatically");
            Both("page.lyr.autoclean.days", "间隔（天）", "Every (days)");
            Both("page.lyr.autoclean.last", "上次清理：{0}", "Last cleaned: {0}");
            Both("page.lyr.autoclean.never", "还没清理过", "never yet");
            Both("page.lyr.autoclean.hint", "到期后会在启动时清一次，之后每小时复查一次。", "Checked once at startup, then hourly.");
            Both("page.lyr.pick", "选择当前歌曲的歌词…", "Pick lyrics for this song…");
            Both("cand.loading", "正在搜索…", "Searching…");
            Both("cand.empty", "没有找到候选歌词", "No candidates found");
            Both("cand.count", "共 {0} 个候选，按匹配度排序", "{0} candidates, best match first");
            Both("cand.unknownArtist", "未知艺人", "Unknown artist");
            Both("menu.lyricEarly", "歌词提前 0.1s", "Lyrics 0.1s earlier");
            Both("menu.lyricLate", "歌词延后 0.1s", "Lyrics 0.1s later");
            Both("menu.bigger", "放大", "Bigger");
            Both("menu.smaller", "缩小", "Smaller");
            Both("menu.fadeMore", "更透明", "More transparent");
            Both("menu.fadeLess", "更不透明", "Less transparent");
            Both("menu.clickthrough", "交互穿透（屏蔽卡片交互）", "Click-through (ignore the mouse)");
            Both("menu.clearBg", "透明背景（只留文字）", "Transparent background (text only)");
            Both("menu.clearBgShort", "透明背景", "Transparent background");
            Both("menu.spin", "封面旋转", "Spin artwork");
            Both("menu.listenAll", "监听所有播放器", "Listen to all players");
            Both("menu.autostart", "开机自启", "Start with Windows");
            Both("menu.close", "关闭卡片", "Close card");
            Both("menu.cancel", "取消", "Cancel");
            Both("menu.quit", "退出", "Quit");

            Both("export.title", "歌词已导出", "Lyrics exported");
            Both("export.body", "导出 {0} 首，已存在跳过 {1} 首，纯音乐/空文件跳过 {2} 首。\n想看效果就把文件挪到 data\\lrc",
                 "Exported {0}, skipped {1} already present, {2} instrumental/empty.\nMove them into data\\lrc to use them.");

            Both("settings.title", "个性化设置", "Appearance");

            Both("sec.ui", "界面", "Interface");
            Both("sec.entry", "入口与快捷键", "Entry & shortcuts");
            Both("sec.text", "文字", "Text");
            Both("sec.bar", "进度条", "Progress bar");
            Both("sec.art", "封面", "Artwork");
            Both("sec.card", "卡片", "Card");
            Both("sec.behavior", "行为", "Behaviour");
            Both("sec.presets", "预设", "Presets");

            Both("sec.text", "文字", "Text");
            Both("sec.bar", "进度条", "Progress bar");
            Both("sec.art", "封面", "Artwork");
            Both("sec.card", "卡片", "Card");
            Both("lbl.lang", "界面语言", "Language");
            Both("lang.auto", "跟随系统", "System");
            Both("lang.zh", "中文", "中文");
            Both("lang.en", "English", "English");

            Both("uninst.title", "卸载", "Uninstall");
            Both("uninst.confirm",
                 "将退出卡片、删除开机自启、删除配置与歌词缓存，并删除程序本身。\n确定要卸载吗？",
                 "This stops the card, removes the startup entry, deletes the config and lyric cache, and deletes the program itself.\nUninstall now?");
            Both("uninst.done",
                 "已卸载：关闭了 {0} 个运行中的副本，开机自启已清除（{1}），删除了 {2} 个数据目录。",
                 "Uninstalled: closed {0} running copy(ies), startup entry cleared ({1}), {2} data folder(s) removed.");
            Both("menu.uninstall", "卸载…", "Uninstall…");


            Both("lbl.titleSize", "歌名字号", "Title size");
            Both("lbl.titleColor", "歌名颜色", "Title colour");
            Both("lbl.artistSize", "作者字号", "Artist size");
            Both("lbl.artistMax", "作者长度上限", "Artist max chars");
            Both("lbl.titleIndent", "歌名顶格余量", "Title left padding");
            Both("lbl.lyricIndent", "歌词顶格余量", "Lyrics left padding");
            Both("lbl.align", "对齐方式", "Alignment");
            Both("align.left", "左", "L");
            Both("align.center", "中", "C");
            Both("align.right", "右", "R");
            Both("lbl.artistColor", "作者颜色", "Artist colour");
            Both("lbl.lyricSize", "歌词字号", "Lyrics size");
            Both("lbl.lyricColor", "歌词颜色", "Lyrics colour");

            Both("lbl.barScale", "长度", "Length");
            Both("lbl.barThickness", "粗细", "Thickness");
            Both("lbl.barColor", "轨道颜色", "Track colour");
            Both("lbl.barFill", "填充颜色", "Fill colour");

            Both("lbl.artSize", "大小", "Size");
            Both("lbl.artSat", "饱和度", "Saturation");
            Both("lbl.artHiCut", "高光压制", "Highlight cut");
            Both("lbl.artShadow", "封面阴影", "Artwork shadow");
            Both("lbl.textShadow", "文字阴影（浅色壁纸用）", "Text shadow (for light wallpapers)");

            Both("lbl.scale", "整体缩放", "Scale");
            Both("lbl.opacity", "整体透明度", "Opacity");
            Both("lbl.controlsColor", "下方按钮颜色", "Button colour");
            Both("lbl.controlsX", "下方按钮X轴位置（%）", "Buttons X position (%)");
            Both("lbl.clearBg", "透明背景（只留文字）", "Transparent background");
            Both("lbl.spin", "封面旋转", "Spin artwork");

            Both("lbl.autostart", "开机自启", "Start with Windows");
            Both("lbl.listenAll", "监听所有播放器", "Listen to all players");

            Both("preset.prompt", "给这套外观起个名字", "Name this look");
            Both("preset.save", "保存当前", "Save current");
            Both("preset.defaultName", "预设 {0}", "Preset {0}");
            Both("preset.empty", "还没有预设 —— 上面起个名字，点「保存当前」把现在这套外观存下来",
                 "No presets yet — type a name above, then hit “Save current”.");

            Both("footer.live", "改动即时生效", "Changes apply instantly");
            Both("btn.done", "完成", "Done");
            Both("btn.reset", "恢复默认", "Restore defaults");
            Both("board.apply", "应用", "Apply");
            Both("board.cancel", "取消", "Cancel");
        }

        // CultureInfo.CurrentUICulture follows the system locale, which can differ from the
        // Windows display language (this machine reports en-US there while the UI is zh-CN),
        // so "follow the system" has to ask Win32 for the UI language instead.
        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        static extern ushort GetUserDefaultUILanguage();

        public static bool SystemIsChinese()
        {
            try
            {
                int primary = GetUserDefaultUILanguage() & 0x3FF;
                return primary == 0x04;   // LANG_CHINESE
            }
            catch
            {
                try { return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "zh"; }
                catch { return false; }
            }
        }

        public static void Resolve(int pref)
        {
            Preference = pref;
            if (pref == LangAuto) Current = SystemIsChinese() ? LangZh : LangEn;
            else Current = (pref == LangZh) ? LangZh : LangEn;
        }

        public static string T(string key)
        {
            Dictionary<string, string> d = (Current == LangZh) ? Zh : En;
            string v;
            if (d.TryGetValue(key, out v)) return v;
            if (Zh.TryGetValue(key, out v)) return v;
            return key;
        }

        public static string F(string key, params object[] args)
        {
            return string.Format(T(key), args);
        }
    }
}
