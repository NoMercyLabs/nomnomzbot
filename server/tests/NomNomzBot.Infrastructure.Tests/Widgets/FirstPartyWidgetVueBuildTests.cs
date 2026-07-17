// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Infrastructure.Content.Widgets;
using NomNomzBot.Infrastructure.Widgets.Bundling;

namespace NomNomzBot.Infrastructure.Tests.Widgets;

/// <summary>
/// End-to-end proof that the fourteen shipped first-party <c>.vue</c> SFCs are real, compilable widgets: each embedded
/// asset runs through the full build path — stage A compiles the SFC in a real <see cref="JintVueSfcCompiler"/>,
/// stage B bundles it with esbuild (Vue kept external, mapped to the host <c>window.Vue</c>). When the esbuild
/// binary is present the assertions cover the self-contained IIFE (Vue global + render output); when it is absent
/// (e.g. CI without the binary) the test still proves stage A compiled every SFC — the failure is the coded
/// tool-unavailable result, NOT a compile failure — and gates the bundle assertions, exactly like
/// <see cref="EsbuildWidgetBuildServiceVueTests"/>.
/// </summary>
public sealed class FirstPartyWidgetVueBuildTests : IClassFixture<VueSfcCompilerFixture>
{
    private readonly VueSfcCompilerFixture _fixture;

    public FirstPartyWidgetVueBuildTests(VueSfcCompilerFixture fixture) => _fixture = fixture;

    private static string LoadAsset(string key)
    {
        Assembly assembly = typeof(FirstPartyWidgetCatalogueSeeder).Assembly;
        string resourceName = $"NomNomzBot.Infrastructure.Content.Widgets.Assets.{key}.vue";
        using System.IO.Stream? stream = assembly.GetManifestResourceStream(resourceName);
        stream.Should().NotBeNull($"the embedded asset '{resourceName}' should exist");
        using System.IO.StreamReader reader = new(stream!);
        return reader.ReadToEnd();
    }

    [Theory]
    [InlineData("alerts")]
    [InlineData("goal_bar")]
    [InlineData("labels")]
    [InlineData("drop_game")]
    [InlineData("event_ticker")]
    [InlineData("chat_box")]
    [InlineData("now_playing")]
    [InlineData("sr_queue")]
    [InlineData("tts_caption")]
    [InlineData("poll_prediction")]
    [InlineData("redemption_alert")]
    [InlineData("countdown_timer")]
    [InlineData("emote_wall")]
    [InlineData("custom_data")]
    public async Task First_party_sfc_compiles_and_bundles_to_a_vue_iife(string key)
    {
        string sfc = LoadAsset(key);
        sfc.Should().Contain("<script setup").And.Contain("<template>");

        // Real esbuild via the real process runner; its path comes from Widgets:EsbuildPath (env
        // Widgets__EsbuildPath) or defaults to "esbuild" on PATH — so the full pipeline runs locally and gates
        // cleanly on CI where the binary may be absent.
        IConfiguration configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();
        EsbuildWidgetBuildService service = new(
            new ProcessRunner(),
            _fixture.Compiler,
            configuration,
            NullLogger<EsbuildWidgetBuildService>.Instance
        );

        Result<WidgetBuildOutput> result = await service.BuildAsync(
            new WidgetBuildInput("vue", sfc)
        );

        if (result.IsFailure)
        {
            // esbuild is not installed here — but stage A (the real Jint SFC compile) still ran cleanly and reached
            // stage B, proven by the tool-unavailable code (NOT a WIDGET_VUE_COMPILE_FAILED). This is what proves,
            // even on a binary-less CI, that every shipped SFC compiles. The full-IIFE assertions need the binary.
            result.ErrorCode.Should().Be("WIDGET_BUILD_TOOL_UNAVAILABLE", result.ErrorMessage);
            return;
        }

        string bundle = result.Value.CompiledBundle;
        bundle.Should().NotBeNullOrEmpty();
        bundle.Should().StartWith("(function(){"); // the require-shim closure wrapping the IIFE
        bundle.Should().Contain("window.Vue"); // Vue kept external, mapped to the host global by the shim
        bundle.Should().Contain("createApp"); // the component self-mounts
        bundle.Should().Contain("data-v-"); // the scoped-style scope id survives (render output present)
        result.Value.ContentHash.Should().MatchRegex("^[0-9a-f]{64}$");
    }
}
