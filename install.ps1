<#
.SYNOPSIS
    Installs the okf CLI on Windows.

.DESCRIPTION
    The Windows half of okf-net's installer pair (work item #36). install.sh covers Linux
    and macOS; this covers Windows, reading the same release manifest and making the same
    promises: HTTPS only, the sha256 from latest.json checked before anything is written
    outside a temporary directory, and an install that lands by rename so okf.exe is either
    the old binary or the new one and never a half-written file.

    Downloads okf-win-x64.exe, verifies it, and installs it as okf.exe into
    $env:LOCALAPPDATA\okf\bin - a user-writable directory, so nothing here needs
    Administrator. Re-running is safe: it reinstalls over itself rather than accumulating.

.PARAMETER Version
    Install this release instead of the newest, e.g. 1.0.0-rc.15 (a leading "v" is fine).

.PARAMETER InstallDir
    Install into this directory instead of $env:LOCALAPPDATA\okf\bin.

.PARAMETER DryRun
    Report what would be downloaded and installed. Write nothing.

.EXAMPLE
    irm https://get.okf.tychostation.dev/install.ps1 | iex

.EXAMPLE
    & ([scriptblock]::Create((irm https://get.okf.tychostation.dev/install.ps1))) -DryRun

    The one-liner idiom cannot pass arguments - "iex" gets a string, not a command - so
    arguments go through a script block. This is the same shape rustup and pnpm use.

.NOTES
    Requires Windows PowerShell 5.1 or PowerShell 7+.

    OKF_INSTALL_URL   base URL to install from  (default https://get.okf.tychostation.dev)

    This file is deliberately pure ASCII. Windows PowerShell 5.1 decodes a BOM-less file
    using the system ANSI code page, so a single typographic dash in a comment renders as
    mojibake on a machine whose code page is not 1252 - and a script fetched with
    Invoke-RestMethod has no BOM to offer. Staying inside ASCII removes the question.

    There is no Windows CI runner in this project, so this script has never been executed
    on Windows by the pipeline. It is parsed and rule-checked by PSScriptAnalyzer
    ("mise run lint-ps1"); its first real run is a tester's. See docs/decisions.md.
#>

# An installer's progress belongs on the console, for a human, in the order it happened.
# It is not pipeline data: nothing consumes this script's output, and Write-Output would
# put "==> downloading" into a variable if anyone ever captured the call. Write-Host is
# the correct cmdlet for that job and PSAvoidUsingWriteHost is wrong about this one file.
[Diagnostics.CodeAnalysis.SuppressMessageAttribute(
    'PSAvoidUsingWriteHost', '',
    Justification = 'Installer progress is console output for a human, not pipeline data.')]
[CmdletBinding()]
param(
    [string] $Version,
    [string] $InstallDir,
    [switch] $DryRun
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Invoke-WebRequest renders a progress bar by redrawing the console on every chunk, which
# on Windows PowerShell 5.1 costs more wall-clock time than the download it is reporting
# on. A 15 MB binary is the difference between seconds and minutes.
$ProgressPreference = 'SilentlyContinue'

# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------

function Write-Note {
    param([string] $Message = '')
    Write-Host $Message
}

function Write-Failure {
    param([Parameter(Mandatory)][string] $Message)
    Write-Error "install.ps1: $Message"
    exit 1
}

# ---------------------------------------------------------------------------
# Platform
#
# Refuse early and by name, the way install.sh does. This script writes to
# LOCALAPPDATA, edits the per-user Path in the registry and installs an okf.exe; none of
# those mean anything anywhere else, and PowerShell runs on Linux and macOS.
#
# $IsWindows exists from PowerShell 6 onward and does not exist in 5.1, where Set-StrictMode
# would turn reading it into a terminating error. $PSVersionTable has no Platform key on
# 5.1 either, so the absence of the key IS the answer: 5.1 only ever ran on Windows.
# ---------------------------------------------------------------------------

$onWindows = (-not $PSVersionTable.ContainsKey('Platform')) -or ($PSVersionTable.Platform -eq 'Win32NT')
if (-not $onWindows) {
    Write-Failure @'
this installer is for Windows. On Linux and macOS, use install.sh:
    curl -fsSL https://get.okf.tychostation.dev/install.sh | sh
'@
}

if ($PSVersionTable.PSVersion.Major -lt 5) {
    Write-Failure "needs Windows PowerShell 5.1 or newer; this is $($PSVersionTable.PSVersion)."
}

# Windows PowerShell 5.1 negotiates whatever .NET Framework's default is, which on an
# un-patched box is still SSL 3.0 / TLS 1.0 - and every host worth downloading from
# refused those years ago. The failure it produces ("The underlying connection was closed")
# names nothing useful, so the protocol is widened here rather than diagnosed later.
# Additive, so a system that already prefers TLS 1.3 keeps it.
if ($PSVersionTable.PSVersion.Major -lt 6) {
    [Net.ServicePointManager]::SecurityProtocol =
        [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
}

# ---------------------------------------------------------------------------
# Configuration
# ---------------------------------------------------------------------------

$baseUrl = if ($env:OKF_INSTALL_URL) { $env:OKF_INSTALL_URL } else { 'https://get.okf.tychostation.dev' }
$baseUrl = $baseUrl.TrimEnd('/')

# HTTPS, with one exception that is not a loophole: a loopback address, which is what an
# acceptance harness serves a fixture over. Everything else must be https, because the
# manifest is what the sha256 comparison is made against - fetch it in cleartext and an
# attacker writes both numbers and the check proves nothing at all.
#
# Known gap, stated rather than papered over: PowerShell has no equivalent of curl's
# --proto-redir, so a hostile https -> http REDIRECT is not blocked here the way install.sh
# blocks it. The artifact host issues no redirects today. Closing this properly means
# following redirects by hand, and it belongs with the rest of the public-host security
# story in ringo/okf-net#26, which owns manifest signing too.
$uri = $null
if (-not [Uri]::TryCreate($baseUrl, [UriKind]::Absolute, [ref] $uri)) {
    Write-Failure "OKF_INSTALL_URL is not a URL: $baseUrl"
}
$isLoopback = $uri.IsLoopback
if ($uri.Scheme -ne 'https' -and -not $isLoopback) {
    Write-Failure "refusing to install over $($uri.Scheme): use an https URL (got $baseUrl)."
}

if (-not $InstallDir) {
    if (-not $env:LOCALAPPDATA) {
        Write-Failure 'LOCALAPPDATA is not set; pass -InstallDir explicitly.'
    }
    $InstallDir = Join-Path $env:LOCALAPPDATA 'okf\bin'
}

$asset = 'okf-win-x64.exe'
$destination = Join-Path $InstallDir 'okf.exe'

$requested = ''
if ($Version) {
    $requested = $Version.TrimStart('v', 'V')
}

# ---------------------------------------------------------------------------
# Manifest
#
# Invoke-RestMethod parses the JSON for us, which is the one place this script gets to be
# simpler than install.sh: PowerShell has a JSON reader and a machine with PowerShell on it
# has that reader too, so there is no sed-based reader here and no reason for one.
#
# Property lookups go through PSObject.Properties rather than dotting straight in. Under
# Set-StrictMode a missing property is a terminating error whose message is about
# PowerShell, and "the manifest lists no okf-win-x64.exe" is what the reader needs to know.
# ---------------------------------------------------------------------------

function Get-JsonProperty {
    param(
        [Parameter(Mandatory)] $Object,
        [Parameter(Mandatory)][string] $Name
    )

    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    return $property.Value
}

$manifestUrl = if ($requested) { "$baseUrl/v$requested/latest.json" } else { "$baseUrl/latest.json" }

if ($requested) {
    Write-Note "==> okf $requested"
} else {
    Write-Note '==> okf (newest release)'
}
Write-Note "    manifest: $manifestUrl"

try {
    $manifest = Invoke-RestMethod -Uri $manifestUrl -UseBasicParsing
} catch {
    Write-Failure @"
could not fetch $manifestUrl
    $($_.Exception.Message)
    If this cannot resolve, note that get.okf.tychostation.dev resolves only inside
    Ringo's network today - see ringo/okf-net#26.
"@
}

$resolved = Get-JsonProperty -Object $manifest -Name 'version'
if (-not $resolved) {
    Write-Failure "no version in $manifestUrl - is it a release manifest?"
}
if ($requested -and $resolved -ne $requested) {
    Write-Failure "asked for $requested but $manifestUrl describes $resolved"
}

$assets = Get-JsonProperty -Object $manifest -Name 'assets'
if (-not $assets) {
    Write-Failure "the manifest for $resolved lists no assets"
}

$entry = Get-JsonProperty -Object $assets -Name $asset
if (-not $entry) {
    Write-Failure "the manifest for $resolved lists no $asset"
}

$assetPath = Get-JsonProperty -Object $entry -Name 'path'
$assetSha = Get-JsonProperty -Object $entry -Name 'sha256'
if (-not $assetPath) { Write-Failure "the manifest for $resolved lists no $asset path" }
if (-not $assetSha) { Write-Failure "the manifest for $resolved lists no $asset sha256" }

$assetUrl = "$baseUrl/$assetPath"

Write-Note "    version:  $resolved"
Write-Note "    binary:   $assetUrl"
Write-Note "    sha256:   $assetSha"
Write-Note "    install:  $destination"

if ($DryRun) {
    Write-Note '==> -DryRun: nothing was downloaded or written'
    return
}

# ---------------------------------------------------------------------------
# Download, verify, install
# ---------------------------------------------------------------------------

$staging = Join-Path ([IO.Path]::GetTempPath()) ("okf-install-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $staging -Force | Out-Null

try {
    $downloaded = Join-Path $staging 'okf.exe'

    Write-Note "==> downloading $asset"
    try {
        Invoke-WebRequest -Uri $assetUrl -OutFile $downloaded -UseBasicParsing
    } catch {
        Write-Failure "could not download $assetUrl`n    $($_.Exception.Message)"
    }

    # Fail hard, and print both digests. A single "verification failed" cannot tell a
    # truncated download apart from the wrong file, and those are opposite repairs.
    # Nothing has been written outside the temporary directory yet, so a mismatch leaves
    # no partial install behind.
    $actual = (Get-FileHash -LiteralPath $downloaded -Algorithm SHA256).Hash
    if (-not [string]::Equals($actual, $assetSha, [StringComparison]::OrdinalIgnoreCase)) {
        Write-Failure @"
sha256 mismatch for $asset
    manifest:   $assetSha
    downloaded: $($actual.ToLowerInvariant())
    Refusing to install. Nothing was written to $InstallDir.
"@
    }
    Write-Note '    sha256 verified'

    if (-not (Test-Path -LiteralPath $InstallDir)) {
        New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
    }

    # Install by rename, so okf.exe is either the old binary or the new one and never a
    # half-written file. The staging copy is made INSIDE the install directory: a rename
    # is only atomic within one volume, and the temp directory is very often on another.
    #
    # Move-Item -Force onto a running executable fails on Windows, where a loaded image is
    # locked - unlike POSIX, where the rename succeeds and the old inode lives on. That is
    # worth saying out loud rather than reporting as "access denied".
    $localStaging = Join-Path $InstallDir (".okf.install." + [Guid]::NewGuid().ToString('N'))
    Copy-Item -LiteralPath $downloaded -Destination $localStaging -Force
    try {
        Move-Item -LiteralPath $localStaging -Destination $destination -Force
    } catch {
        Remove-Item -LiteralPath $localStaging -Force -ErrorAction SilentlyContinue
        Write-Failure @"
could not install to $destination
    $($_.Exception.Message)
    If okf is running, close it and run this again: Windows locks a loaded executable.
"@
    }
} finally {
    Remove-Item -LiteralPath $staging -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Note '==> installed'

# Two ways this can go wrong and only one of them is an exception. A file that will not
# execute at all throws; a binary that runs and fails sets $LASTEXITCODE and throws
# nothing, because $ErrorActionPreference has no bearing on a native command's exit code.
# Both are worth a warning and neither is worth failing an install that has already
# landed correct, verified bytes.
#
# No `2>&1` on the call: on Windows PowerShell 5.1 that turns a native command's stderr
# into ErrorRecords, which under $ErrorActionPreference = 'Stop' can terminate the script
# over a binary that merely wrote a diagnostic line.
$reported = $null
try {
    $reported = & $destination version
} catch {
    $reported = $null
}

if ($LASTEXITCODE -eq 0 -and $reported) {
    Write-Note "    okf.exe $reported"
} else {
    Write-Warning "install.ps1: $destination was installed but did not answer ``okf version``"
}

# ---------------------------------------------------------------------------
# PATH
#
# The per-user Path, never the machine one: this installs into LOCALAPPDATA and needs no
# Administrator, and an installer that quietly asks for one has changed what it is.
#
# Written through the registry rather than through
# [Environment]::SetEnvironmentVariable(..., 'User'), which is the obvious call and is
# lossy: it always writes REG_SZ, so a user whose Path is REG_EXPAND_SZ - the default, and
# the reason entries like %JAVA_HOME%\bin work at all - has every such entry frozen to
# whatever it expanded to at that moment. Reading with DoNotExpandEnvironmentNames and
# writing back with the kind it already had preserves both.
# ---------------------------------------------------------------------------

function Test-PathEntry {
    param(
        [Parameter(Mandatory)][AllowEmptyString()][string] $PathValue,
        [Parameter(Mandatory)][string] $Directory
    )

    $wanted = $Directory.TrimEnd('\', '/')
    foreach ($candidate in $PathValue -split ';') {
        if ($candidate.Trim().TrimEnd('\', '/') -eq $wanted) { return $true }
    }
    return $false
}

$key = $null
try {
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Environment', $true)
    if ($null -eq $key) {
        $key = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey('Environment')
    }

    $current = [string] $key.GetValue(
        'Path', '', [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)

    if (Test-PathEntry -PathValue $current -Directory $InstallDir) {
        Write-Note "    $InstallDir is already on your PATH"
    } else {
        $kind = [Microsoft.Win32.RegistryValueKind]::ExpandString
        if ($current) { $kind = $key.GetValueKind('Path') }

        $updated = if ($current) { $current.TrimEnd(';') + ';' + $InstallDir } else { $InstallDir }
        $key.SetValue('Path', $updated, $kind)

        # The current session's copy was inherited at start-up and is not refreshed by the
        # write above, so `okf` would still not be found in THIS window without it.
        $env:Path = $env:Path.TrimEnd(';') + ';' + $InstallDir

        Write-Note ''
        Write-Note "    Added $InstallDir to your user PATH."
        Write-Note '    Already-open terminals will not see it; new ones will.'
    }
} catch {
    Write-Warning @"
install.ps1: could not update your PATH ($($_.Exception.Message))
    okf.exe is installed at $destination - add that directory to PATH by hand, or run it
    by its full path.
"@
} finally {
    if ($null -ne $key) { $key.Dispose() }
}
