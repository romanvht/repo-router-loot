param(
    [string]$GameDir = 'D:\SteamLibrary\steamapps\common\REPO',
    [string]$ProfileDir = (Join-Path $env:APPDATA 'r2modmanPlus-local\REPO\profiles\Default'),
    [string]$UnityEditor = 'C:\Program Files\Unity\Hub\Editor\2022.3.62f3\Editor\Unity.exe',
    [switch]$UseLocalDependencies,
    [switch]$NoPackage
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent

Push-Location $projectRoot

try {
    & (Join-Path $PSScriptRoot 'build-plugin.ps1') -GameDir $GameDir -ProfileDir $ProfileDir -UseLocalDependencies:$UseLocalDependencies

    $unityProject = Join-Path $projectRoot 'unity'
    $logPath = Join-Path $projectRoot 'obj\unity-build.log'
    if (-not (Test-Path -LiteralPath $UnityEditor -PathType Leaf)) {
        throw "Unity Editor not found: $UnityEditor"
    }

    $arguments = @('-batchmode', '-quit', '-projectPath', "`"$unityProject`"",
        '-executeMethod', 'RouterLoot.Editor.AssetBuild.Build', '-logFile', "`"$logPath`"")
    $process = Start-Process -FilePath $UnityEditor -ArgumentList $arguments -WindowStyle Hidden -PassThru
    $process.WaitForExit()
    if ($process.ExitCode -ne 0) { throw "Unity build failed. See $logPath" }

    if (-not $NoPackage) {
        & (Join-Path $PSScriptRoot 'package-zip.ps1')
    }
}
finally {
    Pop-Location
}
