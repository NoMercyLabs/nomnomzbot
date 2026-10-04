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
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.CustomCode;
using NomNomzBot.Application.DevPlatform.Projects;
using NomNomzBot.Application.Widgets.Services;

namespace NomNomzBot.Infrastructure.Widgets.Bundling;

/// <summary>
/// The widget compile boundary (widgets-overlays.md §3.2, dev-platform.md §4.2). A project is a file set + manifest,
/// not one string: the service <b>materializes the files to a temp dir</b> and runs the standalone <c>esbuild</c>
/// binary from the manifest <c>entry</c> with <c>--bundle</c>, so cross-file relative imports resolve into ONE bundle;
/// the temp dir is always deleted (try/finally). Vanilla widgets are browser-ready and pass through (the entry file
/// IS the bundle). Vue is a two-stage build: each <c>.vue</c> file is compiled by an <see cref="IVueSfcCompiler"/> to
/// an ES module (kept at its <c>.vue</c> path, loaded via <c>--loader:.vue=ts</c>), then a synthetic mount module is
/// bundled with Vue kept external (host-injected as <c>window.Vue</c>). Dependencies resolve from an
/// <see cref="IWidgetDependencyAllowlist"/> only — never npm. Every outcome is a <see cref="Result"/>: a failed build
/// carries esbuild's stderr, a missing binary an install hint, a bad SFC the code-framed error, an un-allowlisted
/// dependency a coded denial; nothing throws.
/// </summary>
public sealed partial class EsbuildWidgetBuildService : IWidgetBuildService
{
    // The require shim + closure prelude that maps the external `vue` import to the host `window.Vue` global without
    // polluting page scope. esbuild's iife wrapper resolves an external import through a `require` if one is in
    // lexical scope, so we wrap its whole IIFE (banner + footer) in a function that declares a local
    // `require("vue") -> window.Vue`. Each widget bundle is its own closure, so several can coexist on one page.
    private const string VueRequireShimPrelude =
        "(function(){var require=function(m){if(m===\"vue\"){return window.Vue;}"
        + "throw new Error(\"NomNomzBot widget: unresolved import '\"+m+\"'\");};";

    // The synthetic entry that imports the project's root component and mounts it — kept out of the project's own
    // namespace with a reserved name a widget author would never author.
    private const string VueMountModuleName = "__nnz_mount__.ts";

    // esbuild's plain-text log: `[ERROR] <message>`, a blank line, then `    <file>:<line>:<column>:`.
    [GeneratedRegex(
        @"\[ERROR\] (?<message>[^\r\n]+)(?:\r?\n){2}[ \t]+(?<file>[^\r\n]+?):(?<line>\d+):(?<column>\d+):"
    )]
    private static partial Regex LoggedError();

    private readonly IProcessRunner _process;
    private readonly IVueSfcCompiler _vue;
    private readonly IWidgetDependencyAllowlist _allowlist;
    private readonly ILogger<EsbuildWidgetBuildService> _logger;
    private readonly string _esbuildPath;
    private readonly TimeSpan _esbuildTimeout;

    public EsbuildWidgetBuildService(
        IProcessRunner process,
        IVueSfcCompiler vue,
        IWidgetDependencyAllowlist allowlist,
        IConfiguration configuration,
        ILogger<EsbuildWidgetBuildService> logger
    )
    {
        _process = process;
        _vue = vue;
        _allowlist = allowlist;
        _logger = logger;
        _esbuildPath = configuration["Widgets:EsbuildPath"] ?? "esbuild";
        _esbuildTimeout = ProcessRunner.EsbuildTimeout(configuration);
    }

    public async Task<Result<WidgetBuildOutput>> BuildAsync(
        WidgetBuildInput input,
        CancellationToken cancellationToken = default
    )
    {
        ProjectManifest manifest = input.Manifest;
        string framework = manifest.Framework.Trim().ToLowerInvariant();

        // React and svelte have no build and runtime support — refuse before anything else, the same way everywhere.
        if (WidgetFrameworkSupport.IsUnsupported(manifest.Framework))
            return Failed(
                WidgetFrameworkSupport.UnsupportedMessage(manifest.Framework),
                WidgetFrameworkSupport.UnsupportedCode
            );

        // The manifest entry must be one of the project's files — the module esbuild (or the pass-through) reads.
        if (!input.Files.TryGetValue(manifest.Entry, out string? entryContent))
            return Failed(
                $"The manifest entry '{manifest.Entry}' is not present in the project files.",
                "WIDGET_PROJECT_ENTRY_MISSING"
            );

        // Guard every path against traversal before anything touches disk (a project is untrusted input).
        Result pathCheck = ValidateProjectPaths(input.Files);
        if (pathCheck.IsFailure)
            return Result.Failure<WidgetBuildOutput>(
                pathCheck.ErrorMessage,
                pathCheck.ErrorCode,
                errorData: pathCheck.ErrorData
            );

        // Deny-by-default: a declared dependency outside the allowlist fails up-front (no npm, ever). A bare import of
        // an un-allowlisted module that is NOT declared here still fails later — esbuild cannot resolve it off disk.
        foreach (string dependency in manifest.Dependencies)
            if (!_allowlist.IsAllowed(dependency))
                return Failed(
                    $"Dependency '{dependency}' is not on the allowlist — only vetted, bot-provided libraries may be "
                        + $"used (there is no npm install). Allowed: {string.Join(", ", _allowlist.Externals)}.",
                    "WIDGET_DEPENDENCY_NOT_ALLOWED"
                );

        // Vanilla = browser-ready HTML/JS: no build step, the entry file IS the bundle. Keeps create -> edit -> serve
        // -> inject working with zero external dependency; the hash is still computed for cache-busting.
        if (framework == "vanilla")
            return Result.Success(
                new WidgetBuildOutput(
                    entryContent,
                    Sha256Hex(entryContent),
                    "vanilla: no build step"
                )
            );

        // Vue = compile every SFC (Jint) then bundle the mount module with esbuild — its own two-stage path.
        if (framework == "vue")
            return await BuildVueAsync(input, cancellationToken);

        return Failed(
            $"Framework '{manifest.Framework}' is not a known widget framework. Use 'vanilla' or 'vue'.",
            WidgetFrameworkSupport.UnsupportedCode
        );
    }

