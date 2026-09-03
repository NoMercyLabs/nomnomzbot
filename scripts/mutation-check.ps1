# Mutation harness: break ONE guard, run the suite, record which tests notice, restore.
# `if (false)` does not work here — unreachable code is a warning and warnings are errors — so each
# mutation DELETES the guard outright, which is also the more realistic regression.

$ErrorActionPreference = 'Stop'
$repo = 'C:\Projects\NoMercyLabs\nomnomzbot'
$src = Join-Path $repo 'server\src\NomNomzBot.Infrastructure\Moderation\SpamEnforcementExecutor.cs'
$trx = Join-Path $repo '.scratch\mutate.trx'
$original = Get-Content $src -Raw

$mutations = @(
    @{ Name = 'delete the dry-run guard'
       From = '        if (decision.IsDryRun)
            return new SpamEnforcementOutcome(false, false, "dry run");

'
       To   = '' }

    @{ Name = 'delete the Flag/None guard'
       From = '        if (decision.Outcome is SpamOutcome.None or SpamOutcome.Flag)
            return new SpamEnforcementOutcome(false, false, "nothing to enforce");

'
       To   = '' }

    @{ Name = 'invert the platform guard (enforce on non-Twitch, skip Twitch)'
       From = '        if (!string.Equals(provider, AuthEnums.Platform.Twitch, StringComparison.OrdinalIgnoreCase))'
       To   = '        if (string.Equals(provider, AuthEnums.Platform.Twitch, StringComparison.OrdinalIgnoreCase))' }

    @{ Name = 'time out on DeleteAndQueue too (breaks the SD11 ceiling)'
       From = '        if (decision.Outcome != SpamOutcome.DeleteAndEscalate)
            return new SpamEnforcementOutcome(deleted, false, null);

'
       To   = '' }

    @{ Name = 'trust a stored 0 as the timeout length'
       From = '        return config.IsSuccess && config.Value.HeatTimeoutSeconds > 0'
       To   = '        return config.IsSuccess' }

    @{ Name = 'report a failed delete as success'
       From = '        return result.IsSuccess;
    }

    private async Task<bool> TimeoutAsync('
       To   = '        return true;
    }

    private async Task<bool> TimeoutAsync(' }

    @{ Name = 'issue the timeout as the SUBJECT instead of the broadcaster'
       From = '            broadcasterId.ToString(),
            ownerUserId,
            subjectPlatformUserId,'
       To   = '            broadcasterId.ToString(),
            Guid.NewGuid(),
            subjectPlatformUserId,' }

    @{ Name = 'send a generic reason instead of the decision explanation'
       From = '            decision.Reason,
            null,
            ct'
       To   = '            "Automated action.",
            null,
            ct' }
)

Push-Location (Join-Path $repo 'server')
try {
    foreach ($m in $mutations) {
        if (-not $original.Contains($m.From)) {
            Write-Output ("SKIP       {0} -- anchor not found" -f $m.Name)
            continue
        }

        Set-Content -Path $src -Value $original.Replace($m.From, $m.To) -NoNewline
        if (Test-Path $trx) { Remove-Item $trx -Force }

        & dotnet test tests/NomNomzBot.Infrastructure.Tests --nologo `
            --filter 'FullyQualifiedName~SpamEnforcementExecutor' `
            --logger "trx;LogFileName=$trx" 2>&1 | Out-Null

        if (Test-Path $trx) {
            [xml]$r = Get-Content $trx
            $failed = @($r.TestRun.Results.UnitTestResult | Where-Object { $_.outcome -eq 'Failed' })
            if ($failed.Count -eq 0) {
                Write-Output ("SURVIVED   {0}  <-- NO TEST NOTICED" -f $m.Name)
            }
            else {
                $names = ($failed | ForEach-Object { (($_.testName -split '\(')[0] -split '\.')[-1] }) -join ', '
                Write-Output ("caught     {0} -> {1}" -f $m.Name, $names)
            }
        }
        else {
            Write-Output ("BUILD FAIL {0}" -f $m.Name)
        }

        Set-Content -Path $src -Value $original -NoNewline
    }
}
finally {
    Set-Content -Path $src -Value $original -NoNewline
    Pop-Location
}
Write-Output 'source restored'
