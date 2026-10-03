// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.CustomCode;
using NomNomzBot.Infrastructure.Widgets.Bundling;

namespace NomNomzBot.Infrastructure.CustomCode;

/// <summary>
/// Bundles a script project with the same standalone esbuild binary the widget build uses
/// (<c>Widgets:EsbuildPath</c>, default <c>esbuild</c> on PATH). The files are written to a temp dir that is always
/// deleted. The output is one IIFE for Jint: types stripped, relative imports inlined, ASCII-only (no
/// <c>--charset=utf8</c>), so the stdout pipe's encoding can never corrupt a string literal.
/// </summary>
public sealed partial class EsbuildScriptBundler(
    IProcessRunner process,
    IConfiguration configuration,
    ILogger<EsbuildScriptBundler> logger
) : IScriptBundler
{
    private readonly string _esbuildPath = configuration["Widgets:EsbuildPath"] ?? "esbuild";
    private readonly TimeSpan _esbuildTimeout = ProcessRunner.EsbuildTimeout(configuration);

    // esbuild's plain-text log: `✘ [ERROR] <message>`, a blank line, then `    <file>:<line>:<column>:`.
    [GeneratedRegex(
        @"\[ERROR\] (?<message>[^\r\n]+)(?:\r?\n){2}[ \t]+(?<file>[^\r\n]+?):(?<line>\d+):(?<column>\d+):"
    )]
    private static partial Regex LoggedError();

    public async Task<Result<string>> BundleAsync(
        IReadOnlyDictionary<string, string> files,
        string entry,
        CancellationToken cancellationToken = default
    )
    {
        string workDir = Path.Combine(
            Path.GetTempPath(),
            "nnz-script-build-" + Guid.NewGuid().ToString("N")
        );
        try
        {
            Result written = WriteProject(workDir, files);
            if (written.IsFailure)
                return Result.Failure<string>(written.ErrorMessage!, written.ErrorCode);

            ProcessRunResult run = await process.RunAsync(
                new(
                    _esbuildPath,
                    Arguments(entry),
                    StandardInput: null,
                    WorkingDirectory: workDir,
                    Timeout: _esbuildTimeout
                ),
                cancellationToken
            );
            return ToResult(run);
        }
        finally
        {
            TryDeleteDir(workDir);
        }
    }

    private static List<string> Arguments(string entry) =>
        [
            "--bundle",
            "--format=iife",
            "--platform=neutral",
            "--target=es2022",
            // A tsconfig.json inside the project must not change how the sandbox code is emitted.
            "--tsconfig-raw={}",
            "--log-level=error",
            "--color=false",
            Normalize(entry),
        ];

    private Result<string> ToResult(ProcessRunResult run)
    {
        if (!run.Started)
        {
            logger.LogWarning(
                "Script build tool '{Path}' could not be started: {Error}",
                _esbuildPath,
                run.StandardError
            );
            return Result.Failure<string>(
                $"The esbuild binary '{_esbuildPath}' could not be started. Install esbuild (or set "
                    + "Widgets:EsbuildPath) to save scripts.",
                "SCRIPT_BUILD_TOOL_UNAVAILABLE"
            );
        }

        if (run.ExitCode == 0)
            return Result.Success(run.StandardOutput);

        return Result.Failure<string>(Describe(run.StandardError), "SCRIPT_BUILD_FAILED");
    }

    // One `file:line:column: message` line per error, the form editors and people both read.
    private static string Describe(string standardError)
    {
        List<string> errors = LoggedError()
            .Matches(standardError)
            .Select(m =>
                $"{m.Groups["file"].Value}:{m.Groups["line"].Value}:{m.Groups["column"].Value}: {m.Groups["message"].Value}"
            )
            .ToList();
        if (errors.Count > 0)
            return string.Join('\n', errors);
        return string.IsNullOrWhiteSpace(standardError)
            ? "The script build failed."
            : standardError.Trim();
    }

    // A project's paths are untrusted and become real files: every one must land inside the temp dir.
    private static Result WriteProject(string workDir, IReadOnlyDictionary<string, string> files)
    {
        string root = Path.GetFullPath(workDir) + Path.DirectorySeparatorChar;
        foreach (string path in files.Keys)
        {
            string full = Path.GetFullPath(Path.Combine(root, Normalize(path)));
            if (string.IsNullOrWhiteSpace(path) || !full.StartsWith(root, StringComparison.Ordinal))
                return Result.Failure(
                    $"Project file path '{path}' is not a safe relative path.",
                    "SCRIPT_PROJECT_PATH_INVALID"
                );
        }

        foreach ((string path, string content) in files)
        {
            string full = Path.Combine(root, Normalize(path));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(
                full,
                content,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
            );
        }
        return Result.Success();
    }

    private static string Normalize(string path) => path.Replace('\\', '/').TrimStart('/');

    private void TryDeleteDir(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A transient lock (AV, indexer) must not fail an otherwise good build.
            logger.LogWarning(ex, "Failed to delete script build temp dir {Dir}", dir);
        }
    }
}
