// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Widgets.Services;

namespace NomNomzBot.Infrastructure.Widgets.Bundling;

/// <summary>
/// The widget compile boundary (widgets-overlays.md §3.2). Vanilla widgets are already browser-ready, so they pass
/// through unchanged (the loop needs no external tool); framework widgets are transpiled + bundled by the standalone
/// <c>esbuild</c> binary (no Node runtime), its path taken from config (<c>Widgets:EsbuildPath</c>, default
/// <c>esbuild</c> on PATH). Every outcome is a <see cref="Result"/> — a failed build carries esbuild's stderr, a
/// missing binary carries an install hint; nothing throws.
/// </summary>
public sealed class EsbuildWidgetBuildService : IWidgetBuildService
{
    private readonly IProcessRunner _process;
    private readonly ILogger<EsbuildWidgetBuildService> _logger;
    private readonly string _esbuildPath;

    public EsbuildWidgetBuildService(
        IProcessRunner process,
        IConfiguration configuration,
        ILogger<EsbuildWidgetBuildService> logger
    )
    {
        _process = process;
        _logger = logger;
        _esbuildPath = configuration["Widgets:EsbuildPath"] ?? "esbuild";
    }

    public async Task<Result<WidgetBuildOutput>> BuildAsync(
        WidgetBuildInput input,
        CancellationToken cancellationToken = default
    )
    {
        string framework = input.Framework.Trim().ToLowerInvariant();
        string source = input.SourceCode;

        // Vanilla = browser-ready HTML/JS: no build step, the source IS the bundle. This keeps create -> edit ->
        // serve -> inject working with zero external dependency; the hash is still computed for cache-busting.
        if (framework == "vanilla")
            return Result.Success(
                new WidgetBuildOutput(source, Sha256Hex(source), "vanilla: no build step")
            );

        // The standalone esbuild binary transpiles + bundles JS/TS/JSX natively (no plugins). Vue and Svelte need
        // the plugin-based build (a tracked follow-on) — fail honestly rather than silently mis-compiling.
        string? loader = framework switch
        {
            "react" => "tsx",
            _ => null,
        };
        if (loader is null)
            return Result.Failure<WidgetBuildOutput>(
                $"Framework '{input.Framework}' needs the plugin-based build, which is not available on the "
                    + "standalone esbuild path yet. Use 'vanilla' or 'react'.",
                "WIDGET_FRAMEWORK_UNSUPPORTED"
            );

        List<string> arguments =
        [
            "--bundle",
            "--format=iife",
            "--minify",
            "--charset=utf8",
            $"--loader={loader}",
            "--jsx=automatic",
        ];

        ProcessRunResult run = await _process.RunAsync(
            new ProcessRunRequest(_esbuildPath, arguments, source),
            cancellationToken
        );

        if (!run.Started)
        {
            _logger.LogWarning(
                "Widget build tool '{Path}' could not be started: {Error}",
                _esbuildPath,
                run.StandardError
            );
            return Result.Failure<WidgetBuildOutput>(
                $"The esbuild binary '{_esbuildPath}' could not be started. Install esbuild (or set "
                    + $"Widgets:EsbuildPath) to compile {framework} widgets.",
                "WIDGET_BUILD_TOOL_UNAVAILABLE"
            );
        }

        if (run.ExitCode != 0)
            return Result.Failure<WidgetBuildOutput>(
                string.IsNullOrWhiteSpace(run.StandardError)
                    ? "The widget build failed."
                    : run.StandardError.Trim(),
                "WIDGET_BUILD_FAILED"
            );

        string bundle = run.StandardOutput;
        return Result.Success(new WidgetBuildOutput(bundle, Sha256Hex(bundle), run.StandardError));
    }

    private static string Sha256Hex(string content) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
}
