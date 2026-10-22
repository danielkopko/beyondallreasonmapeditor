<#
.SYNOPSIS
    Five Rivers: the map in parts small enough for GitHub, joined back into the map, and pushed.

.DESCRIPTION
    join    Rebuilds five_rivers_1.0.sd7 beside this script from parts\ and checks it against the SHA-256 in
            parts\five_rivers_1.0.sd7.sha256. With -Install, also copies it into BAR's maps folder, and its lobby
            picture into BAR's LuaMenu\Images\Minimaps.
    split   Cuts five_rivers_1.0.sd7 into parts\ again (after the map is rebuilt) and writes its SHA-256.
    push    split, then commits the parts, this folder, the drawings (out\koth-maps), PLAN.md and .gitignore,
            and pushes to origin, signing in as the repository's owner (from origin's address).

.EXAMPLE
    five_rivers.cmd join -Install
.EXAMPLE
    five_rivers.cmd push -Message "Five Rivers 1.1"
#>
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [ValidateSet('join', 'split', 'push')]
    [string]$Action,
    [switch]$Install,
    # BAR's data folder, the one that holds maps\ (its usual place when not given).
    [string]$BarData = '',
    [string]$Message = 'Five Rivers: the map parts updated',
    # GitHub refuses files over 100 MB and warns over 50 MB.
    [int]$PartMB = 45
)

$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$mapName = 'five_rivers_1.0.sd7'
$map = Join-Path $here $mapName
$parts = Join-Path $here 'parts'
$sumFile = Join-Path $parts "$mapName.sha256"
$picture = Join-Path $here 'bar-data\LuaMenu\Images\Minimaps\Five_Rivers_1.0.jpg'

function Get-Sha256([string]$Path) {
    (Get-FileHash -Algorithm SHA256 -LiteralPath $Path).Hash.ToLowerInvariant()
}

function Split-Map {
    if (-not (Test-Path -LiteralPath $map)) { throw "$mapName isn't beside this script: build it, or join the parts first." }
    New-Item -ItemType Directory -Force $parts | Out-Null
    Get-ChildItem -LiteralPath $parts -Filter "$mapName.*" | Remove-Item -Force
    $size = [long]$PartMB * 1MB
    $buffer = New-Object byte[] (4MB)
    $n = 0
    $in = [System.IO.File]::OpenRead($map)
    try {
        while ($in.Position -lt $in.Length) {
            $n++
            $out = [System.IO.File]::Create((Join-Path $parts ('{0}.{1:000}' -f $mapName, $n)))
            try {
                $left = [Math]::Min($size, $in.Length - $in.Position)
                while ($left -gt 0) {
                    $read = $in.Read($buffer, 0, [int][Math]::Min([long]$buffer.Length, $left))
                    if ($read -le 0) { throw 'The map file ended early.' }
                    $out.Write($buffer, 0, $read)
                    $left -= $read
                }
            }
            finally { $out.Dispose() }
        }
    }
    finally { $in.Dispose() }
    $hash = Get-Sha256 $map
    $length = (Get-Item -LiteralPath $map).Length
    "$hash  $mapName  $length bytes  $n parts" | Set-Content -LiteralPath $sumFile -Encoding Ascii
    Write-Host "Split $mapName ($([Math]::Round($length / 1MB)) MB) into $n parts in parts\ (SHA-256 $hash)."
}

function Join-Map {
    $files = @(Get-ChildItem -LiteralPath $parts -Filter "$mapName.*" | Where-Object { $_.Name -match '\.\d{3}$' } | Sort-Object Name)
    if ($files.Count -eq 0) { throw "There are no parts in $parts." }
    $expected = ((Get-Content -LiteralPath $sumFile -TotalCount 1) -split '\s+')[0]
    $joining = "$map.joining"
    $out = [System.IO.File]::Create($joining)
    try {
        foreach ($f in $files) {
            $in = [System.IO.File]::OpenRead($f.FullName)
            try { $in.CopyTo($out) } finally { $in.Dispose() }
        }
    }
    finally { $out.Dispose() }
    $hash = Get-Sha256 $joining
    if ($hash -ne $expected) {
        Remove-Item -LiteralPath $joining -Force
        throw "The joined map's SHA-256 is $hash, not ${expected}: a part is missing or damaged (pull again)."
    }
    Move-Item -LiteralPath $joining $map -Force
    Write-Host "Joined $($files.Count) parts into $mapName; its SHA-256 matches."
}

function Install-Map {
    $data = $BarData
    if (-not $data) { $data = Join-Path $env:LOCALAPPDATA 'Programs\Beyond-All-Reason\data' }
    if (-not (Test-Path -LiteralPath $data)) { throw "BAR's data folder isn't at $data; give it with -BarData <folder>." }
    $mapsDir = Join-Path $data 'maps'
    New-Item -ItemType Directory -Force $mapsDir | Out-Null
    Copy-Item -LiteralPath $map $mapsDir -Force
    $minimaps = Join-Path $data 'LuaMenu\Images\Minimaps'
    New-Item -ItemType Directory -Force $minimaps | Out-Null
    Copy-Item -LiteralPath $picture $minimaps -Force
    Write-Host "Installed $mapName in $mapsDir, and its lobby picture; restart BAR's lobby to see it."
}

function Invoke-Git {
    # git writes progress to stderr; only its exit code says whether it failed.
    $saved = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    & git -C $script:root @args
    $code = $LASTEXITCODE
    $ErrorActionPreference = $saved
    if ($code -ne 0) { throw "git $($args -join ' ') failed (exit $code)." }
}

function Push-Map {
    Split-Map
    $script:root = (& git -C $here rev-parse --show-toplevel).Trim()
    Invoke-Git add -- out/step44-five-rivers out/koth-maps PLAN.md .gitignore
    $staged = & git -C $script:root diff --cached --name-only
    if ($staged) { Invoke-Git commit -m $Message } else { Write-Host 'Nothing new to commit.' }
    # Sign in as the repository's owner: git may be set to another GitHub account first (a work account), which
    # can't see a private repository.
    $url = (& git -C $script:root remote get-url origin).Trim()
    $owner = ''
    if ($url -match 'github\.com[/:]([^/]+)/') { $owner = $Matches[1] }
    if ($owner -and (Test-GhAccount $owner)) {
        # The GitHub CLI holds the owner's sign-in: git gets that account's token for this push only (nothing saved).
        $helper = "!f() { echo username=$owner; echo password=`$(gh auth token --user $owner); }; f"
        Invoke-Git -c 'credential.https://github.com.helper=' -c "credential.https://github.com.helper=$helper" push origin HEAD
    }
    elseif ($owner) { Invoke-Git -c "credential.username=$owner" push origin HEAD }
    else { Invoke-Git push origin HEAD }
    Write-Host 'Pushed.'
}

function Test-GhAccount([string]$User) {
    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { return $false }
    $saved = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    & gh auth token --user $User 2>$null | Out-Null
    $ok = $LASTEXITCODE -eq 0
    $ErrorActionPreference = $saved
    return $ok
}

switch ($Action) {
    'split' { Split-Map }
    'join' { Join-Map; if ($Install) { Install-Map } }
    'push' { Push-Map }
}
