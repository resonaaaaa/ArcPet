$ErrorActionPreference='Stop'
$arcRoot=Split-Path -Parent $PSScriptRoot
$arcStable=(Resolve-Path -LiteralPath (Join-Path $arcRoot 'dist/ArcPet-v0.9')).Path
$arcStage=(Resolve-Path -LiteralPath (Join-Path $arcRoot 'dist/ArcPet-v0.19')).Path
$arcExe=Join-Path $arcStable 'ArcPet.Preview.exe'
$arcEvidence=Join-Path $arcRoot 'docs/desktop-v0.19-validation'
$arcValidation=Get-Content -LiteralPath (Join-Path $arcEvidence 'validation.json') -Raw -Encoding UTF8 | ConvertFrom-Json
if($arcValidation.core_checks -lt 848 -or $arcValidation.routed_window_checks -lt 138 -or $arcValidation.mood_window_checks -lt 19 -or $arcValidation.rocket_window_checks -lt 16 -or $arcValidation.dance_window_checks -lt 31 -or $arcValidation.sleep_variant_window_checks -lt 54 -or $arcValidation.travel_window_checks -lt 136){throw 'Patch checks are incomplete'}
if($arcValidation.pomodoro_window_checks -lt 44){throw 'Pomodoro checks are incomplete'}
foreach($arcHash in $arcValidation.binary_sha256.PSObject.Properties){
    if((Get-FileHash -LiteralPath (Join-Path $arcStage $arcHash.Name) -Algorithm SHA256).Hash -ne $arcHash.Value){throw 'Staged binary differs from verified release'}
}
foreach($arcAssetHash in $arcValidation.runtime_asset_sha256.PSObject.Properties){
    if((Get-FileHash -LiteralPath (Join-Path $arcStage ('assets/'+$arcAssetHash.Name)) -Algorithm SHA256).Hash -ne $arcAssetHash.Value){throw 'Staged mood asset differs from verified release'}
}
$arcFiles=@('ArcPet.Animation.dll','ArcPet.Behavior.dll','ArcPet.Settings.dll','ArcPet.Preview.exe','ArcPet.Preview.exe.config','使用说明.txt')
$arcBackup=Join-Path $arcRoot 'app/.build/update-backup-v0.19'
New-Item -ItemType Directory -Path $arcBackup -Force | Out-Null
foreach($arcName in $arcFiles){Copy-Item -LiteralPath (Join-Path $arcStable $arcName) -Destination $arcBackup -Force}
# This update changes code only. Do not duplicate or replace animation assets.

