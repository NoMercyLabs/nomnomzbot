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
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using NomNomzBot.Api.Controllers;
using NomNomzBot.Api.RateLimiting;
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

    private const string DocsOrigin = "https://docs.nomnomz.bot";

    // The request comes from the docs site, which the gate allows by default.
    private static (ReadAloudController Controller, IReadAloudService Service) Build(
        string? origin = DocsOrigin,
        string host = "bot.example",
        string? baseUrl = null
    )
    {
        IReadAloudService service = Substitute.For<IReadAloudService>();
        DefaultHttpContext http = new();
        http.Request.Host = new(host);
        if (origin is not null)
            http.Request.Headers.Origin = origin;

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["App:BaseUrl"] = baseUrl })
            .Build();

        ReadAloudController controller = new(service, new(configuration))
        {
            ControllerContext = new() { HttpContext = http },
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
        controller.Response.Headers.CacheControl.ToString().Should().Be("no-store");
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
        controller.Response.Headers.CacheControl.ToString().Should().Be("no-store");
    }

    // Stoney 2026-10-29: the read-aloud clips must not be usable from any site, and the prod copy is cached
    // heavily on Cloudflare's side. A cached refusal or an empty clip would be kept for a month.
    [Fact]
    public async Task A_clip_with_no_audio_is_503_and_never_cached()
    {
        (ReadAloudController controller, IReadAloudService service) = Build();
        service
            .SynthesizeAsync("Hello.", "en", Arg.Any<CancellationToken>())
            .Returns(Result.Success(new TtsResult([], 0, "voice-1", "edge")));

        IActionResult result = await controller.SpeakCached("Hello.", "en", CancellationToken.None);

        result.Should().BeOfType<StatusCodeResult>().Which.StatusCode.Should().Be(503);
        controller.Response.Headers.CacheControl.ToString().Should().Be("no-store");
    }

    [Theory]
    [InlineData("https://evil.example")]
    [InlineData("https://docs.nomnomz.bot.evil.example")]
    [InlineData("http://docs.nomnomz.bot")]
    [InlineData(null)]
    public async Task A_request_from_a_site_that_is_not_allowed_is_refused_before_anything_is_spoken(
        string? origin
    )
    {
        (ReadAloudController controller, IReadAloudService service) = Build(origin);

        IActionResult get = await controller.SpeakCached("Hello.", "en", CancellationToken.None);
        IActionResult post = await controller.Speak(new("Hello.", "en"), CancellationToken.None);

        get.Should().BeOfType<StatusCodeResult>().Which.StatusCode.Should().Be(403);
        post.Should().BeOfType<StatusCodeResult>().Which.StatusCode.Should().Be(403);
        controller.Response.Headers.CacheControl.ToString().Should().Be("no-store");
        await service.DidNotReceiveWithAnyArgs().SynthesizeAsync(default!, default!, default);
    }

    [Fact]
    public async Task The_site_that_serves_the_bot_itself_may_use_it_as_named_in_the_app_base_url()
    {
        (ReadAloudController controller, IReadAloudService service) = Build(
            origin: "https://bot.example",
            baseUrl: "https://bot.example/"
        );
        service
            .SynthesizeAsync("Hello.", "en", Arg.Any<CancellationToken>())
            .Returns(Result.Success(new TtsResult(Mp3, 900, "voice-1", "edge")));

        IActionResult result = await controller.Speak(new("Hello.", "en"), CancellationToken.None);

        result.Should().BeOfType<FileContentResult>();
    }

    // A caller controls its own Host and X-Forwarded-Host, so neither may make a site "the bot itself".
    [Fact]
    public async Task A_forged_host_header_does_not_make_a_site_allowed()
    {
        (ReadAloudController controller, IReadAloudService service) = Build(
            origin: "https://evil.example",
            host: "evil.example"
        );
        controller.Request.Headers["X-Forwarded-Host"] = "evil.example";

        IActionResult result = await controller.Speak(new("Hello.", "en"), CancellationToken.None);

        result.Should().BeOfType<StatusCodeResult>().Which.StatusCode.Should().Be(403);
        await service.DidNotReceiveWithAnyArgs().SynthesizeAsync(default!, default!, default);
    }

    // Cloudflare keeps one copy of a clip for every caller on the plans that cannot add the Origin header to the
    // cache key, so the answer must be the same for every allowed site. The CORS header is a constant "*" (the
    // route sends no credentials) and the answer does not vary by Origin.
    [Fact]
    public async Task A_clip_is_the_same_answer_for_every_allowed_site_so_one_cached_copy_serves_them_all()
    {
        (ReadAloudController controller, IReadAloudService service) = Build();
        service
            .SynthesizeAsync("Hello.", "en", Arg.Any<CancellationToken>())
            .Returns(Result.Success(new TtsResult(Mp3, 900, "voice-1", "edge")));

        await controller.SpeakCached("Hello.", "en", CancellationToken.None);

        controller.Response.Headers.Vary.ToString().Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("en;drop table")]
    [InlineData("e")]
    [InlineData("english-united-states-of-america-extra-long")]
    [InlineData("../../x")]
    public async Task A_locale_that_is_not_a_language_code_is_400_so_it_cannot_fill_the_cache_with_junk(
        string locale
    )
    {
        (ReadAloudController controller, IReadAloudService service) = Build();

        IActionResult result = await controller.SpeakCached(
            "Hello.",
            locale,
            CancellationToken.None
        );

        result.Should().BeOfType<StatusCodeResult>().Which.StatusCode.Should().Be(400);
        controller.Response.Headers.CacheControl.ToString().Should().Be("no-store");
        await service.DidNotReceiveWithAnyArgs().SynthesizeAsync(default!, default!, default);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("nl")]
    [InlineData("en-US")]
    [InlineData("pt_BR")]
    public async Task A_language_code_is_accepted(string locale)
    {
        (ReadAloudController controller, IReadAloudService service) = Build();
        service
            .SynthesizeAsync("Hello.", locale, Arg.Any<CancellationToken>())
            .Returns(Result.Success(new TtsResult(Mp3, 900, "voice-1", "edge")));

        IActionResult result = await controller.SpeakCached(
            "Hello.",
            locale,
            CancellationToken.None
        );

        result.Should().BeOfType<FileContentResult>();
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
    public void The_route_is_on_its_own_tighter_rate_limit_tier()
    {
        EnableRateLimitingAttribute tier = typeof(ReadAloudController)
            .GetCustomAttributes(typeof(EnableRateLimitingAttribute), inherit: false)
            .Cast<EnableRateLimitingAttribute>()
            .Single();

        tier.PolicyName.Should().Be(RateLimitPolicyNames.ReadAloud);
    }

    [Fact]
    public void The_cors_policy_is_one_constant_answer_and_never_sends_credentials()
    {
        CorsOptions options = new();

        ReadAloudCors.Configure(options);

        // Which sites may call is decided by ReadAloudOriginGate and the edge rule, not by this header: a cached
        // clip would carry one site's name to every other site.
        CorsPolicy policy = options.GetPolicy(ReadAloudCors.PolicyName)!;
        policy.AllowAnyOrigin.Should().BeTrue();
        policy.SupportsCredentials.Should().BeFalse();
        policy.Methods.Should().BeEquivalentTo("GET", "POST");
    }

    [Fact]
    public void The_allowed_sites_are_the_docs_sites_by_default()
    {
        IConfiguration empty = new ConfigurationBuilder().Build();

        ReadAloudCors
            .AllowedOrigins(empty)
            .Should()
            .BeEquivalentTo("https://docs.nomnomz.bot", "https://docs-dev.nomnomz.bot");
    }

    [Fact]
    public void The_allowed_sites_come_from_the_read_aloud_setting_when_it_is_set()
    {
        IConfiguration configured = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ReadAloud:Origins:0"] = "https://help.example.org",
                }
            )
            .Build();

        ReadAloudCors
            .AllowedOrigins(configured)
            .Should()
            .BeEquivalentTo("https://help.example.org");
    }
}
