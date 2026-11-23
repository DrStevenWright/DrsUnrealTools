<#
.SYNOPSIS
    Releases UE Test Tool: tags main, waits for GitHub Actions to build the release, and installs the new exe.

.DESCRIPTION
    Run from a clean, up-to-date main whose CI build has passed (merge your PR first). The script:
      1. checks the version, the branch, the working tree, and that the tag is new and newer than the last one;
      2. waits for (or checks) the CI build of main's latest commit;
      3. creates and pushes the tag v<Version>; GitHub Actions then tests, builds and creates the release;
      4. waits for that build and checks the release has UETestTool.exe;
      5. installs it into -InstallDir (asking you to close the app if it's running) and adds a Start menu
         shortcut if there isn't one.
    The version shown in the app and in the exe's file properties comes from the tag (MinVer).

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts\release.ps1 0.3.0

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts\release.ps1 0.3.0 -DryRun
    Runs every check and says what it would do, without tagging or installing.
#>
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string] $Version,

    [string] $InstallDir = (Join-Path $env:LOCALAPPDATA 'Programs\UE Test Tool'),

    [switch] $NoInstall,

    [switch] $DryRun
)

# Native tools (git, gh) report progress on stderr, so don't treat stderr as failure; check exit codes instead.
$ErrorActionPreference = 'Continue'

function Step([string] $message) { Write-Host "==> $message" -ForegroundColor Cyan }
function Info([string] $message) { Write-Host "    $message" }
function Fail([string] $message) { Write-Host "ERROR: $message" -ForegroundColor Red; exit 1 }

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

function Invoke-Git {
    $output = & git -C $root @args
    if ($LASTEXITCODE -ne 0) { Fail "git $($args -join ' ') failed." }
    return $output
}

function Invoke-Gh {
    $output = & gh @args
    if ($LASTEXITCODE -ne 0) { Fail "gh $($args -join ' ') failed." }
    return $output
}

