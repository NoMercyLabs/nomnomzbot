// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.DevPlatform.Projects;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Infrastructure.Widgets.Bundling;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Widgets;

/// <summary>
/// Proves the multi-file project model + build (dev-platform.md §4): a project is a file set + manifest, and esbuild
/// resolves cross-file imports into ONE bundle that INCLUDES the imported module's code. Also proves the boundaries —
/// single-file back-compat, the <c>SourceCode → one-file project</c> backfill shape (mirrored by the migration), the
/// dependency allowlist (deny-by-default), a missing entry, and the path-traversal guard.
/// </summary>
public sealed class EsbuildWidgetMultiFileTests : IClassFixture<VueSfcCompilerFixture>
{
    private const string CrossFileMarker = "NNZ_MULTIFILE_MARKER";
    private const string VueLibMarker = "NNZ_VUE_LIB";

    private readonly VueSfcCompilerFixture _fixture;

    public EsbuildWidgetMultiFileTests(VueSfcCompilerFixture fixture) => _fixture = fixture;

    // Real esbuild via the real process runner (path from Widgets:EsbuildPath / PATH), so multi-file bundling runs
    // locally and gates cleanly on CI where the binary may be absent.
    private EsbuildWidgetBuildService RealBuild() =>
        new(
            new ProcessRunner(),
            _fixture.Compiler,
            new WidgetDependencyAllowlist(),
            new ConfigurationBuilder().AddEnvironmentVariables().Build(),
            NullLogger<EsbuildWidgetBuildService>.Instance
        );

    private EsbuildWidgetBuildService FakeBuild(IProcessRunner runner) =>
        new(
            runner,
            _fixture.Compiler,
            new WidgetDependencyAllowlist(),
            new ConfigurationBuilder().Build(),
            NullLogger<EsbuildWidgetBuildService>.Instance
        );

