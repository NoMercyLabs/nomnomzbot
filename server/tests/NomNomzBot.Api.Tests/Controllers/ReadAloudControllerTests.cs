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

using FluentAssertions;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using NomNomzBot.Api.Controllers;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Tts.Services;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// Proves the public Read Aloud route: the cacheable GET answers audio with a long public cache header (so the
/// browser and Cloudflare keep each sentence), a refusal carries no cache header, and the CORS policy lets the
/// docs site call it without credentials.
/// </summary>
public sealed class ReadAloudControllerTests
{
    private static readonly byte[] Mp3 = [1, 2, 3, 4];

    private static (ReadAloudController Controller, IReadAloudService Service) Build()
    {
        IReadAloudService service = Substitute.For<IReadAloudService>();
        ReadAloudController controller = new(service)
        {
            ControllerContext = new() { HttpContext = new DefaultHttpContext() },
        };
        return (controller, service);
    }

    [Fact]
    public async Task Get_returns_the_audio_with_a_long_public_cache_header()
    {
        (ReadAloudController controller, IReadAloudService service) = Build();
        service
            .SynthesizeAsync("Hello there.", "en", Arg.Any<CancellationToken>())
            .Returns(Result.Success(new TtsResult(Mp3, 900, "voice-1", "edge")));

        IActionResult result = await controller.SpeakCached(
            "Hello there.",
            "en",
            CancellationToken.None
        );

        FileContentResult file = result.Should().BeOfType<FileContentResult>().Subject;
        file.ContentType.Should().Be("audio/mpeg");
        file.FileContents.Should().Equal(Mp3);
        string cacheControl = controller.Response.Headers.CacheControl.ToString();
        cacheControl.Should().Contain("public");
        cacheControl.Should().Contain("max-age=604800");
        cacheControl.Should().Contain("s-maxage=2592000");
    }

    [Fact]
    public async Task Get_with_text_that_fails_validation_is_400_and_never_cached()
    {
        (ReadAloudController controller, IReadAloudService service) = Build();
        service
            .SynthesizeAsync("", "en", Arg.Any<CancellationToken>())
            .Returns(Result.Failure<TtsResult>("bad text", "VALIDATION_FAILED"));

        IActionResult result = await controller.SpeakCached("", "en", CancellationToken.None);

        result.Should().BeOfType<StatusCodeResult>().Which.StatusCode.Should().Be(400);
        controller.Response.Headers.CacheControl.ToString().Should().BeEmpty();
    }

    [Fact]
    public async Task Get_when_the_voice_is_unavailable_is_503_and_never_cached()
    {
        (ReadAloudController controller, IReadAloudService service) = Build();
        service
            .SynthesizeAsync("Hello.", "nl", Arg.Any<CancellationToken>())
            .Returns(Result.Failure<TtsResult>("down", "TTS_UNAVAILABLE"));

        IActionResult result = await controller.SpeakCached("Hello.", "nl", CancellationToken.None);

        result.Should().BeOfType<StatusCodeResult>().Which.StatusCode.Should().Be(503);
        controller.Response.Headers.CacheControl.ToString().Should().BeEmpty();
    }

    [Fact]
    public async Task Post_still_returns_the_audio_for_the_dashboard()
    {
        (ReadAloudController controller, IReadAloudService service) = Build();
        service
            .SynthesizeAsync("Hello.", "en", Arg.Any<CancellationToken>())
            .Returns(Result.Success(new TtsResult(Mp3, 900, "voice-1", "edge")));

        IActionResult result = await controller.Speak(new("Hello.", "en"), CancellationToken.None);

        result.Should().BeOfType<FileContentResult>().Which.FileContents.Should().Equal(Mp3);
    }

    [Fact]
    public void The_cors_policy_allows_the_docs_site_by_default_and_never_sends_credentials()
    {
        IConfiguration empty = new ConfigurationBuilder().Build();
        CorsOptions options = new();

        ReadAloudCors.Configure(options, empty);

        CorsPolicy policy = options.GetPolicy(ReadAloudCors.PolicyName)!;
        policy.Origins.Should().Contain("https://docs.nomnomz.bot");
        policy.SupportsCredentials.Should().BeFalse();
        policy.Methods.Should().BeEquivalentTo("GET", "POST");
    }

    [Fact]
    public void The_cors_origins_come_from_the_read_aloud_setting_when_it_is_set()
    {
        IConfiguration configured = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ReadAloud:Origins:0"] = "https://help.example.org",
                }
            )
            .Build();
        CorsOptions options = new();

        ReadAloudCors.Configure(options, configured);

        options
            .GetPolicy(ReadAloudCors.PolicyName)!
            .Origins.Should()
            .BeEquivalentTo("https://help.example.org");
    }
}
