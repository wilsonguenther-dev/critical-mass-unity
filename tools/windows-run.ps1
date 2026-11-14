# DG-56: the Windows test run. Put this file next to CriticalMass.exe (the Windows zip ships it) and run, in PowerShell:
#   powershell -ExecutionPolicy Bypass -File .\windows-run.ps1
# Pass A (logic): -batchmode -nographics autoplay match; Pass B (graphics): a real Direct3D 11 window at 1280x720 for one
# autoplay match with screenshots at the title and mid-match. Uses its own data folder (never your settings). At the end
# it prints PASS or FAIL per pass and leaves CriticalMass-windows-test-<time>.zip on your Desktop: send that zip back.
# Exit code: 0 when both passes pass. Optional: -Quick (Pass B only, 4x speed), -SkipB (Pass A only, the CI logic run),
# -Trace (Pass B also records the Direct3D 11 shader warm-up states to test-<time>\data-b\traces, ShaderWarmup / DG-59).
param([switch]$Quick, [switch]$SkipB, [switch]$Trace, [string]$Exe = ".\CriticalMass.exe")
$ErrorActionPreference = "Stop"
if (-not (Test-Path $Exe)) { Write-Host "CriticalMass.exe is not in this folder. Run this from the unzipped game folder."; exit 66 }
$stamp = (Get-Date).ToUniversalTime().ToString("yyyyMMdd-HHmmss")
$out = Join-Path (Get-Location) "test-$stamp"
New-Item -ItemType Directory -Force -Path $out | Out-Null
function Check-Log($log, $needDevice) {
  if (-not (Test-Path $log)) { return "no log" }
  $t = Get-Content $log -Raw
  $why = @()
  if ($t -notmatch "\[CM\.Autoplay\] MATCH END") { $why += "no MATCH END" }
  $exc = ([regex]::Matches($t, "(?m)^\s*(?:[\w.]+\.)?\w*Exception\b")).Count
  if ($exc -gt 0) { $why += "$exc exception lines" }
  if ($t -match "(?i)shader.*error|error.*shader") { $why += "shader errors" }
  if ($needDevice -and $t -notmatch "Direct3D 11") { $why += "no Direct3D 11 line" }
  if ($why.Count -eq 0) { return "ok" } else { return ($why -join ", ") }
}
$results = @{}
$codes = @{}
if (-not $Quick) {
  Write-Host "Pass A: the game logic, no window (about 4 minutes)..."
  $a = Start-Process $Exe -ArgumentList '-batchmode','-nographics','-autoplay','-quitAfterMatch','-autoplaySpeed','8','-dataDir',"$out\data-a",'-logFile',"$out\Player-A.log",'-perfLog',"$out\perf-A.json" -Wait -PassThru
  $codes.A = $a.ExitCode
  $results.A = Check-Log "$out\Player-A.log" $false
  Write-Host ("Pass A: exit {0}, {1}" -f $a.ExitCode, $results.A)
}
if (-not $SkipB) {
  $speed = if ($Quick) { '4' } else { '2' }
  Write-Host "Pass B: a real game window for one match (leave it alone; about 4 to 8 minutes)..."
  $argsB = @('-force-d3d11','-screen-fullscreen','0','-screen-width','1280','-screen-height','720','-autoplay','-quitAfterMatch','-autoplaySpeed',$speed,'-shots','-dataDir',"$out\data-b",'-logFile',"$out\Player-B.log",'-perfLog',"$out\perf-B.json")
  if ($Trace) { $argsB += '-traceStates' }
  $b = Start-Process $Exe -ArgumentList $argsB -Wait -PassThru
  $codes.B = $b.ExitCode
  $results.B = Check-Log "$out\Player-B.log" $true
  foreach ($s in 'shot-title.png','shot-mid.png') { if (-not (Test-Path "$out\data-b\$s")) { $results.B += ", missing $s" } }
  Write-Host ("Pass B: exit {0}, {1}" -f $b.ExitCode, $results.B)
}
# System facts for us (no personal data: Windows version, CPU, GPU, memory, displays).
Get-CimInstance Win32_OperatingSystem | Select-Object Caption, Version | Out-File "$out\system.txt"
Get-CimInstance Win32_Processor | Select-Object Name, NumberOfCores | Out-File -Append "$out\system.txt"
Get-CimInstance Win32_VideoController | Select-Object Name, DriverVersion, AdapterRAM, CurrentHorizontalResolution, CurrentVerticalResolution, CurrentRefreshRate | Out-File -Append "$out\system.txt"
Get-CimInstance Win32_ComputerSystem | Select-Object TotalPhysicalMemory | Out-File -Append "$out\system.txt"
$zip = Join-Path ([Environment]::GetFolderPath("Desktop")) "CriticalMass-windows-test-$stamp.zip"
Compress-Archive -Path "$out\*" -DestinationPath $zip -Force
$pass = $true
foreach ($k in $results.Keys) { if ($results[$k] -ne "ok" -or $codes[$k] -ne 0) { $pass = $false } }
Write-Host ""
if ($pass) { Write-Host "PASS. Send this file back: $zip" } else { Write-Host "FAIL. Send this file back: $zip" }
if ($pass) { exit 0 } else { exit 1 }