    // Vue: stage A compiles every .vue in the set to an ES module (default-exporting the component + injecting its
    // scoped CSS), kept at its .vue path so `import './Foo.vue'` resolves; stage B bundles a synthetic mount module
    // that imports the entry's default export and mounts it via the external Vue global. A stage-A failure on ANY SFC
    // short-circuits (esbuild is never invoked on a broken component).
    private async Task<Result<WidgetBuildOutput>> BuildVueAsync(
        WidgetBuildInput input,
        CancellationToken cancellationToken
    )
    {
        Dictionary<string, string> materialized = new(StringComparer.Ordinal);
        List<Result<VueSfcOutput>> failedCompiles = [];
        List<ProjectBuildError> compileErrors = [];
        foreach ((string path, string content) in input.Files)
        {
            if (path.EndsWith(".vue", StringComparison.OrdinalIgnoreCase))
            {
                Result<VueSfcOutput> compiled = _vue.Compile(content, Path.GetFileName(path));
                if (compiled.IsFailure)
                {
                    failedCompiles.Add(compiled);
                    compileErrors.AddRange(VueCompileProblems(compiled, path));
                    continue;
                }
                materialized[path] = BuildVueModule(compiled.Value.ModuleCode, compiled.Value.Css);
            }
            else
            {
                materialized[path] = content;
            }
        }

        // Every broken SFC is reported, each with the project path it came from; esbuild never runs on a broken set.
        if (failedCompiles.Count > 0)
            return Result.Failure<WidgetBuildOutput>(
                string.Join('\n', failedCompiles.Select(f => f.ErrorMessage)),
                failedCompiles[0].ErrorCode,
                failedCompiles[0].ErrorDetail,
                new ProjectBuildFailure(compileErrors)
            );

        materialized[VueMountModuleName] = BuildVueMountModule(input.Manifest.Entry);

        string workDir = CreateTempDir();
        try
        {
            MaterializeFiles(workDir, materialized);
            List<string> arguments =
            [
                "--bundle",
                "--format=iife",
                "--minify",
                "--charset=utf8",
                // Each compiled .vue file carries residual TS (compileScript keeps it) — load .vue with the ts loader.
                "--loader:.vue=ts",
                // Vue is host-injected — keep it external and map it to window.Vue via the require-shim prelude.
                "--external:vue",
                "--banner:js=" + VueRequireShimPrelude,
                "--footer:js=})();",
                VueMountModuleName,
            ];
            return await RunEsbuildAsync(workDir, arguments, "vue", cancellationToken);
        }
        finally
        {
            TryDeleteDir(workDir);
        }
    }

    // A compiled SFC module default-exports its component (bound as __sfc_main__) — the compile-sfc wrapper's
    // contract. Append the scoped-CSS injection so importing this module (root OR a nested component) adds its styles.
    private static string BuildVueModule(string moduleCode, string css)
    {
        if (string.IsNullOrEmpty(css))
            return moduleCode;

        string cssLiteral = JsonSerializer.Serialize(css);
        return moduleCode
            + "\n;(function(){var __nnzCss="
            + cssLiteral
            + ";var __nnzStyle=document.createElement(\"style\");__nnzStyle.textContent=__nnzCss;"
            + "(document.head||document.documentElement).appendChild(__nnzStyle);})();\n";
    }

    // The synthetic entry: import the project's root component (the manifest entry's default export) and mount it onto
    // the widget root using the external Vue global. Its own name is reserved, so it never shadows a project file.
    private static string BuildVueMountModule(string entry)
    {
        string specifier = "./" + NormalizeRelative(entry);
        string specifierLiteral = JsonSerializer.Serialize(specifier);
        return "import __nnzRootComponent from "
            + specifierLiteral
            + ";\n"
            + "import { createApp as __nnzCreateApp } from \"vue\";\n"
            + "(function(){\n"
            + "  var __nnzRoot = (document.currentScript && document.currentScript.parentElement) || document.getElementById(\"app\") || document.body;\n"
            + "  __nnzCreateApp(__nnzRootComponent).mount(__nnzRoot);\n"
            + "})();\n";
    }

