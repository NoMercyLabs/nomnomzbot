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
using System.Net.Http.Headers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NomNomzBot.Api.Tests.Authentication;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Music.Dtos;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Infrastructure.Content.Identity;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Authorization;

/// <summary>
/// The song queue and the now-playing track are read by the public song-request page, the music widget and
/// every signed-in viewer, so <c>music:queue:read</c> is a community key (Everyone by default). The endpoint
/// must still pass through Gate 2: a broadcaster who raises the key must lock a plain viewer out, and
/// without that raise the viewer must keep reading.
/// </summary>
public sealed class MusicQueueReadGateTests : IAsyncLifetime
{
    private const string ActionKey = "music:queue:read";
    private const int ModeratorLevel = 10;

    private static readonly Guid Owner = Guid.Parse("0192b000-0000-7000-8000-000000000d01");
    private static readonly Guid Viewer = Guid.Parse("0192b000-0000-7000-8000-000000000d02");
    private static readonly Guid ChannelId = Guid.Parse("0192b000-0000-7000-8000-000000000d04");

    private ActAsTestHost _host = null!;

    public async Task InitializeAsync()
    {
        IMusicService music = Substitute.For<IMusicService>();
        music
            .GetQueueAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(
                new MusicQueue(
                    null,
                    [new MusicQueueItem("Song A", "Artist A", null, 1000, "viewer")]
                )
            );
        music
            .GetNowPlayingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>(), Arg.Any<bool>())
            .Returns(
                new NowPlaying("Song A", "Artist A", null, null, 1000, 0, true, 50, null, "spotify")
            );
        IMusicHistoryReader history = Substitute.For<IMusicHistoryReader>();
        history
            .ResolveRequestersAsync(
                Arg.Any<IReadOnlyCollection<string>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(new Dictionary<string, RequesterIdentityDto>());
        IMusicConfigService config = Substitute.For<IMusicConfigService>();
        config
            .GetConfigAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<MusicConfigDto>("none", "NOT_FOUND"));

        _host = await ActAsTestHost.StartAsync(
            Guid.Empty,
            services =>
            {
                services.AddSingleton(music);
                services.AddSingleton(history);
                services.AddSingleton(config);
                services.AddSingleton(Substitute.For<ISongRequestPageTokenService>());
                services.AddSingleton(Substitute.For<IBlockedTrackService>());
                services.AddSingleton(Substitute.For<ISongRequestIntakeService>());
            }
        );
        await _host.SeedAsync(async db =>
        {
            db.Users.AddRange(User(Owner, "owner"), User(Viewer, "viewer"));
            db.Channels.Add(
                new()
                {
                    Id = ChannelId,
                    OwnerUserId = Owner,
                    Name = "owner",
                    NameNormalized = "owner",
                    TwitchChannelId = ChannelId.ToString("N")[^10..],
                    ExternalChannelId = ChannelId.ToString("N")[^12..],
                    IsOnboarded = true,
                }
            );
            await new ActionDefinitionSeeder(db).SeedAsync();
        });
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Theory]
    [InlineData("queue")]
    [InlineData("now-playing")]
    public async Task Viewer_reads_the_song_queue_while_the_key_holds_its_Everyone_default(
        string route
    )
    {
        HttpResponseMessage response = await GetAsync(Viewer, route);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("queue")]
    [InlineData("now-playing")]
    public async Task Viewer_is_forbidden_once_the_broadcaster_raises_the_key_to_moderator(
        string route
    )
    {
        await RaiseKeyToModeratorAsync();

        HttpResponseMessage viewerResponse = await GetAsync(Viewer, route);
        HttpResponseMessage ownerResponse = await GetAsync(Owner, route);

        viewerResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        ownerResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Queue_body_carries_the_queued_items_for_the_viewer()
    {
        HttpResponseMessage response = await GetAsync(Viewer, "queue");

        string body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Song A").And.Contain("Artist A");
    }

    private async Task RaiseKeyToModeratorAsync() =>
        await _host.SeedAsync(async db =>
        {
            Guid definitionId = await db
                .ActionDefinitions.Where(a => a.ActionKey == ActionKey)
                .Select(a => a.Id)
                .SingleAsync();
            db.ChannelActionOverrides.Add(
                new()
                {
                    BroadcasterId = ChannelId,
                    ActionDefinitionId = definitionId,
                    OverrideLevel = ModeratorLevel,
                }
            );
            await db.SaveChangesAsync();
        });

    private async Task<HttpResponseMessage> GetAsync(Guid caller, string route)
    {
        string token = (await _host.LoginAsync(caller, ChannelId)).AccessToken;
        using HttpRequestMessage request = new(
            HttpMethod.Get,
            $"/api/v1/channels/{ChannelId}/music/{route}"
        );
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _host.Client.SendAsync(request);
    }

    private static User User(Guid id, string username) =>
        new()
        {
            Id = id,
            TwitchUserId = id.ToString("N")[^10..],
            Username = username,
            UsernameNormalized = username,
            DisplayName = username,
            Color = "#1E90FF",
        };
}
