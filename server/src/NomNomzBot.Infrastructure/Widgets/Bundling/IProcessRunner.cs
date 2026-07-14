// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Infrastructure.Widgets.Bundling;

/// <summary>
/// A thin, testable seam over an out-of-process CLI invocation (source piped on stdin, output read from stdout).
/// It exists so the widget build service can be unit-tested against a fake without a real esbuild binary present.
/// </summary>
public interface IProcessRunner
{
    Task<ProcessRunResult> RunAsync(
        ProcessRunRequest request,
        CancellationToken cancellationToken = default
    );
}

/// <summary>What to run: the executable, its arguments, and the text to feed on stdin (null = no stdin).</summary>
public sealed record ProcessRunRequest(
    string FileName,
    IReadOnlyList<string> Arguments,
    string? StandardInput
);

/// <summary>
/// The result. <see cref="Started"/> is false when the executable could not be launched at all (not found / not
/// executable) — distinct from a process that ran and exited non-zero.
/// </summary>
public sealed record ProcessRunResult(
    bool Started,
    int ExitCode,
    string StandardOutput,
    string StandardError
);
