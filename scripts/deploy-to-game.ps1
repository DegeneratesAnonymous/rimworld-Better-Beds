<#
  Builds Wil's Better Beds for 1.7 and 1.6 and copies the mod into RimWorld's Mods folder.

  Why a copy and not a junction or symlink: RimWorld 1.7 runs on .NET 10, which refuses to
  enumerate a folder reached through a junction ("The path cannot be traversed because it
  contains an untrusted mount point"). The mod list then fails to build and the game starts to a
  black screen. Always give the game a real folder.

  Usage:   scripts\deploy-to-game.ps1                  (build, then copy)
           scripts\deploy-to-game.ps1 -NoBuild         (copy what is already built)
           scripts\deploy-to-game.ps1 -Game "D:\Games\RimWorld"
#>
param(
    [string]$Game = "B:\SteamLibrary\steamapps\common\RimWorld",
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$dest = Join-Path $Game 'Mods\SmarterBeds'

if (-not (Test-Path (Join-Path $Game 'Mods'))) { throw "No Mods folder at $Game. Pass -Game with your RimWorld folder." }

# The game keeps the mod's DLL open while it runs, so copying over it fails (and robocopy's
# default is to retry for a very long time). Close the game first.
if (Get-Process -Name 'RimWorld*' -ErrorAction SilentlyContinue) {
    throw "RimWorld is running and has the mod's DLL locked. Close the game, then run this again."
}

if ((Test-Path $dest) -and (Get-Item $dest -Force).LinkType) {
    throw "$dest is a junction or symlink, which RimWorld 1.7 cannot read. Delete the link (not its contents) and run again."
}

if (-not $NoBuild) {
    foreach ($v in '1.7', '1.6') {
        Write-Host "Building $v..."
        dotnet build "$repo\Source\SmarterBeds\SmarterBeds.csproj" -c Release -p:GameVersion=$v -v q
        if ($LASTEXITCODE -ne 0) { throw "Build failed for $v" }
    }
}

# Only what the game needs: no source, no git data, no docs.
robocopy $repo $dest /MIR /XD .git .github Source docs scripts obj bin /XF *.md .gitignore *.deps.json /R:2 /W:1 /NFL /NDL /NJH /NJS | Out-Null
if ($LASTEXITCODE -ge 8) { throw "Copy failed (robocopy exit code $LASTEXITCODE)" }
$global:LASTEXITCODE = 0   # robocopy uses 1-7 for "copied something", which is success

Write-Host "Deployed to $dest"
Write-Host "In RimSort: Refresh, make sure ""Wil's Better Beds"" (SpacemanWil.SmarterBeds) is active, Sort, Save."
