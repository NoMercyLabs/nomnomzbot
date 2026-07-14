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
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Infrastructure.Widgets.Bundling;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Widgets;

/// <summary>
/// Proves the widget build service's Vue path: it compiles the SFC (stage A, real Jint) and hands the resulting
/// module to esbuild (stage B) with the right args and stdin, short-circuits before esbuild when the SFC is broken,
/// and — when the esbuild binary is present — yields a self-contained IIFE that references the host Vue global.
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
            configuration ?? new ConfigurationBuilder().Build(),
            NullLogger<EsbuildWidgetBuildService>.Instance
        );

    [Fact]
    public async Task Vue_compiles_the_sfc_and_bundles_it_with_the_ts_loader_and_external_vue()
    {
        const string bundle = "(function(){/* iife */})();";
        ProcessRunRequest? captured = null;
        IProcessRunner runner = Substitute.For<IProcessRunner>();
        runner
            .RunAsync(Arg.Do<ProcessRunRequest>(r => captured = r), Arg.Any<CancellationToken>())
            .Returns(new ProcessRunResult(true, 0, bundle, string.Empty));
        EsbuildWidgetBuildService service = Build(runner);

        Result<WidgetBuildOutput> result = await service.BuildAsync(
            new WidgetBuildInput("vue", RepresentativeSfc)
        );

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.CompiledBundle.Should().Be(bundle);
        result.Value.ContentHash.Should().MatchRegex("^[0-9a-f]{64}$");

        // Stage B was invoked with the Vue-specific esbuild contract.
        captured.Should().NotBeNull();
        captured!.FileName.Should().Be("esbuild");
        captured.Arguments.Should().Contain("--bundle");
        captured.Arguments.Should().Contain("--format=iife");
        captured.Arguments.Should().Contain("--minify");
        // ts loader (compileScript keeps TS syntax) + vue kept external + the require-shim closure mapping to
        // window.Vue.
        captured.Arguments.Should().Contain("--loader=ts");
        captured.Arguments.Should().Contain("--external:vue");
        captured
            .Arguments.Should()
            .ContainSingle(a => a.StartsWith("--banner:js="))
            .Which.Should()
            .Contain("window.Vue");
        captured.Arguments.Should().Contain("--footer:js=})();");

        // The REAL compiled module + the host mount are what got piped to esbuild — not the raw SFC source.
        captured.StandardInput.Should().NotBeNullOrEmpty();
        captured.StandardInput.Should().Contain("__sfc_main__");
        captured.StandardInput.Should().Contain("createApp");
        captured.StandardInput.Should().Contain("data-v-");
        captured.StandardInput.Should().Contain(".counter");
        captured.StandardInput.Should().NotContain("<template>"); // the SFC was compiled, not passed through
    }

    [Fact]
    public async Task A_broken_vue_sfc_fails_the_compile_and_never_invokes_esbuild()
    {
        IProcessRunner runner = Substitute.For<IProcessRunner>();
        EsbuildWidgetBuildService service = Build(runner);

        Result<WidgetBuildOutput> result = await service.BuildAsync(
            new WidgetBuildInput("vue", BrokenSfc)
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
            new WidgetBuildInput("vue", RepresentativeSfc)
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
