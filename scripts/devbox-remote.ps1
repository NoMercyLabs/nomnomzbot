# -----------------------------------------------------------------------------
#  Copyright (c) NoMercy Labs.
#
#  This file is part of NomNomzBot. You may use, change and share it for free,
#  for any noncommercial purpose, under the PolyForm Noncommercial License
#  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
#  See LICENSE for details, and TRADEMARKS.md for the name and logo.
#
#  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
# -----------------------------------------------------------------------------
#
# Mirrors this working tree into the Proxmox devbox LXC (devbox/proxmox/provision-lxc.sh) and runs a
# command there, inside the devbox container. The mirror is exact: the box checks out the local HEAD
# (local commits travel as a git bundle), then gets every changed and untracked file, and loses every
# file deleted here. Build outputs (bin/obj/node_modules) stay in the box between runs.
#
#   scripts/devbox-remote.ps1 -Command "./scripts/slice-check.ps1 -Paths @('server/src/...') -Inspect"
# -Command is PowerShell, run from the repo root in the box.
#   scripts/devbox-remote.ps1 -SyncOnly
param(
    [string]$Command,
    [switch]$SyncOnly,
    [string]$HostAlias = 'nnz-devbox',
    [string]$RepoDir = '/srv/nomnomzbot',
    [string]$Container = 'nomnomzbot-devbox'
)
$ErrorActionPreference = 'Stop'
$root = (git rev-parse --show-toplevel).Trim()
Set-Location $root
$sha = (git rev-parse HEAD).Trim()
$tmp = Join-Path ([IO.Path]::GetTempPath()) "devbox-remote-$PID"
New-Item -ItemType Directory -Force $tmp | Out-Null

function Invoke-Box([string]$cmd) {
    ssh -o BatchMode=yes $HostAlias $cmd
    if ($LASTEXITCODE -ne 0) { throw "box step failed ($LASTEXITCODE): $cmd" }
}

try {
    # 1. Commits: the box fetches origin, then any local commits from a bundle.
    Invoke-Box "git -C $RepoDir fetch -q origin"
    $local = (git rev-list --count "HEAD" "^origin/master").Trim()
    if ([int]$local -gt 0) {
        $bundle = Join-Path $tmp 'local.bundle'
        git bundle create -q $bundle HEAD '^origin/master'
        scp -q -o BatchMode=yes $bundle "${HostAlias}:/tmp/local.bundle"
        if ($LASTEXITCODE -ne 0) { throw "scp of the bundle failed ($LASTEXITCODE)" }
        Invoke-Box "git -C $RepoDir fetch -q /tmp/local.bundle HEAD && rm -f /tmp/local.bundle"
    }
    Invoke-Box "git -C $RepoDir reset -q --hard $sha && git -C $RepoDir clean -qfd"

    # 2. Uncommitted work: changed + untracked files go over as one tar, deleted files are removed.
    $files = git ls-files -m -o --exclude-standard | Where-Object { $_ -notmatch '__pycache__' -and (Test-Path $_ -PathType Leaf) }
    if ($files) {
        $list = Join-Path $tmp 'files.txt'
        $tar = Join-Path $tmp 'dirty.tar'
        [IO.File]::WriteAllLines($list, [string[]]$files)
        # Windows' own tar: Git's GNU tar reads "C:" in a path as a remote host name.
        & "$env:SystemRoot\System32\tar.exe" -cf $tar -T $list
        if ($LASTEXITCODE -ne 0) { throw "tar failed ($LASTEXITCODE)" }
        scp -q -o BatchMode=yes $tar "${HostAlias}:/tmp/dirty.tar"
        if ($LASTEXITCODE -ne 0) { throw "scp of the changed files failed ($LASTEXITCODE)" }
        Invoke-Box "tar -xf /tmp/dirty.tar -C $RepoDir && rm -f /tmp/dirty.tar"
    }
    $deleted = git ls-files -d
    if ($deleted) { Invoke-Box ("cd $RepoDir && rm -f -- " + (($deleted | ForEach-Object { "'$_'" }) -join ' ')) }
    Write-Output "SYNCED $sha (+$local local commits, $(@($files).Count) changed, $(@($deleted).Count) deleted)"

    if ($SyncOnly -or -not $Command) { exit 0 }

    # 3. Run the PowerShell command inside the devbox container (the repo is mounted at /workspace).
    # Encoded, so no shell on the way (ssh, bash, docker) can split or re-quote it.
    $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($Command))
    ssh -o BatchMode=yes $HostAlias "docker exec -u root -w /workspace $Container pwsh -NoProfile -EncodedCommand $encoded"
    exit $LASTEXITCODE
}
finally {
    Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue
}
