// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Net;
using System.Threading.RateLimiting;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using NomNomzBot.Api.Controllers.V1;
using NomNomzBot.Api.RateLimiting;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Music.Dtos;
using NomNomzBot.Application.Music.Services;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// Proves the public (JWT-less) song-request controller: a viewer submission resolves the page token, refuses when
/// the channel is closed (409) or the token is unknown (404), and otherwise queues the requested track against the
/// resolved broadcaster. Also proves the controller's <c>[EnableRateLimiting(Anonymous)]</c> attribute (S067i) is
/// backed by a real, working per-IP budget — a viewer who has the public token URL but no account still hits a
/// throttle, exercised through the exact <see cref="PartitionedRateLimiter{TResource}"/> the middleware uses
/// (mirrors the established <c>RateLimitTierTests</c> pattern; this test project has no WebApplicationFactory).
/// </summary>
public sealed class PublicSongRequestControllerTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000f0c1");

    private static (
        PublicSongRequestController Controller,
        ISongRequestPageTokenService Tokens,
        IMusicService Music
    ) Build()
    {
        ISongRequestPageTokenService tokens = Substitute.For<ISongRequestPageTokenService>();
        IMusicService music = Substitute.For<IMusicService>();
        return (new(tokens, music), tokens, music);
    }

    private static SongRequestPageDto Page(bool accepting) =>
        new(Channel, "CoolStreamer", accepting, ["spotify"]);

    [Fact]
    public async Task Submit_with_unknown_token_returns_not_found()
    {
        (PublicSongRequestController controller, ISongRequestPageTokenService tokens, _) = Build();
        tokens
            .ResolveAsync("nope", Arg.Any<CancellationToken>())
            .Returns(Result.Failure<SongRequestPageDto>("Unknown song-request page.", "NOT_FOUND"));

        IActionResult result = await controller.Submit("nope", new() { Query = "a song" }, default);

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task Submit_when_channel_closed_returns_conflict()
    {
        (PublicSongRequestController controller, ISongRequestPageTokenService tokens, _) = Build();
        tokens
            .ResolveAsync("tok", Arg.Any<CancellationToken>())
            .Returns(Result.Success(Page(accepting: false)));

        IActionResult result = await controller.Submit("tok", new() { Query = "a song" }, default);

        result.Should().BeOfType<ConflictObjectResult>();
    }

    [Fact]
    public async Task Submit_queues_the_track_against_the_resolved_broadcaster()
    {
        (
            PublicSongRequestController controller,
            ISongRequestPageTokenService tokens,
            IMusicService music
        ) = Build();
        tokens
            .ResolveAsync("tok", Arg.Any<CancellationToken>())
            .Returns(Result.Success(Page(accepting: true)));
        music
            .RequestTrackAsync(
                Channel.ToString(),
                "never gonna give you up",
                Arg.Any<string?>(),
                Arg.Any<int?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success(
                    new MusicTrack(
                        "spotify:track:abc",
                        "Never Gonna Give You Up",
                        "Rick Astley",
                        null,
                        null,
                        0,
                        "spotify"
                    )
                )
            );

        IActionResult result = await controller.Submit(
            "tok",
            new() { Query = "never gonna give you up" },
            default
        );

        result.Should().BeOfType<OkObjectResult>();
        await music
            .Received()
            .RequestTrackAsync(
                Channel.ToString(),
                "never gonna give you up",
                Arg.Any<string?>(),
                Arg.Any<int?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task AnonymousRateLimit_RejectsSpamSubmissionsFromOneIpWellBefore200Calls()
    {
        // The exact policy the controller carries: [EnableRateLimiting(RateLimitPolicyNames.Anonymous)].
        // Without a per-IP throttle, anyone holding the public /sr/{token} URL could spam submissions
        // (the authenticated dashboard/chat paths already enforce MaxRequestsPerUser, which an anonymous
        // page submitter has no identity for). This exercises the real PartitionedRateLimiter, not a mock.
        using PartitionedRateLimiter<HttpContext> limiter = PartitionedRateLimiter.Create<
            HttpContext,
            string
        >(AnonymousRateLimitPolicy.Partition);

        HttpContext SubmissionFrom(string ip)
        {
            DefaultHttpContext context = new();
            context.Connection.RemoteIpAddress = IPAddress.Parse(ip);
            return context;
        }

        List<bool> acquired = [];
        for (int i = 0; i < 200; i++)
        {
            using RateLimitLease lease = await limiter.AcquireAsync(
                SubmissionFrom("198.51.100.23")
            );
            acquired.Add(lease.IsAcquired);
        }

        acquired
            .Count(x => x)
            .Should()
            .Be(
                AnonymousRateLimitPolicy.PermitLimit,
                "only the anonymous tier's per-minute budget of song-request submissions from one IP may succeed"
            );
        acquired
            .Count(x => !x)
            .Should()
            .BeGreaterThan(
                0,
                "spamming the public song-request page must be throttled, not accepted forever"
            );
    }

    [Fact]
    public async Task AnonymousRateLimit_StayingUnderTheLimit_NeverRejectsTheSameIp()
    {
        using PartitionedRateLimiter<HttpContext> limiter = PartitionedRateLimiter.Create<
            HttpContext,
            string
        >(AnonymousRateLimitPolicy.Partition);

        HttpContext SubmissionFrom(string ip)
        {
            DefaultHttpContext context = new();
            context.Connection.RemoteIpAddress = IPAddress.Parse(ip);
            return context;
        }

        List<bool> acquired = [];
        for (int i = 0; i < AnonymousRateLimitPolicy.PermitLimit; i++)
        {
            using RateLimitLease lease = await limiter.AcquireAsync(
                SubmissionFrom("198.51.100.99")
            );
            acquired.Add(lease.IsAcquired);
        }

        acquired
            .Should()
            .AllSatisfy(
                x => x.Should().BeTrue(),
                "a viewer submitting song requests within the sane per-minute budget must never be throttled"
            );
    }
}