    // Runs esbuild in the materialized project dir and maps the outcome to a Result: a launch failure is an install
    // hint, a non-zero exit carries esbuild's stderr, success carries the bundle (stdout) + its cache-bust hash.
    private async Task<Result<WidgetBuildOutput>> RunEsbuildAsync(
        string workDir,
        List<string> arguments,
        string framework,
        CancellationToken cancellationToken
    )
    {
        ProcessRunResult run = await _process.RunAsync(
            new(
                _esbuildPath,
                arguments,
                StandardInput: null,
                WorkingDirectory: workDir,
                Timeout: _esbuildTimeout
            ),
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
        {
            string message = string.IsNullOrWhiteSpace(run.StandardError)
                ? "The widget build failed."
                : run.StandardError.Trim();
            List<ProjectBuildError> problems = ParseLoggedErrors(run.StandardError);
            return Result.Failure<WidgetBuildOutput>(
                message,
                "WIDGET_BUILD_FAILED",
                errorData: new ProjectBuildFailure(
                    problems.Count > 0
                        ? problems
                        : [new("WIDGET_BUILD_FAILED", message, null, null, null)]
                )
            );
        }

        string bundle = run.StandardOutput;
        return Result.Success(new WidgetBuildOutput(bundle, Sha256Hex(bundle), run.StandardError));
    }

    // One problem per `[ERROR]` esbuild logged, each at its own file, line and column.
    private static List<ProjectBuildError> ParseLoggedErrors(string standardError) =>
        LoggedError()
            .Matches(standardError)
            .Select(m => new ProjectBuildError(
                "WIDGET_BUILD_FAILED",
                m.Groups["message"].Value,
                m.Groups["file"].Value,
                int.Parse(m.Groups["line"].Value),
                // esbuild counts columns from 0; editors from 1.
                int.Parse(m.Groups["column"].Value) + 1
            ))
            .ToList();

    // The SFC compiler's own problems, re-pointed at the project path (it only knows the file's base name).
    private static IEnumerable<ProjectBuildError> VueCompileProblems(
        Result<VueSfcOutput> failed,
        string path
    ) =>
        (failed.ErrorData as ProjectBuildFailure)?.Errors.Select(e => e with { File = path })
        ?? [new(failed.ErrorCode ?? "WIDGET_BUILD_FAILED", failed.ErrorMessage!, path, null, null)];

    private static Result<WidgetBuildOutput> Failed(string message, string code) =>
        Result.Failure<WidgetBuildOutput>(
            message,
            code,
            errorData: new ProjectBuildFailure([new(code, message, null, null, null)])
        );

    // Rejects any path that could escape the temp dir (rooted, drive-qualified, or containing a `..` segment) — a
    // project's file paths are untrusted, and they become real files on disk.
    private static Result ValidateProjectPaths(IReadOnlyDictionary<string, string> files)
    {
        foreach (string path in files.Keys)
        {
            if (string.IsNullOrWhiteSpace(path))
                return Result.Failure(
                    "A project file path is empty.",
                    "WIDGET_PROJECT_PATH_INVALID",
                    errorData: new ProjectBuildFailure([
                        new(
                            "WIDGET_PROJECT_PATH_INVALID",
                            "A project file path is empty.",
                            null,
                            null,
                            null
                        ),
                    ])
                );

            string normalized = path.Replace('\\', '/');
            if (
                Path.IsPathRooted(normalized)
                || normalized.StartsWith('/')
                || normalized.Contains(':')
                || normalized.Split('/').Any(segment => segment == "..")
            )
            {
                string message = $"Project file path '{path}' is not a safe relative path.";
                return Result.Failure(
                    message,
                    "WIDGET_PROJECT_PATH_INVALID",
                    errorData: new ProjectBuildFailure([
                        new("WIDGET_PROJECT_PATH_INVALID", message, path, null, null),
                    ])
                );
            }
        }
        return Result.Success();
    }

    private static void MaterializeFiles(string workDir, IReadOnlyDictionary<string, string> files)
    {
        foreach ((string path, string content) in files)
        {
            string full = Path.Combine(workDir, NormalizeRelative(path));
            string? directory = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllText(
                full,
                content,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
            );
        }
    }

    private static string NormalizeRelative(string path) => path.Replace('\\', '/').TrimStart('/');

    private static string CreateTempDir()
    {
        string dir = Path.Combine(
            Path.GetTempPath(),
            "nnz-widget-build-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(dir);
        return dir;
    }

    private void TryDeleteDir(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort cleanup — a transient lock (AV, indexer) must not fail an otherwise-successful build.
            _logger.LogWarning(ex, "Failed to delete widget build temp dir {Dir}", dir);
        }
    }

    private static string Sha256Hex(string content) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
}
