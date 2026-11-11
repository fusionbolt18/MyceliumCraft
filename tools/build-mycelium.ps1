# Builds work\build\MyceliumCraft.exe: Mycelium plus the MyceliumCraft package.
#
# 1. Copies Mycelium's project (Assets, Packages, ProjectSettings) into work\Mycelium. The first
#    time it also copies Library (Unity's import cache) so Unity doesn't re-import everything.
#    Your Mycelium folder is only read, never written.
# 2. Adds this repo's mycelium\ folder to the copy's Packages\manifest.json.
# 3. Runs Unity 2022.3.0f1 in batch mode (no window) to build. Log: work\unity-build.log
#
# Usage:  powershell -ExecutionPolicy Bypass -File tools\build-mycelium.ps1
#         ... -BundleOnly   builds only our shaders, as work\bundle\myceliumcraft.shaders (for the BepInEx plugin)
param(
    [switch]$BundleOnly,
    [string]$MyceliumSource = "C:\Users\Ryan\Documents\ClonedGithubRepos\Mycelium",
    [string]$Unity = "C:\Program Files\Unity\Hub\Editor\2022.3.0f1\Editor\Unity.exe"
)
$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$work = Join-Path $repo "work"
$project = Join-Path $work "Mycelium"
$out = Join-Path $work "build"
$log = Join-Path $work "unity-build.log"

if (-not (Test-Path $Unity)) { throw "Unity not found at $Unity" }
New-Item -ItemType Directory -Force $project | Out-Null

# robocopy exit codes 0-7 mean success.
foreach ($dir in "Assets", "Packages", "ProjectSettings") {
    robocopy (Join-Path $MyceliumSource $dir) (Join-Path $project $dir) /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "copying $dir failed (robocopy $LASTEXITCODE)" }
}
if (-not (Test-Path (Join-Path $project "Library"))) {
    Write-Host "First build: copying Unity's Library cache (a few GB)..."
    robocopy (Join-Path $MyceliumSource "Library") (Join-Path $project "Library") /E /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "copying Library failed (robocopy $LASTEXITCODE)" }
}

# Point the copy at our package (a "file:" reference, so edits in mycelium\ are picked up directly).
$manifestPath = Join-Path $project "Packages\manifest.json"
$manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
$pkg = "file:" + ((Join-Path $repo "mycelium") -replace '\\', '/')
$manifest.dependencies | Add-Member -NotePropertyName "dev.myceliumcraft" -NotePropertyValue $pkg -Force
[IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 10), (New-Object Text.UTF8Encoding $false))
Write-Host "Package: $pkg"

Write-Host "Building with Unity (this can take a while; log: $log)..."
$started = Get-Date
if ($BundleOnly) { $out = Join-Path $work "bundle" }
$method = if ($BundleOnly) { "MyceliumCraft.Editor.BuildMyceliumCraft.BuildShaderBundle" } else { "MyceliumCraft.Editor.BuildMyceliumCraft.Build" }
$p = Start-Process -FilePath $Unity -Wait -PassThru -ArgumentList @(
    "-batchmode", "-nographics",
    "-projectPath", "`"$project`"",
    "-executeMethod", $method,
    "-mcOut", "`"$out`"",
    "-logFile", "`"$log`""
)
$mins = [math]::Round(((Get-Date) - $started).TotalMinutes, 1)
if ($p.ExitCode -ne 0) {
    Write-Host "BUILD FAILED (Unity exit code $($p.ExitCode), $mins min). Last lines of the log:"
    Get-Content $log -Tail 40
    exit 1
}
if ($BundleOnly) { Write-Host "Built $out\myceliumcraft.shaders in $mins min" } else { Write-Host "Built $out\MyceliumCraft.exe in $mins min" }
