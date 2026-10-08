[CmdletBinding()]
param(
    [string]$UnityPath,
    [string]$UnityProjectPath,
    [switch]$RunUnityTests
)

$ErrorActionPreference = 'Stop'
$packageRoot = Split-Path -Parent $PSScriptRoot
Set-Location $packageRoot

Write-Host '== GFramework pure C# tests =='
dotnet run --project 'Tests~/FrameUpdate/Core/FrameUpdate.Core.Tests.csproj'
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
dotnet run --project 'Tests~/Modules/Core/GFramework.Modules.Tests.csproj'
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host '== asmdef boundary check =='
$asmdefs = Get-ChildItem Core, Samples~, Tests~ -Recurse -Filter '*.asmdef'
$names = @{}
foreach ($file in $asmdefs) {
    $definition = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
    if ($names.ContainsKey($definition.name)) { throw "Duplicate assembly name: $($definition.name)" }
    $names[$definition.name] = $file.FullName
}
foreach ($file in $asmdefs) {
    $definition = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
    foreach ($reference in @($definition.references)) {
        if ($reference -and $reference -notmatch '^Unity\.' -and $reference -notmatch '^UnityEngine') {
            if (-not $names.ContainsKey($reference)) { Write-Warning "Reference is external or supplied by host: $reference ($($file.FullName))" }
        }
    }
}

if ($UnityPath -and $UnityProjectPath) {
    $method = 'WFrameWork.Samples.Combined.Editor.CombinedSampleAddressablesValidator.ValidateCommandLine'
    Write-Host '== Unity package/content validation =='
    $unityArgs = @('-batchmode', '-nographics', '-quit', '-projectPath', $UnityProjectPath, '-executeMethod', $method, '-logFile', '-')
    & $UnityPath @unityArgs
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    if ($RunUnityTests) {
        Write-Host 'Unity Test Runner is host-project specific; invoke the project test runner with its installed test package.'
    }
}
else {
    Write-Host 'Unity validation skipped: provide -UnityPath and -UnityProjectPath for a real host project.'
}

Write-Host 'Package validation completed.'
