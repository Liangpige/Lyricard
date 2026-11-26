# Captures the card at a given Scale so every later refactor step can be diffed against it.
# Usage: baseline-shot.ps1 -Scale 1.0 -Name 1x
param([double]$Scale = 1.0, [string]$Name = "shot", [int]$WaitMs = 2800, [string]$OutDir = "C:\Users\HYJ\AppData\Local\Temp\shots")
$ErrorActionPreference = "Stop"
$cfg = "E:\APPLE MUSIC GeCi\data\config.ini"
$exe = "E:\APPLE MUSIC GeCi\Lyricard.exe"
$out = $OutDir
if (-not (Test-Path $out)) { New-Item -ItemType Directory -Path $out | Out-Null }

Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public class B0 {
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L,T,R,B; }
}
"@
[void][B0]::SetProcessDPIAware()
Add-Type -AssemblyName System.Drawing

# 1. set the scale in config.ini (BOM preserved, one key only)
$text = [System.IO.File]::ReadAllText($cfg)
if ($text -match "(?m)^Scale=.*$") { $text = $text -replace "(?m)^Scale=.*$", ("Scale=" + $Scale.ToString([System.Globalization.CultureInfo]::InvariantCulture)) }
else { $text = $text + "`r`nScale=" + $Scale }
[System.IO.File]::WriteAllText($cfg, $text, (New-Object System.Text.UTF8Encoding($true)))

# 2. restart so the new scale is loaded
Get-Process Lyricard -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 1200
Start-Process -FilePath $exe -ArgumentList "--background" | Out-Null
Start-Sleep -Milliseconds $WaitMs

# 3. biggest visible window of that process = the card (main page is not opened with --background)
$p = Get-Process Lyricard | Select-Object -First 1
$script:best = [IntPtr]::Zero; $script:area = 0
$cb = [B0+EnumProc]{ param($h,$l)
  $q = 0; [void][B0]::GetWindowThreadProcessId($h, [ref]$q)
  if ($q -eq $p.Id -and [B0]::IsWindowVisible($h)) {
    $r = New-Object B0+RECT; [void][B0]::GetWindowRect($h, [ref]$r)
    $w = $r.R - $r.L; $hh = $r.B - $r.T
    if ($w -gt 200 -and $hh -gt 80 -and ($w * $hh) -gt $script:area) { $script:area = $w * $hh; $script:best = $h }
  }
  return $true }
[void][B0]::EnumWindows($cb, [IntPtr]::Zero)
if ($script:best -eq [IntPtr]::Zero) { Write-Output "no card window"; exit 1 }
$r = New-Object B0+RECT; [void][B0]::GetWindowRect($script:best, [ref]$r)
$w = $r.R - $r.L; $hh = $r.B - $r.T

# 4. full desktop + a 12px-margin crop around the card
$screen = New-Object System.Drawing.Bitmap 2560,1440
$g = [System.Drawing.Graphics]::FromImage($screen)
$g.CopyFromScreen(0, 0, 0, 0, (New-Object System.Drawing.Size 2560,1440))
$screen.Save("$out\$Name-full.png", [System.Drawing.Imaging.ImageFormat]::Png)
$pad = 12
$x = [Math]::Max(0, $r.L - $pad); $y = [Math]::Max(0, $r.T - $pad)
$cw = [Math]::Min(2560 - $x, $w + 2*$pad); $ch = [Math]::Min(1440 - $y, $hh + 2*$pad)
$crop = $screen.Clone((New-Object System.Drawing.Rectangle $x, $y, $cw, $ch), $screen.PixelFormat)
$crop.Save("$out\$Name-card.png", [System.Drawing.Imaging.ImageFormat]::Png)
$crop.Dispose(); $g.Dispose(); $screen.Dispose()
Write-Output ("scale=" + $Scale + " rect=" + $r.L + "," + $r.T + " " + $w + "x" + $hh + " -> $Name-card.png")
