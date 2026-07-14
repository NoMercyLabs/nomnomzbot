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
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Widgets.Services;

namespace NomNomzBot.Infrastructure.Widgets.Bundling;

/// <summary>
/// The widget compile boundary (widgets-overlays.md §3.2). Vanilla widgets are already browser-ready, so they pass
/// through unchanged (the loop needs no external tool); framework widgets are transpiled + bundled by the standalone
/// <c>esbuild</c> binary (no Node runtime), its path taken from config (<c>Widgets:EsbuildPath</c>, default
/// <c>esbuild</c> on PATH). Vue is a two-stage build: an <see cref="IVueSfcCompiler"/> turns the SFC into one ES
/// module + scoped CSS (in-process, via Jint), then esbuild bundles that module with Vue kept external. Every
/// outcome is a <see cref="Result"/> — a failed build carries esbuild's stderr, a missing binary carries an install
/// hint, a bad SFC carries the code-framed compile error; nothing throws.
/// </summary>
public sealed class EsbuildWidgetBuildService : IWidgetBuildService
{
    // The require shim + closure prelude that maps the external `vue` import to the host `window.Vue` global without
    // polluting page scope. esbuild's iife wrapper resolves an external import through a `require` if one is in
    // lexical scope, so we wrap its whole IIFE (banner + footer) in a function that declares a local
    // `require("vue") -> window.Vue`. Each widget bundle is its own closure, so several can coexist on one page.
    private const string VueRequireShimPrelude =
        "(function(){var require=function(m){if(m===\"vue\"){return window.Vue;}"
        + "throw new Error(\"NomNomzBot widget: unresolved import '\"+m+\"'\");};";

    private readonly IProcessRunner _process;
    private readonly IVueSfcCompiler _vue;
    private readonly ILogger<EsbuildWidgetBuildService> _logger;
    private readonly string _esbuildPath;

    public EsbuildWidgetBuildService(
        IProcessRunner process,
        IVueSfcCompiler vue,
        IConfiguration configuration,
        ILogger<EsbuildWidgetBuildService> logger
    )
    {
        _process = process;
        _vue = vue;
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

        // Vue = compile the SFC (Jint) then bundle the module with esbuild — its own two-stage path.
        if (framework == "vue")
            return await BuildVueAsync(source, cancellationToken);

        // The standalone esbuild binary transpiles + bundles JS/TS/JSX natively (no plugins). Svelte needs the
        // plugin-based build (a tracked follow-on) — fail honestly rather than silently mis-compiling.
        string? loader = framework switch
        {
            "react" => "tsx",
            _ => null,
        };
        if (loader is null)
            return Result.Failure<WidgetBuildOutput>(
                $"Framework '{input.Framework}' needs the plugin-based build, which is not available on the "
                    + "standalone esbuild path yet. Use 'vanilla', 'react', or 'vue'.",
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

        return await RunEsbuildAsync(source, arguments, framework, cancellationToken);
    }

    // Stage A: compile the SFC to an ES module + scoped CSS in-process. Stage B: bundle that module with esbuild —
    // Vue stays external (host-injected as window.Vue), the ts loader strips the residual TS, and the module
    // self-mounts. A stage-A failure short-circuits (esbuild is never invoked on a broken SFC).
    private async Task<Result<WidgetBuildOutput>> BuildVueAsync(
        string source,
        CancellationToken cancellationToken
    )
    {
        Result<VueSfcOutput> compiled = _vue.Compile(source, "Widget.vue");
        if (compiled.IsFailure)
            return Result.Failure<WidgetBuildOutput>(
                compiled.ErrorMessage,
                compiled.ErrorCode,
                compiled.ErrorDetail
            );

        string entry = BuildVueEntryModule(compiled.Value.ModuleCode, compiled.Value.Css);
        List<string> arguments =
        [
            "--bundle",
            "--format=iife",
            "--minify",
            "--charset=utf8",
            // compileScript keeps TS/JSX syntax; the ts loader strips it (a superset that also handles plain JS).
            "--loader=ts",
            // Vue is host-injected — keep it external and map it to window.Vue via the require-shim prelude.
            "--external:vue",
            "--banner:js=" + VueRequireShimPrelude,
            "--footer:js=})();",
        ];

        return await RunEsbuildAsync(entry, arguments, "vue", cancellationToken);
    }

    // The compiled module default-exports the component under the stable binding `__sfc_main__` (the compile-sfc
    // wrapper's contract). Append the host mount: inject the scoped CSS as a <style> and mount the component onto
    // the widget root using the external Vue global.
    private static string BuildVueEntryModule(string moduleCode, string css)
    {
        string cssLiteral = JsonSerializer.Serialize(css);
        return moduleCode
            + "\nimport { createApp as __nnzCreateApp } from \"vue\";\n"
            + "(function(){\n"
            + "  var __nnzCss = "
            + cssLiteral
            + ";\n"
            + "  if (__nnzCss) { var __nnzStyle = document.createElement(\"style\"); __nnzStyle.textContent = __nnzCss; (document.head || document.documentElement).appendChild(__nnzStyle); }\n"
            + "  var __nnzRoot = (document.currentScript && document.currentScript.parentElement) || document.getElementById(\"app\") || document.body;\n"
            + "  __nnzCreateApp(__sfc_main__).mount(__nnzRoot);\n"
            + "})();\n";
    }

    // Runs esbuild over stdin and maps the outcome to a Result: a launch failure is an install hint, a non-zero
    // exit carries esbuild's stderr, success carries the bundle + its cache-bust hash (stderr = warnings).
    private async Task<Result<WidgetBuildOutput>> RunEsbuildAsync(
        string stdin,
        List<string> arguments,
        string framework,
        CancellationToken cancellationToken
    )
    {
        ProcessRunResult run = await _process.RunAsync(
            new ProcessRunRequest(_esbuildPath, arguments, stdin),
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
