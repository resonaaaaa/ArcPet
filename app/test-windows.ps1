param([string]$Runtime='dist/ArcPet-v0.20.1',[string]$Report='docs/desktop-v0.20.1-validation',[switch]$MoodOnly,[switch]$RocketOnly,[switch]$DanceOnly,[switch]$TravelOnly,[switch]$PomodoroOnly)
$ErrorActionPreference='Stop'
$arcRoot=Split-Path -Parent $PSScriptRoot
$arcOutput=[IO.Path]::GetFullPath((Join-Path $arcRoot $Runtime))
$arcReport=[IO.Path]::GetFullPath((Join-Path $arcRoot $Report))
$arcBuild=Join-Path $PSScriptRoot '.build'
$arcRef=Join-Path ${env:ProgramFiles(x86)} 'Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8'
$arcCompiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
New-Item -ItemType Directory -Force -Path $arcReport,$arcBuild | Out-Null
$arcLibs=@('mscorlib.dll','System.dll','System.Core.dll','System.Runtime.Serialization.dll','System.Xml.dll','WindowsBase.dll','PresentationCore.dll','PresentationFramework.dll','System.Xaml.dll','System.Windows.Forms.dll','System.Drawing.dll') | ForEach-Object {'/reference:'+(Join-Path $arcRef $_)}
foreach($arcLibrary in @('ArcPet.Animation.dll','ArcPet.Behavior.dll','ArcPet.Settings.dll')){
    $arcLibs+='/reference:'+(Join-Path $arcOutput $arcLibrary)
    Copy-Item -LiteralPath (Join-Path $arcOutput $arcLibrary) -Destination $arcBuild -Force
}
$arcCases=if($PomodoroOnly){@('PomodoroRegression')}elseif($TravelOnly){@('TravelRegression')}elseif($DanceOnly){@('DanceRegression')}elseif($RocketOnly){@('RocketRegression')}elseif($MoodOnly){@('MoodRegression')}else{@('PomodoroRegression','InputRegression','MoodRegression','RocketRegression','DanceRegression','SleepVariantRegression','TravelRegression')}
foreach($arcCase in $arcCases){
    $arcTest=Join-Path $arcBuild ($arcCase+'.exe')
    & $arcCompiler /nologo /noconfig /nostdlib+ /warnaserror+ /langversion:5 /target:exe "/out:$arcTest" @arcLibs (Join-Path $PSScriptRoot ('ArcPet.Desktop.Tests\'+$arcCase+'.cs'))
    if($LASTEXITCODE -ne 0){throw "$arcCase build failed"}
    & $arcTest (Join-Path $arcOutput 'ArcPet.Preview.exe') $arcReport
    if($LASTEXITCODE -ne 0){throw "$arcCase failed"}
}

