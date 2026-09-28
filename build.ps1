param([switch]$Tests,[string]$OutputDirectory='bin')
$ErrorActionPreference = 'Stop'
$project = $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319/csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw '.NET Framework 4.x compiler is required to build from source.' }
$output = Join-Path $project $OutputDirectory
New-Item -ItemType Directory -Path $output -Force | Out-Null
$sources = @(Get-ChildItem -LiteralPath (Join-Path $project 'src') -Filter '*.cs' | ForEach-Object { $_.FullName })
$references = @('/r:System.dll','/r:System.Core.dll','/r:System.Drawing.dll','/r:System.Windows.Forms.dll','/r:System.Web.Extensions.dll','/r:System.IO.Compression.dll','/r:System.IO.Compression.FileSystem.dll')
$resources = @(Get-ChildItem -LiteralPath (Join-Path $project 'runtime') -Filter '*.dll' -ErrorAction SilentlyContinue | ForEach-Object { '/resource:' + $_.FullName + ',runtime.' + $_.Name })
foreach ($name in @('TextureRuntime.dll','ChaoGarage.dll','dinput8.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $project ('runtime/' + $name)))) { throw "Missing runtime/$name. Run native/build.cmd first." }
}
if ($Tests) {
    $testSources = @(Get-ChildItem -LiteralPath (Join-Path $project 'tests') -Filter '*.cs' | ForEach-Object { $_.FullName })
    & $compiler /nologo /optimize+ /platform:anycpu /target:exe "/out:$output/CoreTests.exe" $references $resources $sources $testSources /main:CoreTests
} else {
    & $compiler /nologo /optimize+ /platform:anycpu /target:winexe "/out:$output/AllStarsTextureStudio.exe" $references $resources $sources /main:AllStarsTextureStudio.Program
}
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