function Read-ArcStartup {
    $arcKey=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\Microsoft\Windows\CurrentVersion\Run',$false)
    try {if($null -eq $arcKey){return $null};return $arcKey.GetValue('ArcPet',$null,[Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)}
    finally {if($null -ne $arcKey){$arcKey.Dispose()}}
}
$arcStartupBefore=Read-ArcStartup
$arcRunning=@(Get-Process 'ArcPet.Preview' -ErrorAction SilentlyContinue | Where-Object {$_.Path -eq $arcExe})
Add-Type @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class ArcPetPatchWindows {
    public delegate bool Visitor(IntPtr h,IntPtr data);
    [DllImport("user32.dll")]public static extern bool EnumWindows(Visitor visitor,IntPtr data);
    [DllImport("user32.dll")]public static extern uint GetWindowThreadProcessId(IntPtr h,out uint process);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]public static extern int GetWindowText(IntPtr h,StringBuilder text,int count);
    [DllImport("user32.dll")]public static extern bool PostMessage(IntPtr h,uint message,IntPtr w,IntPtr l);
}
'@
foreach($arcProcess in $arcRunning){
    $arcProcessId=$arcProcess.Id
    $arcWindows=[Collections.Generic.List[IntPtr]]::new()
    $arcVisitor=[ArcPetPatchWindows+Visitor]{param($arcHandle,$arcUnused)
        [uint32]$arcOwner=0;[void][ArcPetPatchWindows]::GetWindowThreadProcessId($arcHandle,[ref]$arcOwner)
        if($arcOwner -eq $arcProcessId){
            $arcTitle=[Text.StringBuilder]::new(256);[void][ArcPetPatchWindows]::GetWindowText($arcHandle,$arcTitle,256)
            if($arcTitle.ToString().EndsWith('· 桌宠')){$arcWindows.Add($arcHandle)}
        }
        return $true
    }
    [void][ArcPetPatchWindows]::EnumWindows($arcVisitor,[IntPtr]::Zero)
    if($arcWindows.Count -eq 0){throw 'Cannot find the running pet window; no process was terminated'}
    foreach($arcHandle in $arcWindows){[void][ArcPetPatchWindows]::PostMessage($arcHandle,0x0010,[IntPtr]::Zero,[IntPtr]::Zero)}
    if(!$arcProcess.WaitForExit(10000)){throw 'Pet did not close normally; update stopped without forcing termination'}
}
$arcSettings=Join-Path $arcRoot 'data/settings.json'
$arcSavedPreferences=if(Test-Path -LiteralPath $arcSettings){Get-Content -LiteralPath $arcSettings -Raw -Encoding UTF8 | ConvertFrom-Json}else{$null}
try {
    foreach($arcName in $arcFiles){Copy-Item -LiteralPath (Join-Path $arcStage $arcName) -Destination (Join-Path $arcStable $arcName) -Force}
    foreach($arcAssetHash in $arcValidation.runtime_asset_sha256.PSObject.Properties){
        if((Get-FileHash -LiteralPath (Join-Path $arcStable ('assets/'+$arcAssetHash.Name)) -Algorithm SHA256).Hash -ne $arcAssetHash.Value){throw 'Promoted mood asset hash mismatch'}
    }
    foreach($arcName in $arcFiles){
        if((Get-FileHash -LiteralPath (Join-Path $arcStage $arcName)).Hash -ne (Get-FileHash -LiteralPath (Join-Path $arcStable $arcName)).Hash){throw 'Promoted file hash mismatch'}
    }
} catch {
    foreach($arcName in $arcFiles){Copy-Item -LiteralPath (Join-Path $arcBackup $arcName) -Destination (Join-Path $arcStable $arcName) -Force}
    if($arcRunning.Count -gt 0){Start-Process -FilePath $arcExe -WorkingDirectory $arcStable -WindowStyle Hidden | Out-Null}
    throw
}
$arcNewProcess=$null
if($arcRunning.Count -gt 0){
    $arcNewProcess=Start-Process -FilePath $arcExe -WorkingDirectory $arcStable -WindowStyle Hidden -PassThru
    Start-Sleep -Milliseconds 1500
    if($arcNewProcess.HasExited){throw 'Updated pet exited unexpectedly'}
}
$arcCurrentPreferences=if(Test-Path -LiteralPath $arcSettings){Get-Content -LiteralPath $arcSettings -Raw -Encoding UTF8 | ConvertFrom-Json}else{$null}
$arcPreferenceChanges=@()
if($null -ne $arcSavedPreferences){
    foreach($arcProperty in $arcSavedPreferences.PSObject.Properties){
        if($arcProperty.Value -ne $arcCurrentPreferences.($arcProperty.Name)){$arcPreferenceChanges+=$arcProperty.Name}
    }
}
if([Diagnostics.FileVersionInfo]::GetVersionInfo($arcExe).FileVersion -ne '0.19.0.0'){throw 'Installed file version is not 0.19'}
$arcStartupAfter=Read-ArcStartup
$arcHandoff=@{
    version='0.19';stable_path=$arcExe;gracefully_closed_instances=$arcRunning.Count
    restarted=($null -ne $arcNewProcess);running_pid=if($arcNewProcess){$arcNewProcess.Id}else{$null}
    actual_file_version=[Diagnostics.FileVersionInfo]::GetVersionInfo($arcExe).FileVersion
    startup_registration_unchanged=($arcStartupBefore -eq $arcStartupAfter)
    startup_registered=($null -ne $arcStartupAfter)
    preferences_preserved=($arcPreferenceChanges.Count -eq 0);changed_preference_fields=@($arcPreferenceChanges)
    files_replaced=$arcFiles;forced_termination=$false
}
$arcHandoff | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $arcEvidence 'handoff.json') -Encoding UTF8
if(!$arcHandoff.preferences_preserved -or !$arcHandoff.startup_registration_unchanged){throw 'Post-update settings comparison requires inspection'}
Write-Output 'PASS: verified patch installed at original path; normal restart preserved preferences and startup registration.'
