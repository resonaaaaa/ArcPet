param([switch]$Test,[string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$references = Join-Path ${env:ProgramFiles(x86)} 'Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8'
if (!(Test-Path -LiteralPath $compiler) -or !(Test-Path -LiteralPath $references)) {
    throw 'Building requires the .NET Framework 4.8 reference assemblies and C# compiler.'
}
$output = if($OutputDirectory) { [System.IO.Path]::GetFullPath((Join-Path $projectRoot $OutputDirectory)) } else { Join-Path $projectRoot 'dist\ArcPet-v0.9' }
$testOutput = Join-Path $PSScriptRoot '.build'
New-Item -ItemType Directory -Path $output,$testOutput -Force | Out-Null
$baseRefs = @('mscorlib.dll','System.dll','System.Core.dll','System.Runtime.Serialization.dll','System.Xml.dll') | ForEach-Object { '/reference:' + (Join-Path $references $_) }
$coreSources = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'ArcPet.Animation') -Filter '*.cs' | ForEach-Object FullName)
$coreArgs = @('/nologo','/noconfig','/nostdlib+','/optimize+','/warnaserror+','/langversion:5','/target:library',('/out:' + (Join-Path $output 'ArcPet.Animation.dll'))) + $baseRefs + $coreSources
& $compiler @coreArgs
if ($LASTEXITCODE -ne 0) { throw 'Animation library build failed.' }
$behaviorSources = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'ArcPet.Behavior') -Filter '*.cs' | ForEach-Object FullName)
$behaviorArgs = @('/nologo','/noconfig','/nostdlib+','/optimize+','/warnaserror+','/langversion:5','/target:library',('/out:' + (Join-Path $output 'ArcPet.Behavior.dll')),('/reference:' + (Join-Path $output 'ArcPet.Animation.dll'))) + $baseRefs + $behaviorSources
& $compiler @behaviorArgs
if ($LASTEXITCODE -ne 0) { throw 'Behavior library build failed.' }
$settingsSources = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'ArcPet.Settings') -Filter '*.cs' | ForEach-Object FullName)
$settingsArgs = @('/nologo','/noconfig','/nostdlib+','/optimize+','/warnaserror+','/langversion:5','/target:library',('/out:' + (Join-Path $output 'ArcPet.Settings.dll')),('/reference:' + (Join-Path $output 'ArcPet.Behavior.dll')),('/reference:' + (Join-Path $output 'ArcPet.Animation.dll'))) + $baseRefs + $settingsSources
& $compiler @settingsArgs
if ($LASTEXITCODE -ne 0) { throw 'Settings library build failed.' }
$uiRefs = @('WindowsBase.dll','PresentationCore.dll','PresentationFramework.dll','System.Xaml.dll','System.Windows.Forms.dll','System.Drawing.dll') | ForEach-Object { '/reference:' + (Join-Path $references $_) }
$desktopSources = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'ArcPet.Desktop') -Filter '*.cs' | ForEach-Object FullName)
$desktopArgs = @('/nologo','/noconfig','/nostdlib+','/optimize+','/warnaserror+','/langversion:5','/target:winexe',('/out:' + (Join-Path $output 'ArcPet.Preview.exe')),('/reference:' + (Join-Path $output 'ArcPet.Animation.dll'))) + @('/reference:' + (Join-Path $output 'ArcPet.Behavior.dll')) + @('/reference:' + (Join-Path $output 'ArcPet.Settings.dll')) + $baseRefs + $uiRefs + $desktopSources
$icon = Join-Path $PSScriptRoot 'assets\arcpet.ico'
$desktopArgs += @(('/win32icon:' + $icon), ('/resource:' + $icon + ',ArcPet.Icon'))
& $compiler @desktopArgs
if ($LASTEXITCODE -ne 0) { throw 'Desktop preview build failed.' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'ArcPet.Desktop\ArcPet.Preview.exe.config') -Destination $output -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'USAGE.txt') -Destination (Join-Path $output '使用说明.txt') -Force
foreach ($notice in @('LICENSE','NOTICE')) {
    Copy-Item -LiteralPath (Join-Path $projectRoot $notice) -Destination $output -Force
}
$assetRoot = Join-Path $output 'assets'
$sourceManifest = Get-Content -LiteralPath (Join-Path $projectRoot 'manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$copiedFrameFolders = @{}
foreach ($action in $sourceManifest.actions.PSObject.Properties) {
    $clipPath = $action.Value.path
    $frameFolder = Split-Path -Parent $clipPath
    $actionRoot = Join-Path $assetRoot $frameFolder
    if (!$copiedFrameFolders.ContainsKey($frameFolder)) {
        New-Item -ItemType Directory -Path $actionRoot -Force | Out-Null
        $sourceRoot = Join-Path $projectRoot $frameFolder
        foreach($oldFrame in (Get-ChildItem -LiteralPath $actionRoot -Filter '*.png' | Where-Object {$_.Name -match '^\d{3}\.png$'})) {
            if(!(Test-Path -LiteralPath (Join-Path $sourceRoot $oldFrame.Name))){Remove-Item -LiteralPath $oldFrame.FullName -Force}
        }
        Get-ChildItem -LiteralPath $sourceRoot -Filter '*.png' | Where-Object { $_.Name -match '^\d{3}\.png$' } | Copy-Item -Destination $actionRoot -Force
        $copiedFrameFolders[$frameFolder] = $true
    }
    Copy-Item -LiteralPath (Join-Path $projectRoot $clipPath) -Destination $actionRoot -Force
}
Copy-Item -LiteralPath (Join-Path $projectRoot 'manifest.json') -Destination $assetRoot -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'assets/moods') -Destination $assetRoot -Recurse -Force
if ($Test) {
    $testExe = Join-Path $testOutput 'ArcPet.Tests.exe'
    & $compiler /nologo /noconfig /nostdlib+ /warnaserror+ /langversion:5 /target:exe "/out:$testExe" "/reference:$(Join-Path $output 'ArcPet.Animation.dll')" "/reference:$(Join-Path $output 'ArcPet.Behavior.dll')" "/reference:$(Join-Path $output 'ArcPet.Settings.dll')" @baseRefs @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'ArcPet.Tests') -Filter '*.cs' | ForEach-Object FullName)
    if ($LASTEXITCODE -ne 0) { throw 'Test build failed.' }
    Copy-Item -LiteralPath (Join-Path $output 'ArcPet.Animation.dll') -Destination $testOutput -Force
    Copy-Item -LiteralPath (Join-Path $output 'ArcPet.Behavior.dll') -Destination $testOutput -Force
    Copy-Item -LiteralPath (Join-Path $output 'ArcPet.Settings.dll') -Destination $testOutput -Force
    & $testExe $assetRoot $testOutput
    if ($LASTEXITCODE -ne 0) { throw 'Animation checks failed.' }
}
Write-Output "Built: $(Join-Path $output 'ArcPet.Preview.exe')"
