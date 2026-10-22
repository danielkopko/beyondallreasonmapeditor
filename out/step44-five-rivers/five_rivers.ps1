<#
.SYNOPSIS
    Five Rivers: publish the map as a GitHub release (one download, no clone), or install it into BAR.

.DESCRIPTION
    publish  Commits and pushes this folder's notes, the drawings (out\koth-maps), PLAN.md and .gitignore, then
             puts five_rivers_<version>.sd7 and its lobby picture on the GitHub release five-rivers-<version>,
             in this repository and in each of -AlsoTo (danielkopko/BarMaps, public): made with release_notes.md
             if it isn't there, its files replaced if it is. Signs in as the repository's owner, from origin's
             address. Prints the download links.
    install  Copies five_rivers_<version>.sd7 (beside this script) into BAR's maps folder, and its lobby picture
             into BAR's LuaMenu\Images\Minimaps.

.EXAMPLE
    five_rivers.cmd publish -Message "Five Rivers 1.0: wider ramps"
.EXAMPLE
    five_rivers.cmd install
#>
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [ValidateSet('publish', 'install')]
    [string]$Action,
    [string]$Version = '1.0',
    [string]$Message = 'Five Rivers: notes updated',
    # BAR's data folder, the one that holds maps\ (its usual place when not given).
    [string]$BarData = '',
    # Other repositories (owner/name, the same owner) that get the release too.
    [string[]]$AlsoTo = @('danielkopko/BarMaps')
)

$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$mapName = "five_rivers_$Version.sd7"
$map = Join-Path $here $mapName
$picture = Join-Path $here "bar-data\LuaMenu\Images\Minimaps\Five_Rivers_$Version.jpg"
$tag = "five-rivers-$Version"

function Invoke-Native([string]$Exe) {
    # git and gh write progress to stderr; only the exit code says whether they failed.
    $rest = $args
    $saved = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    & $Exe @rest
    $code = $LASTEXITCODE
    $ErrorActionPreference = $saved
    if ($code -ne 0) { throw "$Exe $($rest -join ' ') failed (exit $code)." }
}

function Test-Native([string]$Exe) {
    $rest = $args
    $saved = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    & $Exe @rest 2>$null | Out-Null
    $ok = $LASTEXITCODE -eq 0
    $ErrorActionPreference = $saved
    return $ok
}

function Publish-Map {
    if (-not (Test-Path -LiteralPath $map)) { throw "$mapName isn't beside this script: build it first." }
    $root = (& git -C $here rev-parse --show-toplevel).Trim()
    $url = (& git -C $root remote get-url origin).Trim()
    if ($url -notmatch 'github\.com[/:]([^/]+)/([^/]+?)(\.git)?$') { throw "origin ($url) isn't a GitHub repository." }
    $owner = $Matches[1]
    $repo = "$owner/$($Matches[2])"
    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { throw 'The GitHub CLI (gh) is needed: https://cli.github.com' }
    if (-not (Test-Native gh auth token --user $owner)) { throw "The GitHub CLI isn't signed in as ${owner}: run gh auth login, and choose that account." }

    # The notes and sources: committed and pushed as the owner (git may be set to another GitHub account first,
    # a work account, which can't see a private repository). The token goes from gh to git for this push only.
    Invoke-Native git -C $root add -- out/step44-five-rivers out/koth-maps PLAN.md .gitignore
    if (& git -C $root diff --cached --name-only) { Invoke-Native git -C $root commit -m $Message } else { Write-Host 'Nothing new to commit.' }
    $helper = "!f() { echo username=$owner; echo password=`$(gh auth token --user $owner); }; f"
    Invoke-Native git -C $root -c 'credential.https://github.com.helper=' -c "credential.https://github.com.helper=$helper" push origin HEAD

    # The map itself, on the release in each repository (on its default branch when the release is new).
    $files = @($map)
    if (Test-Path -LiteralPath $picture) { $files += $picture }
    $notes = Join-Path $here 'release_notes.md'
    $env:GH_TOKEN = (& gh auth token --user $owner)
    try {
        foreach ($r in @($repo) + $AlsoTo) {
            if (Test-Native gh release view $tag -R $r) {
                Invoke-Native gh release upload $tag @files -R $r --clobber
                Invoke-Native gh release edit $tag -R $r --title "Five Rivers $Version" --notes-file $notes
            }
            else { Invoke-Native gh release create $tag @files -R $r --title "Five Rivers $Version" --notes-file $notes }
            Write-Host "Published to $r. Download: https://github.com/$r/releases/download/$tag/$mapName"
        }
    }
    finally { Remove-Item Env:GH_TOKEN -ErrorAction SilentlyContinue }
}

function Install-Map {
    if (-not (Test-Path -LiteralPath $map)) { throw "$mapName isn't beside this script: download it from the release, or build it." }
    $data = $BarData
    if (-not $data) { $data = Join-Path $env:LOCALAPPDATA 'Programs\Beyond-All-Reason\data' }
    if (-not (Test-Path -LiteralPath $data)) { throw "BAR's data folder isn't at $data; give it with -BarData <folder>." }
    $mapsDir = Join-Path $data 'maps'
    New-Item -ItemType Directory -Force $mapsDir | Out-Null
    Copy-Item -LiteralPath $map $mapsDir -Force
    if (Test-Path -LiteralPath $picture) {
        $minimaps = Join-Path $data 'LuaMenu\Images\Minimaps'
        New-Item -ItemType Directory -Force $minimaps | Out-Null
        Copy-Item -LiteralPath $picture $minimaps -Force
    }
    Write-Host "Installed $mapName in $mapsDir, with its lobby picture; restart BAR's lobby to see it."
}

switch ($Action) {
    'publish' { Publish-Map }
    'install' { Install-Map }
}
