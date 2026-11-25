#!/bin/bash
# Build Lyricard (浮声) with the in-box .NET Framework C# compiler.
# NOTE: csc.exe receives argv as ANSI, so every argument must stay ASCII.
# That is why we cd into src/ and pass only relative paths for the project files.
#
#   bash build.sh            build + deploy to the install folder
#   bash build.sh dist       also produce dist/Lyricard-v<version>.zip
#   bash build.sh desktop    dist + copy the zip onto the Desktop
set -u

# Git-Bash on CI (and any MSYS shell) rewrites arguments that start with "/" into
# Windows paths, which turns "/nologo" into "C:/Program Files/Git/nologo". csc.exe
# wants the raw switch, so opt out of that conversion for everything below.
export MSYS2_ARG_CONV_EXCL="*"
export MSYS_NO_PATHCONV=1

PROJ="$(cd "$(dirname "$0")" && pwd)"   # MSYS path; PowerShell calls go through cygpath -w
CSC="C:/Windows/Microsoft.NET/Framework64/v4.0.30319/csc.exe"
WPF="C:/Windows/Microsoft.NET/Framework64/v4.0.30319/WPF"
GAC="C:/Windows/Microsoft.NET/assembly/GAC_MSIL"
REF="C:/Program Files (x86)/Reference Assemblies/Microsoft/Framework/.NETFramework/v4.7.2"

APP="Lyricard"
VERSION="0.2.3"
INSTALL="/e/APPLE MUSIC GeCi"

mkdir -p "$PROJ/bin"
rm -f "$PROJ/bin/$APP.exe" "$PROJ/bin/AppleMusicCard.exe"

cd "$PROJ/src" || exit 1

REFS=()
for d in \
  "$WPF/PresentationFramework.dll" \
  "$WPF/PresentationCore.dll" \
  "$WPF/WindowsBase.dll" \
  "$GAC/System.Xaml/v4.0_4.0.0.0__b77a5c561934e089/System.Xaml.dll" \
  "$GAC/System.Drawing/v4.0_4.0.0.0__b03f5f7f11d50a3a/System.Drawing.dll" \
  "$GAC/System.Windows.Forms/v4.0_4.0.0.0__b77a5c561934e089/System.Windows.Forms.dll" \
  "$REF/Facades/System.Runtime.dll" ; do
  if [ -f "$d" ]; then REFS+=("/r:$d"); else echo "MISSING REF: $d" >&2; fi
done

REFS+=("/r:../libs/pkg/ref/netstandard2.0/Windows.Foundation.UniversalApiContract.winmd")
REFS+=("/r:../libs/pkg/ref/netstandard2.0/Windows.Foundation.FoundationContract.winmd")

"$CSC" \
  /nologo /noconfig /target:winexe /platform:x64 /optimize+ /debug- \
  /nowarn:1701,1702,1591,0618,0219,0414 \
  /out:../bin/$APP.exe \
  /win32manifest:app.manifest \
  /win32icon:../assets/lyricard.ico \
  "${REFS[@]}" \
  /r:System.dll /r:System.Core.dll /r:System.Xml.dll \
  /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll \
  Program.cs Config.cs Players.cs AudioPeak.cs Json.cs Smtc.cs Lyrics.cs CardWindow.cs Native.cs Tray.cs \
  AmProbe.cs MenuTheme.cs NetEaseQueue.cs ColorLibraryWindow.cs Strings.cs SettingsWindow.cs MainWindow.cs Uninstall.cs Shortcut.cs CandidateWindow.cs Updater.cs
rc=$?
echo "csc exit=$rc"
ls -l "$PROJ/bin/$APP.exe" 2>/dev/null

# Deploy to the install directory.
# Running the card from inside the DSH workspace gets it a restricted token that
# cannot enumerate processes, which breaks the "is Apple Music running" probe, so the
# live copy lives outside the workspace.
if [ $rc -eq 0 ] && [ -d "$INSTALL" ]; then
  powershell.exe -NoProfile -Command "Stop-Process -Name $APP,AppleMusicCard -Force -ErrorAction SilentlyContinue" >/dev/null 2>&1
  sleep 1
  mkdir -p "$INSTALL/data"
  cp -f "$PROJ/bin/$APP.exe" "$INSTALL/$APP.exe" && echo "deployed to $INSTALL/$APP.exe"
  rm -f "$INSTALL/AppleMusicCard.exe"   # old name after the rename
  [ -f "$INSTALL/data/config.ini" ] || cp -f "$PROJ/bin/data/config.ini" "$INSTALL/data/config.ini" 2>/dev/null
fi

# Optional: distributable zip (exe + INSTALL.md + README.md).
if [ "${1:-}" = "dist" ] || [ "${1:-}" = "desktop" ]; then
  if [ $rc -eq 0 ]; then
    NAME="$APP-v$VERSION"
    OUT="$PROJ/dist/$NAME"
    ZIP="$PROJ/dist/$NAME.zip"
    rm -rf "$OUT" "$ZIP" 2>/dev/null
    mkdir -p "$OUT"
    cp -f "$PROJ/bin/$APP.exe" "$OUT/"
    cp -f "$PROJ/README.md" "$OUT/"
    cp -f "$PROJ/INSTALL.md" "$OUT/"
    cp -f "$PROJ/LICENSE" "$OUT/"
    cp -f "$PROJ/packaging/Make shortcut.cmd" "$OUT/"
    # NOTE: no .lnk is shipped. A .lnk generated here would bake this machine's
    # absolute path into the release. The user gets one by double-clicking
    # "Make shortcut.cmd" (it calls `Lyricard.exe --shortcut desktop`).
    OUTW="$(cygpath -w "$OUT")"
    ZIPW="$(cygpath -w "$ZIP")"
    powershell.exe -NoProfile -Command "Compress-Archive -Path '$OUTW\\*' -DestinationPath '$ZIPW' -Force" 2>&1 | head -3
    ls -l "$ZIP"
    if [ "${1:-}" = "desktop" ]; then
      powershell.exe -NoProfile -Command "\$d=[Environment]::GetFolderPath('Desktop'); \$t=Join-Path \$d '$NAME.zip'; Copy-Item -LiteralPath '$ZIPW' -Destination \$t -Force; if (Test-Path \$t) { Write-Output ('desktop: ' + \$t + ' (' + (Get-Item \$t).Length + ' bytes)') } else { Write-Output 'desktop copy FAILED' }"
    fi
  fi
fi
exit $rc
