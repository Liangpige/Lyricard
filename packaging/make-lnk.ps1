# Usage: make-lnk.ps1 <target-exe> <link-path>   (both relative to the current directory)
# Creates a .lnk that points at the exe next to it, using the exe's own icon.
param([string]$Target, [string]$Link)
$ErrorActionPreference = "Stop"
$w = New-Object -ComObject WScript.Shell
$s = $w.CreateShortcut((Join-Path (Get-Location) $Link))
$exe = Join-Path (Get-Location) $Target
$s.TargetPath = $exe
$s.WorkingDirectory = (Get-Location).Path
$s.IconLocation = "$exe,0"
$s.Description = "Lyricard"
$s.Save()
Write-Output ("link: " + (Test-Path (Join-Path (Get-Location) $Link)))
