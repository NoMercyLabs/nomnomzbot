// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.IO;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Infrastructure.Widgets.Bundling;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Widgets;

/// <summary>
/// Proves the widget build service's Vue path under the multi-file model: it compiles the SFC (stage A, real Jint)
/// and writes the resulting module to the temp project (kept at its <c>.vue</c> path), materializes a synthetic mount
/// entry, and invokes esbuild from it with the right args and no stdin; it short-circuits before esbuild when the SFC
/// is broken; and — when the esbuild binary is present — yields a self-contained IIFE that references the host Vue
/// global.
/// </summary>
public sealed class EsbuildWidgetBuildServiceVueTests : IClassFixture<VueSfcCompilerFixture>
{
    private readonly VueSfcCompilerFixture _fixture;

    public EsbuildWidgetBuildServiceVueTests(VueSfcCompilerFixture fixture) => _fixture = fixture;

    private const string RepresentativeSfc = """
        <script setup lang="ts">
        import { ref } from 'vue'
        const count = ref<number>(0)
        function increment(): void { count.value++ }
        </script>
        <template>
          <button class="counter" @click="increment">count: {{ count }}</button>
        </template>
        <style scoped>
        .counter { color: v-bind(count); padding: 8px; }
        </style>
        """;

    private const string BrokenSfc = """
        <script setup lang="ts">
        import { ref } from 'vue'
        const n = ref<number>(
        </script>
        <template><p>{{ n }}</p></template>
        """;

    private EsbuildWidgetBuildService Build(
        IProcessRunner runner,
        IConfiguration? configuration = null
    ) =>
        new(
            runner,
            _fixture.Compiler,
            new WidgetDependencyAllowlist(),
            configuration ?? new ConfigurationBuilder().Build(),
            NullLogger<EsbuildWidgetBuildService>.Instance
        );

    [Fact]
    public async Task Vue_compiles_the_sfc_to_a_module_on_disk_and_bundles_the_mount_entry()
    {
        const string bundle = "(function(){/* iife */})();";
        ProcessRunRequest? captured = null;
        string? materializedEntry = null;
        string? mountModule = null;
        IProcessRunner runner = Substitute.For<IProcessRunner>();
        runner
            .RunAsync(Arg.Any<ProcessRunRequest>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                ProcessRunRequest r = ci.Arg<ProcessRunRequest>();
                captured = r;
                // The files still exist on disk at invocation time — they are deleted only after the build returns.
                string dir = r.WorkingDirectory!;
                materializedEntry = File.ReadAllText(Path.Combine(dir, "index.vue"));
                mountModule = File.ReadAllText(Path.Combine(dir, "__nnz_mount__.ts"));
                return new ProcessRunResult(true, 0, bundle, string.Empty);
            });
        EsbuildWidgetBuildService service = Build(runner);

        Result<WidgetBuildOutput> result = await service.BuildAsync(
            WidgetBuildInput.SingleFile("vue", RepresentativeSfc)
        );

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.CompiledBundle.Should().Be(bundle);
        result.Value.ContentHash.Should().MatchRegex("^[0-9a-f]{64}$");

        // Stage B was invoked with the multi-file Vue esbuild contract — from the mount entry, no stdin.
        captured.Should().NotBeNull();
        captured!.FileName.Should().Be("esbuild");
        captured.StandardInput.Should().BeNull();
        captured.WorkingDirectory.Should().NotBeNullOrEmpty();
        captured.Arguments.Should().Contain("--bundle");
        captured.Arguments.Should().Contain("--format=iife");
        captured.Arguments.Should().Contain("--minify");
        captured.Arguments.Should().Contain("--loader:.vue=ts");
        captured.Arguments.Should().Contain("--external:vue");
        captured
            .Arguments.Should()
            .ContainSingle(a => a.StartsWith("--banner:js="))
            .Which.Should()
            .Contain("window.Vue");
        captured.Arguments.Should().Contain("--footer:js=})();");
        captured.Arguments.Should().Contain("__nnz_mount__.ts"); // esbuild bundles from the mount entry

        // The REAL compiled module was written to disk (not the raw SFC), and the mount imports the entry component.
        materializedEntry.Should().NotBeNullOrEmpty();
        materializedEntry.Should().Contain("__sfc_main__");
        materializedEntry.Should().Contain("data-v-");
        materializedEntry.Should().Contain(".counter"); // the scoped CSS is injected by the module
        materializedEntry.Should().NotContain("<template>"); // the SFC was compiled, not passed through
        mountModule.Should().Contain("createApp");
        mountModule.Should().Contain("./index.vue");
    }

    [Fact]
    public async Task A_broken_vue_sfc_fails_the_compile_and_never_invokes_esbuild()
    {
        IProcessRunner runner = Substitute.For<IProcessRunner>();
        EsbuildWidgetBuildService service = Build(runner);

        Result<WidgetBuildOutput> result = await service.BuildAsync(
            WidgetBuildInput.SingleFile("vue", BrokenSfc)
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("WIDGET_VUE_COMPILE_FAILED");
        result.ErrorMessage.Should().NotBeNullOrWhiteSpace();
        // A broken SFC is caught at stage A — esbuild is never shelled out to.
        await runner
            .DidNotReceive()
            .RunAsync(Arg.Any<ProcessRunRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Vue_end_to_end_produces_a_self_contained_iife_referencing_the_vue_global()
    {
        // Real esbuild via the real process runner. Its path comes from Widgets:EsbuildPath (env override
        // Widgets__EsbuildPath) or defaults to "esbuild" on PATH — so this runs the full pipeline locally and
        // gates cleanly on CI where the binary may be absent.
        IConfiguration configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();
        EsbuildWidgetBuildService service = Build(new ProcessRunner(), configuration);

        Result<WidgetBuildOutput> result = await service.BuildAsync(
            WidgetBuildInput.SingleFile("vue", RepresentativeSfc)
        );

        if (result.IsFailure)
        {
            // esbuild is not installed here (e.g. CI without the binary). Stage A still ran and reached stage B —
            // proven by the coded tool-unavailable failure (NOT a compile failure). The full-IIFE assertions need
            // the binary, so gate on it.
            result.ErrorCode.Should().Be("WIDGET_BUILD_TOOL_UNAVAILABLE");
            return;
        }

        string bundle = result.Value.CompiledBundle;
        bundle.Should().NotBeNullOrEmpty();
        bundle.Should().Contain("window.Vue"); // Vue kept external, mapped to the host global by the shim
        bundle.Should().Contain("createApp"); // the component is mounted
        bundle.Should().Contain("data-v-"); // scoped style + scope id survive minification
        bundle.Should().Contain(".counter"); // the scoped CSS is injected as a <style>
        result.Value.ContentHash.Should().MatchRegex("^[0-9a-f]{64}$");
    }
}
