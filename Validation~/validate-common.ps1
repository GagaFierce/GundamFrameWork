[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$UnityPath,
    [string]$UnityProjectPath = (Join-Path (Split-Path -Parent $PSScriptRoot) 'UnityProject~'),
    [switch]$Isolated,
    [switch]$UseCachedPackages
)
$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $UnityPath)) { throw "Unity executable not found: $UnityPath" }
$taskProject = (Resolve-Path -LiteralPath $UnityProjectPath).Path
$taskImport = $null
if ($Isolated) {
    # A source-only test host bypasses UPM without changing the user's host or dependency files.
    $taskSourceProject = $taskProject
    $taskRoot = Split-Path -Parent $PSScriptRoot
    $taskProject = Join-Path ([IO.Path]::GetTempPath()) ('gframework-common-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path (Join-Path $taskProject 'Assets/ThirdParty'), (Join-Path $taskProject 'Packages'), (Join-Path $taskProject 'ProjectSettings') -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $taskRoot 'Core') -Destination (Join-Path $taskProject 'Assets/Framework') -Recurse
    Copy-Item -LiteralPath (Join-Path $taskSourceProject 'Assets/GFrameworkTests') -Destination (Join-Path $taskProject 'Assets/GFrameworkTests') -Recurse
    foreach ($taskPackage in @('com.unity.textmeshpro@3.0.7', 'com.unity.ugui@1.0.0', 'com.unity.test-framework@1.1.33', 'com.unity.ext.nunit@1.0.6')) {
        $taskCache = Join-Path $taskSourceProject "Library/PackageCache/$taskPackage"
        if (-not (Test-Path -LiteralPath $taskCache)) { throw "Local dependency missing: $taskCache. Import dependencies in the host first." }
        Copy-Item -LiteralPath $taskCache -Destination (Join-Path $taskProject ('Assets/ThirdParty/' + $taskPackage.Split('@')[0])) -Recurse
    }
    [IO.File]::WriteAllText((Join-Path $taskProject 'Packages/manifest.json'), '{"dependencies":{}}')
    $taskImport = Join-Path $taskProject 'Assets/ThirdParty/com.unity.textmeshpro/Package Resources/TMP Essential Resources.unitypackage'
    $UseCachedPackages = $true
    Write-Host "Isolated source test host: $taskProject"
}
$taskLogs = Join-Path $taskProject 'Logs'
New-Item -ItemType Directory -Path $taskLogs -Force | Out-Null
$taskRun = [Guid]::NewGuid().ToString('N')
$taskResults = Join-Path $taskLogs "common-tests-$taskRun.xml"
$taskLog = Join-Path $taskLogs "common-tests-$taskRun.log"
$taskArguments = @('-batchmode', '-nographics', '-projectPath', ('"' + $taskProject + '"'),
    '-runTests', '-testPlatform', 'EditMode', '-assemblyNames', 'GFramework.Common.Tests',
    '-testResults', ('"' + $taskResults + '"'), '-logFile', ('"' + $taskLog + '"'))
if ($UseCachedPackages) { $taskArguments += '-noUpm' }
if ($taskImport) { $taskArguments += @('-importPackage', ('"' + $taskImport + '"')) }
$taskProcess = Start-Process -FilePath $UnityPath -ArgumentList $taskArguments -WindowStyle Hidden -PassThru -Wait
if ($taskProcess.ExitCode -ne 0) { throw "Unity exited with code $($taskProcess.ExitCode). Log: $taskLog" }
if (-not (Test-Path -LiteralPath $taskResults)) { throw "Unity produced no test results. Log: $taskLog" }
[xml]$taskReport = Get-Content -LiteralPath $taskResults -Raw
$taskSummary = $taskReport.'test-run'
if ([int]$taskSummary.total -le 0 -or [int]$taskSummary.failed -ne 0 -or $taskSummary.result -ne 'Passed') {
    throw "Tests did not pass. Result: $($taskSummary.result); total: $($taskSummary.total); failed: $($taskSummary.failed). Report: $taskResults"
}
Write-Host "Common module tests passed: $($taskSummary.passed)/$($taskSummary.total). Report: $taskResults"