# Waits for the Build workflow run for a commit (main) or a tag to appear, then for it to finish. Returns the run.
function Wait-BuildRun([string] $filterName, [string] $filterValue, [string] $what) {
    $deadline = (Get-Date).AddMinutes(3)
    while ($true) {
        $runs = Invoke-Gh run list --repo $repo --workflow build.yml --event push $filterName $filterValue `
                    --json 'databaseId,status,conclusion,url' --limit 1 | ConvertFrom-Json
        if ($runs) { break }
        if ((Get-Date) -gt $deadline) { Fail "No CI build of $what appeared on GitHub after 3 minutes." }
        Start-Sleep -Seconds 5
    }
    $run = $runs[0]
    if ($run.status -ne 'completed') {
        Info "Waiting for the CI build of $what to finish: $($run.url)"
        & gh run watch $run.databaseId --repo $repo --interval 10 --exit-status *> $null
        $run = Invoke-Gh run view $run.databaseId --repo $repo --json 'databaseId,status,conclusion,url' | ConvertFrom-Json
    }
    return $run
}

# ---- 1. Checks -------------------------------------------------------------------------------------------

if ($Version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$') { Fail "'$Version' isn't a version like 0.3.0 (or 0.3.0-beta.1)." }
$Version = $Version.TrimStart('v')
$tag = "v$Version"

foreach ($tool in 'git', 'gh') {
    if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) { Fail "$tool isn't installed or isn't on PATH." }
}

Step "Checking the repository"
$repo = (Invoke-Gh repo view (Invoke-Git remote get-url origin) --json nameWithOwner --jq .nameWithOwner)
$default = (Invoke-Git symbolic-ref --short refs/remotes/origin/HEAD) -replace '^origin/', ''
$branch = & git -C $root symbolic-ref --short -q HEAD
if ($branch -ne $default) { Fail "You're on '$branch'. Switch to '$default' (after merging your PR): git switch $default" }
if (Invoke-Git status --porcelain) { Fail "There are uncommitted changes. Commit them through a PR first, or stash them." }

Invoke-Git fetch -q origin --tags
& git -C $root merge --ff-only -q "origin/$default"
if ($LASTEXITCODE -ne 0) { Fail "Couldn't fast-forward $default to origin/$default. Sort out the local commits first." }
$sha = (Invoke-Git rev-parse HEAD).Trim()
if ($sha -ne (Invoke-Git rev-parse "origin/$default").Trim()) { Fail "$default has commits that aren't on origin. Publish them through a PR first." }
Info "$repo, $default at $($sha.Substring(0, 7))"

& git -C $root rev-parse -q --verify "refs/tags/$tag" *> $null
if ($LASTEXITCODE -eq 0) { Fail "Tag $tag already exists." }

$latest = & git -C $root describe --tags --abbrev=0 --match 'v[0-9]*' 2>$null
if ($latest) {
    $latestCore = [version](($latest.TrimStart('v') -split '-')[0])
    $newCore = [version](($Version -split '-')[0])
    if ($newCore -le $latestCore -and -not ($newCore -eq $latestCore -and $latest.Contains('-') -and -not $Version.Contains('-'))) {
        Fail "$tag isn't newer than the latest release, $latest."
    }
    Info "Latest release: $latest -> $tag"
}

# ---- 2. CI of main -----------------------------------------------------------------------------------------

Step "Checking the CI build of $($sha.Substring(0, 7))"
$mainRun = Wait-BuildRun '--commit' $sha "$default ($($sha.Substring(0, 7)))"
if ($mainRun.conclusion -ne 'success') { Fail "The CI build of $default failed ($($mainRun.conclusion)): $($mainRun.url)" }
Info "Passed: $($mainRun.url)"

if ($DryRun) {
    Step "Dry run: all checks passed"
    Info "Would tag $($sha.Substring(0, 7)) as $tag, push it, wait for the release build, and $(if ($NoInstall) { 'not install' } else { "install into $InstallDir" })."
    exit 0
}

# ---- 3. Tag ----------------------------------------------------------------------------------------------

Step "Tagging $tag"
Invoke-Git tag -a $tag -m "UE Test Tool $tag"
& git -C $root push -q origin $tag
if ($LASTEXITCODE -ne 0) {
    & git -C $root tag -d $tag *> $null
    Fail "Couldn't push $tag. The local tag was removed; nothing was released."
}

# ---- 4. Release build -------------------------------------------------------------------------------------

Step "Waiting for the release build"
$releaseRun = Wait-BuildRun '--branch' $tag $tag
if ($releaseRun.conclusion -ne 'success') {
    Fail ("The release build failed ($($releaseRun.conclusion)): $($releaseRun.url)`n" +
          "       The tag $tag is pushed but no release was made. After fixing the problem, delete the tag " +
          "(git push origin :refs/tags/$tag; git tag -d $tag) and run this again.")
}
$release = Invoke-Gh release view $tag --repo $repo --json 'url,assets' | ConvertFrom-Json
if (-not ($release.assets | Where-Object name -eq 'UETestTool.exe')) { Fail "Release $tag has no UETestTool.exe: $($release.url)" }
Info "Released: $($release.url)"

# ---- 5. Install ------------------------------------------------------------------------------------------

if ($NoInstall) { exit 0 }

Step "Installing into $InstallDir"
$target = Join-Path $InstallDir 'UETestTool.exe'
New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null

$deadline = (Get-Date).AddMinutes(5)
$asked = $false
while (Get-Process UETestTool -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $target }) {
    if (-not $asked) { Write-Host "    Close UE Test Tool to finish installing (waiting up to 5 minutes)..." -ForegroundColor Yellow; $asked = $true }
    if ((Get-Date) -gt $deadline) { Fail "UE Test Tool is still running. Close it and run: gh release download $tag --repo $repo --pattern UETestTool.exe --dir `"$InstallDir`" --clobber" }
    Start-Sleep -Seconds 2
}

$download = Join-Path ([IO.Path]::GetTempPath()) "UETestTool-$tag"
New-Item -ItemType Directory -Force -Path $download | Out-Null
Invoke-Gh release download $tag --repo $repo --pattern UETestTool.exe --dir $download --clobber | Out-Null
Move-Item -Force (Join-Path $download 'UETestTool.exe') $target
Remove-Item -Recurse -Force $download -ErrorAction SilentlyContinue

$shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'UE Test Tool.lnk'
if (-not (Test-Path $shortcut)) {
    $shell = New-Object -ComObject WScript.Shell
    $link = $shell.CreateShortcut($shortcut)
    $link.TargetPath = $target
    $link.WorkingDirectory = $InstallDir
    $link.IconLocation = "$target,0"
    $link.Description = 'Launch Unreal Engine projects for testing'
    $link.Save()
    Info "Added a Start menu shortcut."
}

Info "Installed UE Test Tool $((Get-Item $target).VersionInfo.ProductVersion.Split('+')[0])"
Step "Done: $tag released and installed"