    [Fact]
    public async Task Multi_file_react_project_bundles_the_imported_lib_module_into_one_bundle()
    {
        // The entry imports a symbol from a sibling lib/ module and uses it — esbuild must pull the module's code
        // (its distinctive string literal) into the single output bundle.
        Dictionary<string, string> files = new()
        {
            ["index.tsx"] =
                "import { GREETING } from './lib/util';\n"
                + "const el = document.createElement('div');\n"
                + "el.textContent = GREETING;\n"
                + "document.body.appendChild(el);\n",
            ["lib/util.ts"] = $"export const GREETING: string = '{CrossFileMarker}';\n",
        };
        WidgetBuildInput input = new(
            new ProjectManifest("index.tsx", "widget", "react", []),
            files
        );

        Result<WidgetBuildOutput> result = await RealBuild().BuildAsync(input);

        if (result.IsFailure)
        {
            // esbuild absent (binary-less CI) — the build still failed cleanly with the tool-unavailable code, which
            // proves the project was assembled and reached the bundler. The inclusion assertion needs the binary.
            result.ErrorCode.Should().Be("WIDGET_BUILD_TOOL_UNAVAILABLE", result.ErrorMessage);
            return;
        }

        // The imported lib module's code is IN the single bundle — the whole point of cross-file resolution.
        result.Value.CompiledBundle.Should().Contain(CrossFileMarker);
        result.Value.ContentHash.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public async Task Single_file_and_multi_file_react_run_through_the_same_temp_dir_bundler_without_stdin()
    {
        // The course-correction guarantee: a single-file convenience input and a genuine multi-file project reach
        // esbuild the SAME way — materialized to a temp working dir and bundled from the manifest entry, with NO
        // stdin. There is ONE bundling path; it does not branch on file count, and the old single-source stdin path
        // is gone.
        List<ProcessRunRequest> captured = [];
        IProcessRunner runner = Substitute.For<IProcessRunner>();
        runner
            .RunAsync(Arg.Do<ProcessRunRequest>(r => captured.Add(r)), Arg.Any<CancellationToken>())
            .Returns(new ProcessRunResult(true, 0, "(()=>{})();", string.Empty));
        EsbuildWidgetBuildService service = FakeBuild(runner);

        // (a) single-file convenience — thin sugar over a one-entry project.
        await service.BuildAsync(WidgetBuildInput.SingleFile("react", "export default () => 1;"));
        // (b) genuine multi-file project (entry importing a lib/ module).
        Dictionary<string, string> multiFile = new()
        {
            ["index.tsx"] = "import { X } from './lib/x';\nexport default X;\n",
            ["lib/x.ts"] = "export const X = 1;\n",
        };
        await service.BuildAsync(
            new WidgetBuildInput(new ProjectManifest("index.tsx", "widget", "react", []), multiFile)
        );

        captured.Should().HaveCount(2);
        captured.Should().OnlyContain(r => r.FileName == "esbuild");
        captured.Should().OnlyContain(r => r.StandardInput == null); // no stdin single-source path remains
        captured.Should().OnlyContain(r => !string.IsNullOrEmpty(r.WorkingDirectory)); // temp-dir materialization both times
        captured.Should().OnlyContain(r => r.Arguments.Contains("index.tsx")); // bundled from the manifest entry
        captured.Should().OnlyContain(r => r.Arguments.Contains("--bundle"));
    }

    [Fact]
    public async Task Multi_file_vue_project_bundles_a_non_vue_lib_import_into_the_bundle()
    {
        // A Vue SFC entry that imports a plain .ts helper — the Vue path must compile the SFC AND let esbuild resolve
        // the cross-file helper into the one bundle.
        Dictionary<string, string> files = new()
        {
            ["index.vue"] =
                "<script setup lang=\"ts\">\n"
                + "import { formatLabel } from './lib/format';\n"
                + "const label = formatLabel('hi');\n"
                + "</script>\n"
                + "<template><span class=\"lbl\">{{ label }}</span></template>\n",
            ["lib/format.ts"] =
                $"export function formatLabel(v: string): string {{ return '{VueLibMarker}_' + v; }}\n",
        };
        WidgetBuildInput input = new(new ProjectManifest("index.vue", "widget", "vue", []), files);

        Result<WidgetBuildOutput> result = await RealBuild().BuildAsync(input);

        if (result.IsFailure)
        {
            result.ErrorCode.Should().Be("WIDGET_BUILD_TOOL_UNAVAILABLE", result.ErrorMessage);
            return;
        }

        result.Value.CompiledBundle.Should().Contain(VueLibMarker); // the helper's code was bundled in
        result.Value.CompiledBundle.Should().Contain("window.Vue"); // Vue still kept external
    }

    [Fact]
    public async Task Single_file_project_still_builds_back_compat()
    {
        const string source = "<div id=\"w\">hi</div><script>NomNomz.on('follow')</script>";

        // A single-file project is a one-entry FilesJson — the exact thing single-file authoring produces.
        Result<WidgetBuildOutput> result = await FakeBuild(Substitute.For<IProcessRunner>())
            .BuildAsync(WidgetBuildInput.SingleFile("vanilla", source));

        result.IsSuccess.Should().BeTrue();
        result.Value.CompiledBundle.Should().Be(source); // vanilla passes through unchanged
    }

    [Fact]
    public async Task Backfilled_one_file_project_has_the_migration_shape_and_still_compiles()
    {
        // ProjectScaffold.SingleFile is the transformation the SourceCode -> FilesJson+ManifestJson migration mirrors
        // row-for-row. A legacy vanilla source becomes a one-file project keyed by the framework's entry name...
        const string legacySource = "<div>legacy widget</div>";
        (Dictionary<string, string> files, ProjectManifest manifest) = ProjectScaffold.SingleFile(
            "widget",
            "vanilla",
            legacySource
        );

        files.Should().ContainSingle();
        files.Should().ContainKey("index.html");
        files["index.html"].Should().Be(legacySource);
        manifest.Entry.Should().Be("index.html");
        manifest.Kind.Should().Be("widget");
        manifest.Framework.Should().Be("vanilla");
        manifest.Dependencies.Should().BeEmpty();

        // ...and that one-file project still compiles.
        Result<WidgetBuildOutput> result = await FakeBuild(Substitute.For<IProcessRunner>())
            .BuildAsync(new WidgetBuildInput(manifest, files));

        result.IsSuccess.Should().BeTrue();
        result.Value.CompiledBundle.Should().Be(legacySource);
    }

    [Fact]
    public async Task Cross_file_import_of_a_non_allowlisted_dependency_fails_cleanly()
    {
        // A declared dependency outside the allowlist is denied up-front (deny-by-default) — no npm, no esbuild.
        Dictionary<string, string> files = new()
        {
            ["index.tsx"] = "import _ from 'lodash';\nexport default () => _.identity(1);\n",
        };
        WidgetBuildInput input = new(
            new ProjectManifest("index.tsx", "widget", "react", ["lodash"]),
            files
        );

        IProcessRunner runner = Substitute.For<IProcessRunner>();
        Result<WidgetBuildOutput> result = await FakeBuild(runner).BuildAsync(input);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("WIDGET_DEPENDENCY_NOT_ALLOWED");
        result.ErrorMessage.Should().Contain("lodash");
        // Denied before the bundler is ever launched.
        await runner
            .DidNotReceive()
            .RunAsync(Arg.Any<ProcessRunRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_allowlisted_dependency_is_accepted_and_kept_external()
    {
        // `vue` is on the allowlist, so it passes the gate and is handed to esbuild as an external (never bundled).
        const string bundle = "(()=>{})();";
        ProcessRunRequest? captured = null;
        IProcessRunner runner = Substitute.For<IProcessRunner>();
        runner
            .RunAsync(Arg.Do<ProcessRunRequest>(r => captured = r), Arg.Any<CancellationToken>())
            .Returns(new ProcessRunResult(true, 0, bundle, string.Empty));
        Dictionary<string, string> files = new()
        {
            ["index.tsx"] = "import { createApp } from 'vue';\nexport default createApp;\n",
        };
        WidgetBuildInput input = new(
            new ProjectManifest("index.tsx", "widget", "react", ["vue"]),
            files
        );

        Result<WidgetBuildOutput> result = await FakeBuild(runner).BuildAsync(input);

        result.IsSuccess.Should().BeTrue();
        captured.Should().NotBeNull();
        captured!.Arguments.Should().Contain("--external:vue");
    }

    [Fact]
    public async Task A_manifest_entry_absent_from_the_files_fails_cleanly()
    {
        Dictionary<string, string> files = new() { ["index.tsx"] = "export default 1;" };
        WidgetBuildInput input = new(new ProjectManifest("main.tsx", "widget", "react", []), files);

        Result<WidgetBuildOutput> result = await FakeBuild(Substitute.For<IProcessRunner>())
            .BuildAsync(input);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("WIDGET_PROJECT_ENTRY_MISSING");
    }

    [Fact]
    public async Task A_file_path_that_escapes_the_project_is_rejected_before_touching_disk()
    {
        Dictionary<string, string> files = new() { ["../evil.ts"] = "export default 1;" };
        WidgetBuildInput input = new(
            new ProjectManifest("../evil.ts", "widget", "react", []),
            files
        );

        IProcessRunner runner = Substitute.For<IProcessRunner>();
        Result<WidgetBuildOutput> result = await FakeBuild(runner).BuildAsync(input);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("WIDGET_PROJECT_PATH_INVALID");
        await runner
            .DidNotReceive()
            .RunAsync(Arg.Any<ProcessRunRequest>(), Arg.Any<CancellationToken>());
    }
}
