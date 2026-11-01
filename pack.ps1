# Builds the mod and zips a Thunderstore package into release/.
# Usage: .\pack.ps1 -ValheimDir "D:\Steam\steamapps\common\Valheim" -BepInExCore "<r2modman profile>\BepInEx\core"
param(
    [string]$ValheimDir = "Z:\SteamLibrary\steamapps\common\Valheim",
    [string]$BepInExCore = "$env:APPDATA\r2modmanPlus-local\Valheim\profiles\Valheim 1.0\BepInEx\core"
)
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
dotnet build "$root\src\CraftingAndCookingDecluttered.csproj" -c Release "-p:ValheimDir=$ValheimDir" "-p:BepInExCore=$BepInExCore"
if ($LASTEXITCODE -ne 0) { throw "build failed" }

$version = (Get-Content "$root\manifest.json" -Raw | ConvertFrom-Json).version_number
$stage = "$root\release\stage"
Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory "$stage\plugins" | Out-Null
Copy-Item "$root\manifest.json", "$root\README.md", "$root\CHANGELOG.md" $stage
Copy-Item "$root\art\Icon.png" "$stage\icon.png"
Copy-Item "$root\src\bin\Release\netstandard2.1\CraftingAndCookingDecluttered.dll" "$stage\plugins"
Copy-Item "$root\licenses" "$stage\plugins\licenses" -Recurse
Copy-Item "$root\LICENSE" "$stage\plugins\licenses\LICENSE.txt"

$zip = "$root\release\Crafting_and_Cooking_Decluttered-$version.zip"
Remove-Item $zip -ErrorAction SilentlyContinue
# Compress-Archive on Windows PowerShell writes backslash paths, so entries are added by hand.
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::Open($zip, "Create")
Get-ChildItem $stage -Recurse -File | ForEach-Object {
    $entry = $_.FullName.Substring($stage.Length + 1).Replace("\", "/")
    [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $_.FullName, $entry)
}
$archive.Dispose()
Remove-Item $stage -Recurse -Force
"Packed $zip"
