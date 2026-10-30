param([string]$Version='0.9',[switch]$Baseline,[string]$ReportVersion='0.9.2')
$ErrorActionPreference='Stop'
$root=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$bin=Join-Path $root "dist/ArcPet-v$Version"
$output=Join-Path $root "docs/desktop-v$ReportVersion-validation"
$build=Join-Path $root 'app/.build/desktop-input'
New-Item -ItemType Directory -Path $build,$output -Force | Out-Null
$compiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$references=Join-Path ${env:ProgramFiles(x86)} 'Reference Assemblies/Microsoft/Framework/.NETFramework/v4.8'
$names=@('mscorlib','System','System.Core','WindowsBase','PresentationCore','PresentationFramework','System.Xaml')
$argsList=@('/nologo','/noconfig','/nostdlib+','/warnaserror+','/langversion:5','/target:exe',('/out:'+(Join-Path $build 'InputRegression.exe')))
$argsList+=@($names | ForEach-Object {'/reference:'+(Join-Path $references ($_+'.dll'))})
foreach($name in @('ArcPet.Animation','ArcPet.Behavior','ArcPet.Settings')){
 Copy-Item -LiteralPath (Join-Path $bin ($name+'.dll')) -Destination $build -Force
 $argsList+='/reference:'+(Join-Path $bin ($name+'.dll'))
}
$argsList+=Join-Path $PSScriptRoot 'InputRegression.cs'
& $compiler @argsList
if($LASTEXITCODE -ne 0){throw 'Input regression build failed.'}
$runArgs=@((Join-Path $bin 'ArcPet.Preview.exe'),$output)
if($Baseline){$runArgs+='--baseline'}
& (Join-Path $build 'InputRegression.exe') @runArgs
if($LASTEXITCODE -ne 0){throw 'Input regression failed.'}
