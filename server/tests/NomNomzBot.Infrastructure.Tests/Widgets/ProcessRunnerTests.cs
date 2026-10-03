// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Diagnostics;
using NomNomzBot.Infrastructure.Widgets.Bundling;

namespace NomNomzBot.Infrastructure.Tests.Widgets;

public sealed class ProcessRunnerTests
{
    [Fact]
    public async Task A_child_that_outlives_the_timeout_is_killed_and_the_result_names_the_timeout()
    {
        string pidFile = NewPidFile();
        try
        {
            ProcessRunRequest request = SleepingChild(pidFile) with
            {
                Timeout = TimeSpan.FromSeconds(5),
            };

            Stopwatch clock = Stopwatch.StartNew();
            ProcessRunResult result = await new ProcessRunner().RunAsync(request);
            clock.Stop();

            Assert.True(result.Started);
            Assert.NotEqual(0, result.ExitCode);
            Assert.True(result.TimedOut);
            Assert.Contains("timed out after 5 seconds", result.StandardError);
            Assert.True(
                clock.Elapsed < TimeSpan.FromSeconds(30),
                $"The call took {clock.Elapsed}, far past the 5 second timeout."
            );
            await AssertChildGoneAsync(pidFile);
        }
        finally
        {
            File.Delete(pidFile);
        }
    }

    [Fact]
    public async Task A_cancel_while_the_child_runs_kills_the_child_and_rethrows()
    {
        string pidFile = NewPidFile();
        try
        {
            using CancellationTokenSource cancel = new(TimeSpan.FromSeconds(5));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                new ProcessRunner().RunAsync(SleepingChild(pidFile), cancel.Token)
            );

            await AssertChildGoneAsync(pidFile);
        }
        finally
        {
            File.Delete(pidFile);
        }
    }

    private static string NewPidFile() =>
        Path.Combine(Path.GetTempPath(), $"processrunner-{Guid.NewGuid():N}.pid");

    // A child that records its own pid, then sleeps for a minute. Uses the shell every CI OS ships.
    private static ProcessRunRequest SleepingChild(string pidFile) =>
        OperatingSystem.IsWindows()
            ? new(
                "powershell",
                [
                    "-NoProfile",
                    "-Command",
                    $"Set-Content -LiteralPath '{pidFile}' -Value $PID; Start-Sleep -Seconds 60",
                ],
                StandardInput: null
            )
            : new("sh", ["-c", $"echo $$ > '{pidFile}'; exec sleep 60"], StandardInput: null);

    private static async Task AssertChildGoneAsync(string pidFile)
    {
        Assert.True(File.Exists(pidFile), "The child never recorded its pid.");
        int pid = int.Parse((await File.ReadAllTextAsync(pidFile)).Trim());

        for (int attempt = 0; attempt < 40; attempt++)
        {
            if (!IsRunning(pid))
                return;
            await Task.Delay(50);
        }

        Assert.Fail($"Child process {pid} is still running after the call ended.");
    }

    private static bool IsRunning(int pid)
    {
        try
        {
            using Process process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
