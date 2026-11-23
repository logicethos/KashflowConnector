# Windows launcher for KashflowMCP. Downloads the self-contained binary from
# GitHub Releases, verifies SHA256SUMS, and runs it. stdout is reserved for
# MCP JSON-RPC. The password is never read or logged.
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

function Log([string]$Message) {
    [Console]::Error.WriteLine($Message)
}

function Die([string]$Message) {
    Log "kashflow-mcp: $Message"
    exit 1
}

$Version = if ($env:KASHFLOW_MCP_VERSION) { $env:KASHFLOW_MCP_VERSION } else { '1.0.0' }
$Repo = if ($env:KASHFLOW_MCP_REPO) { $env:KASHFLOW_MCP_REPO } else { 'logicethos/KashflowConnector' }

function Get-Rid {
    if ($env:KASHFLOW_MCP_RID) { return $env:KASHFLOW_MCP_RID }
    $arch = $env:PROCESSOR_ARCHITECTURE
    if ($env:PROCESSOR_ARCHITEW6432) { $arch = $env:PROCESSOR_ARCHITEW6432 }
    $cpu = if ($arch -eq 'ARM64') { 'arm64' } else { 'x64' }
    return "win-$cpu"
}

function Get-Sha256([string]$Path) {
    return (Get-FileHash -Algorithm SHA256 -Path $Path).Hash.ToLowerInvariant()
}

function Download-File([string]$Url, [string]$OutFile) {
    Invoke-WebRequest -Uri $Url -OutFile $OutFile -UseBasicParsing | Out-Null
}

if ($env:KASHFLOW_MCP_BIN) {
    if (-not (Test-Path -LiteralPath $env:KASHFLOW_MCP_BIN)) {
        Die "KASHFLOW_MCP_BIN does not exist: $($env:KASHFLOW_MCP_BIN)"
    }
    & $env:KASHFLOW_MCP_BIN @args
    exit $LASTEXITCODE
}

$rid = Get-Rid
$binName = 'KashflowMCP.exe'
if ($env:KASHFLOW_MCP_CACHE) {
    $cacheRoot = $env:KASHFLOW_MCP_CACHE
} elseif ($env:LOCALAPPDATA) {
    $cacheRoot = Join-Path $env:LOCALAPPDATA 'kashflow-mcp'
} else {
    $cacheRoot = Join-Path $env:USERPROFILE '.cache\kashflow-mcp'
}

$base = if ($env:KASHFLOW_MCP_RELEASE_BASE) { $env:KASHFLOW_MCP_RELEASE_BASE.TrimEnd('/') } else { "https://github.com/$Repo/releases/download/v$Version" }
$dest = Join-Path (Join-Path $cacheRoot $Version) $rid
New-Item -ItemType Directory -Force -Path $cacheRoot | Out-Null

$mutex = New-Object System.Threading.Mutex($false, 'Local\KashflowMcpLauncher')
if (-not $mutex.WaitOne(30000)) { Die 'timed out waiting for another download to finish' }
try {
    $stampPath = Join-Path $dest '.sha256'
    $stamp = ''
    if (Test-Path -LiteralPath $stampPath) {
        $stamp = ((Get-Content -LiteralPath $stampPath -Raw).Trim().TrimStart([char]0xFEFF)).ToLowerInvariant()
    }
    $binPath = Join-Path $dest $binName

    $expected = ''
    $sumsFile = Join-Path ([System.IO.Path]::GetTempPath()) ("kashflow-sha256-" + [guid]::NewGuid().ToString() + ".txt")
    try {
        Download-File "$base/SHA256SUMS" $sumsFile
        $pattern = '^\s*([A-Fa-f0-9]{64})\s+\*?KashflowMCP-' + [regex]::Escape($rid) + '\.zip\s*$'
        foreach ($line in Get-Content -LiteralPath $sumsFile) {
            if ($line -match $pattern) {
                $expected = $Matches[1].ToLowerInvariant()
                break
            }
        }
    } catch {
        Log "kashflow-mcp: could not download $base/SHA256SUMS"
    } finally {
        if (Test-Path -LiteralPath $sumsFile) { Remove-Item -LiteralPath $sumsFile -Force }
    }

    $ready = $false
    if ($expected -and (Test-Path -LiteralPath $binPath) -and $stamp -eq $expected) {
        $ready = $true
    } elseif (-not $expected -and (Test-Path -LiteralPath $binPath) -and $stamp) {
        Log "kashflow-mcp: checksum server unavailable; using cached $rid binary"
        $ready = $true
    } else {
        if (-not $expected) {
            Die "no checksum for KashflowMCP-$rid.zip in the v$Version release. Publish that GitHub release (scripts/publish.sh) before installing the plugin."
        }
        $tmp = Join-Path ([System.IO.Path]::GetTempPath()) ("kashflow-mcp-" + [guid]::NewGuid().ToString())
        New-Item -ItemType Directory -Force -Path $tmp | Out-Null
        try {
            $zip = Join-Path $tmp "KashflowMCP-$rid.zip"
            try {
                Download-File "$base/KashflowMCP-$rid.zip" $zip
            } catch {
                Die "could not download $base/KashflowMCP-$rid.zip. Create the v$Version GitHub release first."
            }
            $actual = Get-Sha256 $zip
            if ($actual -ne $expected) {
                Die "checksum mismatch for KashflowMCP-$rid.zip (expected $expected, got $actual)"
            }
            $extract = Join-Path $tmp 'extract'
            Expand-Archive -LiteralPath $zip -DestinationPath $extract -Force
            $found = Get-ChildItem -LiteralPath $extract -Recurse -Filter $binName -File | Select-Object -First 1
            if (-not $found) { Die "archive KashflowMCP-$rid.zip did not contain $binName" }
            if (Test-Path -LiteralPath $dest) { Remove-Item -LiteralPath $dest -Recurse -Force }
            New-Item -ItemType Directory -Force -Path $dest | Out-Null
            Copy-Item -Path (Join-Path $found.DirectoryName '*') -Destination $dest -Recurse -Force
            [System.IO.File]::WriteAllText((Join-Path $dest '.sha256'), $expected)
            Log "kashflow-mcp: installed $rid v$Version"
            $ready = $true
        } finally {
            if (Test-Path -LiteralPath $tmp) { Remove-Item -LiteralPath $tmp -Recurse -Force }
        }
    }
} finally {
    $mutex.ReleaseMutex() | Out-Null
    $mutex.Dispose()
}

if (-not (Test-Path -LiteralPath $binPath)) { Die "missing $binPath" }
& $binPath @args
exit $LASTEXITCODE
