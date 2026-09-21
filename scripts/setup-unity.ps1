param(
    [string]$GameDir = 'D:\SteamLibrary\steamapps\common\REPO',
    [string]$UnityEditor = 'C:\Program Files\Unity\Hub\Editor\2022.3.62f3\Editor\Unity.exe',
    [string]$ProfileDir
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$unityProject = Join-Path $projectRoot 'unity'
$dependencies = Join-Path $projectRoot '.deps'
$logs = Join-Path $projectRoot 'obj'

foreach ($path in @($UnityEditor, (Join-Path $GameDir 'REPO_Data\Managed\Assembly-CSharp.dll'))) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "File not found: $path" }
}

New-Item -ItemType Directory -Force -Path $dependencies, $logs | Out-Null
if ($ProfileDir) {
    Copy-Item -LiteralPath (Join-Path $ProfileDir 'BepInEx\core\BepInEx.dll') -Destination $dependencies
    $repolib = @(Get-ChildItem -LiteralPath (Join-Path $ProfileDir 'BepInEx\plugins') -Recurse -File -Filter REPOLib.dll)
    if ($repolib.Count -ne 1) { throw 'Expected one REPOLib.dll in the profile.' }
    Copy-Item -LiteralPath $repolib[0].FullName -Destination $dependencies
}

foreach ($file in @('BepInEx.dll', 'REPOLib.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $dependencies $file))) {
        throw "Missing $file. Pass -ProfileDir with a BepInEx and REPOLib profile."
    }
}

function Invoke-Unity([string]$method, [string]$logName, [switch]$Quit) {
    $logPath = Join-Path $logs $logName
    $arguments = @('-batchmode', '-nographics', '-projectPath', "`"$unityProject`"",
        '-executeMethod', $method, '-gameDir', "`"$GameDir`"", '-logFile', "`"$logPath`"")
    if ($Quit) { $arguments += '-quit' }
    $process = Start-Process -FilePath $UnityEditor -ArgumentList $arguments -WindowStyle Hidden -PassThru
    $process.WaitForExit()
    if ($process.ExitCode -ne 0) { throw "Unity failed. See $logPath" }
}

$manifestPath = Join-Path $unityProject 'Packages\manifest.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json -AsHashtable
$complete = Join-Path $unityProject 'Assets\REPO\.router-loot-ready'
if (-not (Test-Path -LiteralPath $complete)) {
    $manifest.dependencies.Remove('zehs.repolib-sdk') | Out-Null
    $manifest | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $manifestPath -Encoding utf8
    $settingsPath = Join-Path $unityProject 'ProjectSettings\ProjectSettings.asset'
    $settings = [IO.File]::ReadAllText($settingsPath).Replace('ROUTER_LOOT_READY', '')
    [IO.File]::WriteAllText($settingsPath, $settings)

    for ($pass = 1; $pass -le 24; $pass++) {
        Write-Output "Preparing Unity project: pass $pass"
        Invoke-Unity 'RouterLoot.Editor.ProjectSetup.Run' "unity-setup-$pass.log"
        if (Test-Path -LiteralPath $complete) { break }
    }
    if (-not (Test-Path -LiteralPath $complete)) { throw 'Unity setup did not finish. See obj/unity-setup-*.log.' }
}

$plugins = Join-Path $unityProject 'Assets\Plugins'
New-Item -ItemType Directory -Force -Path $plugins | Out-Null
Copy-Item -LiteralPath (Join-Path $dependencies 'REPOLib.dll') -Destination $plugins

$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json -AsHashtable
$manifest.dependencies['zehs.repolib-sdk'] = 'https://github.com/ZehsTeam/REPOLib-Sdk.git#caa0cb223e623f72b2d5fb55732ed00aeaa9b075'
$manifest.dependencies['com.unity.cloud.gltfast'] = '6.14.1'
$manifest.dependencies['com.nomnom.unity-project-patcher-bepinex'] = 'https://github.com/Kesomannen/unity-project-patcher-bepinex.git#a26292a6b088402b2843dfa058880d7d2c5b7807'
$manifest | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $manifestPath -Encoding utf8
Invoke-Unity 'RouterLoot.Editor.ProjectSetup.Finish' 'unity-setup-finish.log' -Quit
Write-Output "Ready: $unityProject"
