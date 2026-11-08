// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
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
/// The media-share widget plays a Twitch clip through the vendored NoMercy video player, loaded from the bot's own
/// origin (<c>/vendor/nomercy-player.js</c>), never an iframe or a third-party CDN. These tests run the real build
/// path on the embedded widget source and check what a saved widget would carry.
/// </summary>
public sealed class MediaShareClipPlayerBuildTests : IClassFixture<VueSfcCompilerFixture>
{
    private const string VendorPath = "/vendor/nomercy-player.js";

    private readonly VueSfcCompilerFixture _fixture;

    public MediaShareClipPlayerBuildTests(VueSfcCompilerFixture fixture) => _fixture = fixture;

    private static string LoadSource()
    {
        Assembly assembly = typeof(FirstPartyWidgetCatalogueSeeder).Assembly;
        using System.IO.Stream? stream = assembly.GetManifestResourceStream(
            "NomNomzBot.Infrastructure.Content.Widgets.Assets.media_share.vue"
        );
        stream.Should().NotBeNull();
        using StreamReader reader = new(stream!);
        return reader.ReadToEnd();
    }

    [Fact]
    public void Widget_source_loads_the_vendor_player_from_the_bot_origin_and_has_no_clip_iframe()
    {
        string source = LoadSource();

        source.Should().Contain(VendorPath);
        source.Should().NotContain("clips.twitch.tv/embed");
        source.Should().NotContain("<iframe");
        source
            .Should()
            .NotContain("cdn.jsdelivr")
            .And.NotContain("unpkg.com")
            .And.NotContain("esm.sh");
    }

    [Fact]
    public async Task Built_widget_succeeds_and_references_the_vendor_file()
    {
        IConfiguration configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();
        EsbuildWidgetBuildService service = new(
            new ProcessRunner(),
            _fixture.Compiler,
            new WidgetDependencyAllowlist(),
            configuration,
            NullLogger<EsbuildWidgetBuildService>.Instance
        );

        Result<WidgetBuildOutput> result = await service.BuildAsync(
            WidgetBuildInput.SingleFile("vue", LoadSource())
        );

        if (result.IsFailure)
        {
            // No esbuild binary on this machine: stage A (the SFC compile) still has to have passed.
            result.ErrorCode.Should().Be("WIDGET_BUILD_TOOL_UNAVAILABLE", result.ErrorMessage);
            return;
        }

        result.Value.CompiledBundle.Should().Contain(VendorPath);
        result.Value.CompiledBundle.Should().NotContain("clips.twitch.tv/embed");
    }
}
