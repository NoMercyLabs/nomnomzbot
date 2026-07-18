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

namespace NomNomzBot.Infrastructure.Widgets.Bundling;

/// <summary>
/// Runs an out-of-process CLI: pipes <see cref="ProcessRunRequest.StandardInput"/> to stdin, reads stdout and
/// stderr concurrently (so a full pipe never deadlocks), and reports whether the executable even launched.
/// </summary>
public sealed class ProcessRunner : IProcessRunner
{
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
            return new ProcessRunResult(false, -1, string.Empty, ex.Message);
        }

        if (request.StandardInput is not null)
        {
            await process.StandardInput.WriteAsync(
                request.StandardInput.AsMemory(),
                cancellationToken
            );
            process.StandardInput.Close();
        }

        Task<string> stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> stderr = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }

        string outputText = await stdout;
        string errorText = await stderr;
        return new ProcessRunResult(true, process.ExitCode, outputText, errorText);
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
