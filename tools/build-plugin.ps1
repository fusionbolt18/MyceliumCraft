# Builds the Mycelium side of MyceliumCraft as a mod for someone's own Mycelium install (BepInEx 5),
# and packs it for friends: dist\MyceliumCraft-for-Mycelium.zip (see docs\INSTALL-FRIENDS.md).
#
# 1. Compiles bepinex\MyceliumCraft.Plugin.csproj (our mycelium\Runtime code + Plugin.cs) into
#    work\plugin\MyceliumCraft.dll. The game's DLLs are only read from its install, never copied.
# 2. Builds our shaders as work\bundle\myceliumcraft.shaders with Unity if they changed
#    (tools\build-mycelium.ps1 -BundleOnly).
# 3. Mycelium's newer code: tools\transplant merges the classes the post-release commits change
#    (from our own build's Assembly-CSharp, the newest commit: build it first with
#    build-mycelium.ps1) into the Steam release's Assembly-CSharp, keeping its saved-field layout:
#    work\newer\Assembly-CSharp.dll. Builds the patcher that swaps it in as the game starts.
# 4. Puts BepInEx (the downloaded release in work\downloads), the plugin, the shader bundle, the
#    patcher with the merged code and the install notes into dist\mycelium-mod\ and zips it.
# Nothing is written to any game install.
#
# Usage:  powershell -ExecutionPolicy Bypass -File tools\build-plugin.ps1
param(
    [string]$GameManaged = "C:\Program Files (x86)\Steam\steamapps\common\Mycelium\Mycelium_Data\Managed",
    [string]$BepInExVersion = "5.4.23.6"
)
$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$work = Join-Path $repo "work"
$bep = Join-Path $work "downloads\bepinex-$BepInExVersion\extracted"
if (-not (Test-Path (Join-Path $bep "BepInEx\core\BepInEx.dll"))) { throw "BepInEx $BepInExVersion not found in $bep (download BepInEx_win_x64_$BepInExVersion.zip from github.com/BepInEx/BepInEx/releases and unzip it there)" }
if (-not (Test-Path (Join-Path $GameManaged "Assembly-CSharp.dll"))) { throw "Mycelium's game files not found at $GameManaged" }

# 1. The plugin
$pluginOut = Join-Path $work "plugin"
& dotnet build (Join-Path $repo "bepinex\MyceliumCraft.Plugin.csproj") -c Release -o $pluginOut "-p:GameManaged=$GameManaged" "-p:BepInExCore=$bep\BepInEx\core" -nologo -v q
if ($LASTEXITCODE -ne 0) { throw "plugin build failed" }
Write-Host "Plugin: $pluginOut\MyceliumCraft.dll"

# 2. The shader bundle, if any shader is newer than it
$bundle = Join-Path $work "bundle\myceliumcraft.shaders"
$newest = Get-ChildItem (Join-Path $repo "mycelium\Runtime\Resources\*.shader") | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not (Test-Path $bundle) -or (Get-Item $bundle).LastWriteTime -lt $newest.LastWriteTime) {
    Write-Host "Shaders changed: building the bundle with Unity..."
    & powershell -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "build-mycelium.ps1") -BundleOnly
    if ($LASTEXITCODE -ne 0) { throw "shader bundle build failed" }
}
Write-Host "Shaders: $bundle"

# 3. Mycelium's newer code, merged into the release's, and the patcher that uses it
$newerSource = Join-Path $work "build\MyceliumCraft_Data\Managed\Assembly-CSharp.dll"
if (-not (Test-Path $newerSource)) { throw "no ${newerSource}: build our own Mycelium first (tools\build-mycelium.ps1)" }
$merged = Join-Path $work "newer\Assembly-CSharp.dll"
New-Item -ItemType Directory -Force (Split-Path $merged) | Out-Null
& dotnet run --project (Join-Path $repo "tools\transplant\Transplant.csproj") -c Release "-p:BepInExCore=$bep\BepInEx\core" -- merge (Join-Path $GameManaged "Assembly-CSharp.dll") $newerSource $merged
if ($LASTEXITCODE -ne 0) { throw "merging Mycelium's newer code failed" }
$patcherOut = Join-Path $work "patcher"
& dotnet build (Join-Path $repo "bepinex\patcher\MyceliumCraft.Patcher.csproj") -c Release -o $patcherOut "-p:BepInExCore=$bep\BepInEx\core" -nologo -v q
if ($LASTEXITCODE -ne 0) { throw "patcher build failed" }
Add-Type -Path (Join-Path $bep "BepInEx\core\Mono.Cecil.dll")
$releaseModule = [Mono.Cecil.ModuleDefinition]::ReadModule((Join-Path $GameManaged "Assembly-CSharp.dll"))
$releaseMvid = $releaseModule.Mvid.ToString()
$releaseModule.Dispose()
Write-Host "Merged code: $merged (replaces the release's $releaseMvid)"

# 4. The package
$dist = Join-Path $repo "dist"
$stage = Join-Path $dist "mycelium-mod"
if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
New-Item -ItemType Directory -Force $stage | Out-Null
Copy-Item -Recurse -Force (Join-Path $bep "*") $stage
$plugins = Join-Path $stage "BepInEx\plugins\MyceliumCraft"
New-Item -ItemType Directory -Force $plugins | Out-Null
Copy-Item (Join-Path $pluginOut "MyceliumCraft.dll"), $bundle $plugins
$patchers = Join-Path $stage "BepInEx\patchers\MyceliumCraft"
New-Item -ItemType Directory -Force (Join-Path $patchers "newer") | Out-Null
Copy-Item (Join-Path $patcherOut "MyceliumCraft.Patcher.dll") $patchers
Copy-Item $merged (Join-Path $patchers "newer")
Set-Content -Encoding ascii (Join-Path $patchers "replaces.txt") $releaseMvid
Copy-Item (Join-Path $repo "docs\INSTALL-FRIENDS.md") (Join-Path $stage "MyceliumCraft-INSTALL.txt")
Set-Content -Encoding utf8 (Join-Path $stage "BepInEx-LICENSE-NOTE.txt") @"
BepInEx $BepInExVersion (unchanged), from https://github.com/BepInEx/BepInEx/releases
BepInEx is free software under the GNU Lesser General Public License 2.1:
https://github.com/BepInEx/BepInEx/blob/master/LICENSE
"@
$zip = Join-Path $dist "MyceliumCraft-for-Mycelium.zip"
if (Test-Path $zip) { Remove-Item -Force $zip }
Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zip # "*" includes .doorstop_version too
$size = [math]::Round((Get-Item $zip).Length / 1KB)
Write-Host "Package: $zip ($size KB)"
