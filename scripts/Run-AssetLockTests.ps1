param(
    [Parameter(Mandatory = $true)][string]$UnityEditor,
    [string]$ResultsDirectory = (Join-Path $PWD 'Temp/AssetLockTestResults'),
    [switch]$Integration
)

$ErrorActionPreference = 'Stop'
function Write-JUnitReport([xml]$Source, [string]$Destination) {
    $run = $Source.'test-run'
    $document = New-Object System.Xml.XmlDocument
    $root = $document.CreateElement('testsuites')
    [void]$document.AppendChild($root)
    $suite = $document.CreateElement('testsuite')
    $suite.SetAttribute('name', 'AssetLock')
    $suite.SetAttribute('tests', $run.total)
    $suite.SetAttribute('failures', $run.failed)
    $suite.SetAttribute('skipped', $run.skipped)
    $suite.SetAttribute('time', $run.duration)
    [void]$root.AppendChild($suite)
    foreach ($case in $Source.SelectNodes('//test-case')) {
        $item = $document.CreateElement('testcase')
        $item.SetAttribute('name', $case.name)
        $item.SetAttribute('classname', $case.classname)
        $item.SetAttribute('time', $case.duration)
        if ($case.result -like 'Failed*') {
            $failure = $document.CreateElement('failure')
            $failure.SetAttribute('message', [string]$case.failure.message)
            $failure.InnerText = [string]$case.failure.'stack-trace'
            [void]$item.AppendChild($failure)
        }
        elseif ($case.result -like 'Skipped*') {
            [void]$item.AppendChild($document.CreateElement('skipped'))
        }
        [void]$suite.AppendChild($item)
    }
    $document.Save($Destination)
}

$editorPath = (Resolve-Path -LiteralPath $UnityEditor).Path
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$resultPath = [System.IO.Path]::GetFullPath($ResultsDirectory)
New-Item -ItemType Directory -Force -Path $resultPath | Out-Null
$tempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
$projectPath = Join-Path $tempRoot ('AssetLockTests-' + [guid]::NewGuid().ToString('N'))
if (-not ([System.IO.Path]::GetFullPath($projectPath).StartsWith($tempRoot, [System.StringComparison]::OrdinalIgnoreCase))) {
    throw 'Temporary project is outside the temporary directory.'
}

try {
    New-Item -ItemType Directory -Force -Path (Join-Path $projectPath 'Packages'), (Join-Path $projectPath 'Assets'), (Join-Path $projectPath 'ProjectSettings') | Out-Null
    Copy-Item -LiteralPath (Join-Path $repoRoot 'Packages/AssetLock') -Destination (Join-Path $projectPath 'Packages/AssetLock') -Recurse
    Copy-Item -LiteralPath (Join-Path $repoRoot 'ProjectSettings/ProjectVersion.txt') -Destination (Join-Path $projectPath 'ProjectSettings/ProjectVersion.txt')
    $manifest = @{
        dependencies = @{
            'com.haydenno.assetlock' = 'file:AssetLock'
            'com.unity.test-framework' = '1.1.33'
            'com.unity.settings-manager' = '2.0.1'
            'com.unity.nuget.newtonsoft-json' = '3.2.1'
        }
        testables = @('com.haydenno.assetlock')
    } | ConvertTo-Json -Depth 5
    [System.IO.File]::WriteAllText((Join-Path $projectPath 'Packages/manifest.json'), $manifest)
    $category = if ($Integration) { 'AssetLockIntegration' } else { 'AssetLockUnit' }
    $xmlPath = Join-Path $resultPath "$category.xml"
    $junitPath = Join-Path $resultPath "$category.junit.xml"
    foreach ($oldResult in @($xmlPath, $junitPath)) {
        if (Test-Path -LiteralPath $oldResult) { Remove-Item -LiteralPath $oldResult -Force }
    }
    $arguments = @('-batchmode', '-nographics', '-projectPath', "`"$projectPath`"", '-assetLockTestMode', '-runTests', '-testPlatform', 'EditMode', '-testCategory', $category, '-testResults', "`"$(Join-Path $resultPath "$category.xml")`"", '-logFile', "`"$(Join-Path $resultPath "$category.log")`"")
    $process = Start-Process -FilePath $editorPath -ArgumentList $arguments -WindowStyle Hidden -PassThru
    if (-not $process.WaitForExit(1200000)) {
        $process.Kill()
        throw 'Unity test run timed out after 20 minutes.'
    }
    if (-not (Test-Path -LiteralPath $xmlPath)) { throw "Unity did not create $xmlPath." }
    [xml]$result = Get-Content -LiteralPath $xmlPath -Raw
    Write-JUnitReport $result $junitPath
    if ($process.ExitCode -ne 0) { throw "Unity test run failed with exit code $($process.ExitCode). See $resultPath." }
    if ([int]$result.'test-run'.total -eq 0 -or [int]$result.'test-run'.failed -ne 0) { throw "No tests ran or tests failed; see $xmlPath." }
    Write-Output "Tests passed: $xmlPath"
}
finally {
    for ($attempt = 0; $attempt -lt 10 -and (Test-Path -LiteralPath $projectPath); $attempt++) {
        try { Remove-Item -LiteralPath $projectPath -Recurse -Force -ErrorAction Stop }
        catch {
            if ($attempt -eq 9) { Write-Warning "Could not remove temporary project: $projectPath" }
            else { Start-Sleep -Seconds 1 }
        }
    }
}
