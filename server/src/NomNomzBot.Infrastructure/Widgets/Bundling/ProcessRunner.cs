// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Configuration;

namespace NomNomzBot.Infrastructure.Widgets.Bundling;

/// <summary>
/// Runs an out-of-process CLI: pipes <see cref="ProcessRunRequest.StandardInput"/> to stdin, reads stdout and
/// stderr concurrently (so a full pipe never deadlocks), and reports whether the executable even launched.
/// </summary>
public sealed class ProcessRunner : IProcessRunner
{
    /// <summary>How long a run may take when the request sets no timeout.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(60);

    /// <summary>The esbuild timeout: <c>Widgets:EsbuildTimeoutSeconds</c>, or <see cref="DefaultTimeout"/>.</summary>
    public static TimeSpan EsbuildTimeout(IConfiguration configuration) =>
        configuration.GetValue<int?>("Widgets:EsbuildTimeoutSeconds") is > 0 and int seconds
            ? TimeSpan.FromSeconds(seconds)
            : DefaultTimeout;

    public async Task<ProcessRunResult> RunAsync(
        ProcessRunRequest request,
        CancellationToken cancellationToken = default
    )
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = request.FileName,
            RedirectStandardInput = request.StandardInput is not null,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        if (request.WorkingDirectory is not null)
            startInfo.WorkingDirectory = request.WorkingDirectory;
        foreach (string argument in request.Arguments)
            startInfo.ArgumentList.Add(argument);

        using Process process = new() { StartInfo = startInfo };

        try
        {
            process.Start();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            // The executable could not be launched (not found / not executable) — a distinct outcome from a
            // process that ran and failed, so the caller can surface an "install the tool" message.
            return new(false, -1, string.Empty, ex.Message);
        }

        TimeSpan timeout = request.Timeout ?? DefaultTimeout;
        using CancellationTokenSource timeoutSource =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        // Every wait below shares one token, so a cancel or a timeout during the stdin write or the output
        // reads kills the process too, not only one during the wait for exit.
        try
        {
            if (request.StandardInput is not null)
            {
                await process.StandardInput.WriteAsync(
                    request.StandardInput.AsMemory(),
                    timeoutSource.Token
                );
                process.StandardInput.Close();
            }

            Task<string> stdout = process.StandardOutput.ReadToEndAsync(timeoutSource.Token);
            Task<string> stderr = process.StandardError.ReadToEndAsync(timeoutSource.Token);

            await process.WaitForExitAsync(timeoutSource.Token);

            return new(true, process.ExitCode, await stdout, await stderr);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            if (cancellationToken.IsCancellationRequested)
                throw;

            return new(
                true,
                -1,
                string.Empty,
                $"The process timed out after {timeout.TotalSeconds:0.##} seconds and was killed.",
                TimedOut: true
            );
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            // Best-effort — the process may have exited between the check and the kill.
        }
    }
}
