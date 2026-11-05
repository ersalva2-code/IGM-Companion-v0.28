param([string]$Compiler='', [string]$ReferenceDirectory='', [switch]$VerifyRebuild)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
if (!$Compiler) { $Compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe' }
if (!(Test-Path -LiteralPath $Compiler)) { throw 'Supply -Compiler pointing to a C# compiler.' }
if (!$ReferenceDirectory) { $ReferenceDirectory=Join-Path ${env:ProgramFiles(x86)} 'Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8' }
if (!(Test-Path -LiteralPath (Join-Path $ReferenceDirectory 'mscorlib.dll'))) { throw 'Install the .NET Framework 4.8 Developer Pack or supply -ReferenceDirectory.' }
$out=Join-Path $PSScriptRoot 'build'
New-Item -ItemType Directory -Path $out -Force | Out-Null
$exe=Join-Path $out 'IGM_Companion.exe'
# Roslyn is required for deterministic builds; fail explicitly on the legacy framework compiler.
$version=& $Compiler /version
if($LASTEXITCODE -ne 0 -or $version -notmatch '^4\.11\.') { throw 'Use Microsoft Roslyn 4.11.x csc.exe for this reproducible build. Supply -Compiler. No compiler is downloaded.' }
$buildArgs=@('/nologo','/noconfig','/nostdlib+','/target:winexe','/platform:x64','/langversion:5','/optimize+','/debug-','/codepage:65001','/deterministic+',('/pathmap:'+$PSScriptRoot+'=/src/IGMCompanion'),('/out:'+$exe),('/win32icon:'+(Join-Path $PSScriptRoot 'Companion.ico')),('/win32manifest:'+(Join-Path $PSScriptRoot 'app.manifest')),('/resource:'+(Join-Path $PSScriptRoot 'Assets.resources')+',IGM.Assets'))
foreach($name in @('mscorlib','System','System.Core','System.Drawing','System.Windows.Forms','System.Web.Extensions')) { $buildArgs += '/reference:'+(Join-Path $ReferenceDirectory ($name+'.dll')) }
foreach($name in @('AssemblyInfo.cs','Core.cs','Storage.cs','Art.cs','Interface.cs')) { $buildArgs += Join-Path $PSScriptRoot $name }
& $Compiler @buildArgs
if($LASTEXITCODE -ne 0) { throw 'Compilation failed.' }
$first=(Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash
if($VerifyRebuild) {
    & $Compiler @buildArgs
    if($LASTEXITCODE -ne 0) { throw 'Verification rebuild failed.' }
    $second=(Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash
    if($first -ne $second) { throw 'Rebuild hashes differ.' }
    Write-Output 'Verified: two builds produced the same SHA256.'
}
Get-FileHash -LiteralPath $exe -Algorithm SHA256
Write-Output "Built $exe. No game files installed or modified."
