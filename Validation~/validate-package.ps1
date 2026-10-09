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

Write-Host '== asmdef cycle check =='
$graph = @{}
foreach ($file in $asmdefs) {
    $definition = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
    $graph[$definition.name] = @($definition.references | Where-Object { $_ -and $names.ContainsKey($_) })
    if ($file.FullName -match '[\\/]Runtime[\\/]' -and $definition.noEngineReferences -ne $true) {
        throw "Runtime assembly must set noEngineReferences=true: $($file.FullName)"
    }
}
$visiting = @{}
$visited = @{}
function Visit-Assembly([string]$name, [string[]]$path) {
    if ($visiting.ContainsKey($name)) { throw "asmdef reference cycle: $($path -join ' -> ') -> $name" }
    if ($visited.ContainsKey($name)) { return }
    $visiting[$name] = $true
    foreach ($next in @($graph[$name])) { Visit-Assembly $next ($path + $name) }
    $visiting.Remove($name)
    $visited[$name] = $true
}
foreach ($name in $graph.Keys) { Visit-Assembly $name @() }

if ($UnityPath -and $UnityProjectPath) {
    $method = 'WFrameWork.Samples.Combined.Editor.CombinedSampleAddressablesValidator.ValidateCommandLine'
    Write-Host '== Unity package/content validation =='
    $unityLogPath = Join-Path ([System.IO.Path]::GetTempPath()) ('gframework-unity-validation-' + [System.Guid]::NewGuid().ToString('N') + '.log')
    $unityArgs = @('-batchmode', '-nographics', '-quit', '-projectPath', $UnityProjectPath, '-executeMethod', $method, '-logFile', $unityLogPath)
    $unityProcess = Start-Process -FilePath $UnityPath -ArgumentList $unityArgs -Wait -PassThru
    if ($unityProcess.ExitCode -ne 0) {
        Write-Error "Unity validation failed with exit code $($unityProcess.ExitCode). Log: $unityLogPath"
        exit $unityProcess.ExitCode
    }
    if (-not (Test-Path -LiteralPath $unityLogPath)) {
        Write-Error "Unity validation did not produce a log."
        exit 1
    }
    $unityLog = Get-Content -LiteralPath $unityLogPath -Raw
    if ($unityLog -match 'Failed to resolve packages|Script compilation failed|Exiting without the bug reporter.*return code [1-9]') {
        Write-Error "Unity validation log reports a failure. Log: $unityLogPath"
        exit 1
    }
    if ($unityLog -notmatch 'GFramework combined (local Addressables content build|Addressables validation) passed') {
        Write-Error "Unity validation did not report the expected success marker. Log: $unityLogPath"
        exit 1
    }
    if ($RunUnityTests) {
        Write-Host 'Unity Test Runner is host-project specific; invoke the project test runner with its installed test package.'
    }
}
else {
    Write-Host 'Unity validation skipped: provide -UnityPath and -UnityProjectPath for a real host project.'
}

Write-Host 'Package validation completed.'
