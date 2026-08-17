param(
    [string]$GameDir = "",
    [ValidateSet("Release", "Debug", "Dev")]
    [string]$Configuration = "Release",
    [switch]$Package,
    [switch]$SkipWorkshopCheck
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Write-Step([string]$Text) {
    Write-Host "`n==> $Text" -ForegroundColor Cyan
}

function Add-Candidate([System.Collections.Generic.List[string]]$List, [string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) { return }
    try {
        $p = [System.IO.Path]::GetFullPath($Path.Trim('"')).TrimEnd('\')
        if (-not $List.Contains($p)) { $List.Add($p) }
    } catch { }
}

function Find-BigAmbitionsInstall {
    param([string]$Explicit)

    if (-not [string]::IsNullOrWhiteSpace($Explicit)) {
        $p = [System.IO.Path]::GetFullPath($Explicit).TrimEnd('\')
        $probe = Join-Path $p "Big Ambitions_Data\Managed\BigAmbitions.dll"
        if (-not (Test-Path $probe)) {
            throw "-GameDir does not look like a Big Ambitions install: $p (missing $probe)"
        }
        return $p
    }

    $libraries = New-Object 'System.Collections.Generic.List[string]'

    foreach ($reg in @(
        'HKCU:\Software\Valve\Steam',
        'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam',
        'HKLM:\SOFTWARE\Valve\Steam'
    )) {
        try {
            $v = Get-ItemProperty -Path $reg -ErrorAction Stop
            if ($v.SteamPath) { Add-Candidate $libraries ([string]$v.SteamPath) }
            if ($v.InstallPath) { Add-Candidate $libraries ([string]$v.InstallPath) }
        } catch { }
    }

    if (${env:ProgramFiles(x86)}) { Add-Candidate $libraries (Join-Path ${env:ProgramFiles(x86)} 'Steam') }
    if ($env:ProgramFiles) { Add-Candidate $libraries (Join-Path $env:ProgramFiles 'Steam') }

    foreach ($steam in @($libraries)) {
        $vdf = Join-Path $steam 'steamapps\libraryfolders.vdf'
        if (-not (Test-Path $vdf)) { continue }
        try {
            $raw = Get-Content $vdf -Raw
            foreach ($m in [regex]::Matches($raw, '"path"\s+"([^"]+)"')) {
                $lib = $m.Groups[1].Value -replace '\\\\', '\'
                Add-Candidate $libraries $lib
            }
        } catch { }
    }

    foreach ($lib in $libraries) {
        $game = Join-Path $lib 'steamapps\common\Big Ambitions'
        $probe = Join-Path $game 'Big Ambitions_Data\Managed\BigAmbitions.dll'
        if (Test-Path $probe) { return $game }
    }

    throw "Big Ambitions was not found in Steam libraries. Re-run with -GameDir 'D:\...\steamapps\common\Big Ambitions'."
}

function Find-WorkshopDuplicates {
    param([string]$GamePath)

    $hits = New-Object 'System.Collections.Generic.List[string]'
    try {
        $common = Split-Path $GamePath -Parent
        $steamapps = Split-Path $common -Parent
        $workshop = Join-Path $steamapps 'workshop\content\1331550'
        if (-not (Test-Path $workshop)) { return $hits }

        Get-ChildItem $workshop -Directory -ErrorAction SilentlyContinue | ForEach-Object {
            $dir = $_.FullName
            $dlls = Get-ChildItem $dir -Filter 'BigAmbitionsMP.dll' -File -Recurse -ErrorAction SilentlyContinue
            foreach ($dll in $dlls) { $hits.Add($dll.FullName) }
        }
    } catch { }
    return $hits
}

function Get-CsprojManagedReferences {
    param([string]$ProjectPath)

    $names = New-Object 'System.Collections.Generic.List[string]'
    try {
        [xml]$xml = Get-Content $ProjectPath -Raw
        foreach ($node in $xml.SelectNodes('//Reference/HintPath')) {
            $text = [string]$node.InnerText
            if ($text.StartsWith('$(ManagedDir)', [System.StringComparison]::OrdinalIgnoreCase)) {
                $name = $text.Substring('$(ManagedDir)'.Length).TrimStart('\', '/')
                if ($name -and -not $names.Contains($name)) { $names.Add($name) }
            }
        }
    } catch {
        throw "Could not inspect ManagedDir references in BigAmbitionsMP.csproj: $($_.Exception.Message)"
    }
    return $names
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $repoRoot 'BigAmbitionsMP.csproj'
if (-not (Test-Path $project)) { throw "BigAmbitionsMP.csproj not found at $project" }

Write-Step "Preflight"
try {
    $dotnet = & dotnet --version
    if ($LASTEXITCODE -ne 0) { throw "dotnet returned exit code $LASTEXITCODE" }
    Write-Host "dotnet SDK: $dotnet"
} catch {
    throw ".NET SDK is required. 'dotnet --version' failed. Install a current .NET SDK, then retry."
}

$resolvedGame = Find-BigAmbitionsInstall -Explicit $GameDir
$managed = Join-Path $resolvedGame 'Big Ambitions_Data\Managed'
Write-Host "Game: $resolvedGame"
Write-Host "Managed: $managed"

# Verify every game/Unity DLL the project actually references. This makes a wrong
# Steam branch/game build fail here with a short useful list instead of producing
# thousands of cascading CS0246/CS0103 errors.
$managedRefs = @(Get-CsprojManagedReferences -ProjectPath $project)
$missing = @($managedRefs | Where-Object { -not (Test-Path (Join-Path $managed $_)) })
if ($missing.Count -gt 0) {
    Write-Host "`nThe installed game does not contain all assemblies required by this source branch:" -ForegroundColor Red
    foreach ($m in $missing) { Write-Host "  - $m" -ForegroundColor Red }
    throw "Managed assembly preflight failed ($($missing.Count) missing). Confirm Big Ambitions EA 0.11 Mono/experimental branch and verify game files."
}
Write-Host "Managed assembly check: $($managedRefs.Count) referenced DLLs present" -ForegroundColor Green

if (-not $SkipWorkshopCheck) {
    $dupes = @(Find-WorkshopDuplicates -GamePath $resolvedGame)
    if ($dupes.Count -gt 0) {
        Write-Host "`nWARNING: Workshop BigAmbitionsMP.dll copy/copies detected:" -ForegroundColor Yellow
        foreach ($d in $dupes) { Write-Host "  $d" -ForegroundColor Yellow }
        Write-Host "For the competitive test, unsubscribe/disable the Workshop Going Public copy or remove the local duplicate. Keep ONE BigAmbitionsMP install." -ForegroundColor Yellow
    } else {
        Write-Host "Workshop duplicate check: clear" -ForegroundColor Green
    }
}

try {
    $branch = (& git -C $repoRoot rev-parse --abbrev-ref HEAD 2>$null).Trim()
    $commit = (& git -C $repoRoot rev-parse --short=12 HEAD 2>$null).Trim()
    if ($branch) { Write-Host "Git: $branch @ $commit" }
    if ($branch -and $branch -ne 'feature/competitive-dashboard') {
        Write-Host "WARNING: expected feature/competitive-dashboard, current branch is '$branch'." -ForegroundColor Yellow
    }
} catch { }

$running = @(Get-Process -ErrorAction SilentlyContinue | Where-Object {
    $_.ProcessName -like 'Big Ambitions*' -or $_.ProcessName -like 'BigAmbitions*'
})
if ($running.Count -gt 0) {
    throw "Big Ambitions appears to be running. Close the game before building/deploying the mod."
}

Write-Step "Build $Configuration"

# Windows PowerShell 5 native-command quoting has a nasty edge case when an
# argument containing spaces ends in a backslash. The old script passed
#   -p:GameDir=E:\...\Big Ambitions\
# and MSBuild could silently fall back to the csproj's default C: path, causing
# every game/Unity reference to disappear and ~thousands of cascade errors.
# Forward slashes avoid that quoting ambiguity. Pass ManagedDir explicitly too,
# so the compiler does not depend on path concatenation inside the project file.
$gameProp = (($resolvedGame -replace '\\', '/').TrimEnd('/')) + '/'
$managedProp = (($managed -replace '\\', '/').TrimEnd('/')) + '/'
Write-Host "MSBuild GameDir:    $gameProp"
Write-Host "MSBuild ManagedDir: $managedProp"

& dotnet build $project -c $Configuration "-p:GameDir=$gameProp" "-p:ManagedDir=$managedProp"
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed with exit code $LASTEXITCODE" }

$deployDir = Join-Path $env:LOCALAPPDATA 'Low\Hovgaard Games\Big Ambitions\ModsLocal\BigAmbitionsMP'
$dll = Join-Path $deployDir 'BigAmbitionsMP.dll'
if (-not (Test-Path $dll)) {
    throw "Build succeeded but deployed DLL was not found at: $dll"
}

$hash = (Get-FileHash $dll -Algorithm SHA256).Hash
$size = (Get-Item $dll).Length
Write-Step "Deployment verified"
Write-Host "Mod folder: $deployDir"
Write-Host "DLL:        $dll"
Write-Host "DLL size:   $size bytes"
Write-Host "SHA-256:    $hash" -ForegroundColor Green

$license = Join-Path $deployDir 'LICENSE'
$deps = Join-Path $deployDir 'Dependencies'
if (-not (Test-Path $license)) { Write-Host "WARNING: deployed LICENSE is missing." -ForegroundColor Yellow }
if (-not (Test-Path $deps)) { Write-Host "WARNING: deployed Dependencies folder is missing." -ForegroundColor Yellow }

if ($Package) {
    Write-Step "Package for Player 2"
    $dist = Join-Path $repoRoot 'dist'
    New-Item -ItemType Directory -Path $dist -Force | Out-Null
    $zip = Join-Path $dist 'BigAmbitionsMP-Competitive.zip'
    if (Test-Path $zip) { Remove-Item $zip -Force }

    $stage = Join-Path $env:TEMP ('bamp-competitive-' + [guid]::NewGuid().ToString('N'))
    $stageMod = Join-Path $stage 'BigAmbitionsMP'
    try {
        New-Item -ItemType Directory -Path $stage -Force | Out-Null
        Copy-Item $deployDir $stageMod -Recurse -Force
        Compress-Archive -Path $stageMod -DestinationPath $zip -CompressionLevel Optimal
    } finally {
        Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
    }

    $zipHash = (Get-FileHash $zip -Algorithm SHA256).Hash
    Write-Host "Package:     $zip" -ForegroundColor Green
    Write-Host "ZIP SHA-256: $zipHash" -ForegroundColor Green
    Write-Host "Player 2: extract BigAmbitionsMP into %LOCALAPPDATA%\Low\Hovgaard Games\Big Ambitions\ModsLocal\ and keep the Workshop copy disabled." -ForegroundColor Cyan
}

Write-Host "`nCompetitive build preflight finished successfully." -ForegroundColor Green
