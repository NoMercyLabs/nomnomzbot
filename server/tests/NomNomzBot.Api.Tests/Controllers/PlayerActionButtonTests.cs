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
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using NomNomzBot.Api.Controllers.V1;
using NomNomzBot.Api.Models;
using NomNomzBot.Api.Tests.Authentication;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Music.Dtos;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Platform.Persistence;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// Any button anywhere can run any action: the run endpoint hands the action, its parameters and the context
/// variables to the owner executor, and refuses a caller below the <c>pipelines:write</c> level. The Now playing
/// panel's buttons are stored per channel and the playing track carries its URI.
/// </summary>
public sealed class PlayerActionButtonTests : IAsyncLifetime
{
    private static readonly Guid Owner = Guid.Parse("0192b000-0000-7000-8000-000000000e01");
    private static readonly Guid Mod = Guid.Parse("0192b000-0000-7000-8000-000000000e03");
    private static readonly Guid ChannelId = Guid.Parse("0192b000-0000-7000-8000-000000000e02");

    private ActAsTestHost _host = null!;

    public async Task InitializeAsync()
    {
        _host = await ActAsTestHost.StartAsync(Guid.Parse("0192b000-0000-7000-8000-0000000000ff"));
        await _host.SeedAsync(SeedAsync);
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private async Task<HttpResponseMessage> SendAsync(
        Guid user,
        HttpMethod method,
        string path,
        object? body
    )
    {
        string token = (await _host.LoginAsync(user, ChannelId)).AccessToken;
        using HttpRequestMessage request = new(method, $"/api/v1/channels/{ChannelId}/{path}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        return await _host.Client.SendAsync(request);
    }

    [Fact]
    public async Task Run_passes_the_action_parameters_and_context_variables_to_the_executor()
    {
        _host
            .OwnerActions.RunAsync(Arg.Any<OwnerActionRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new WidgetActionOutcome(
                        true,
                        "ok",
                        null,
                        new Dictionary<string, string> { ["result"] = "1" }
                    )
                )
            );

        HttpResponseMessage response = await SendAsync(
            Owner,
            HttpMethod.Post,
            "actions/run",
            new
            {
                actionType = "run_pipeline",
                parameters = new { pipeline = "thank-requester" },
                variables = new Dictionary<string, string>
                {
                    ["target.id"] = "42",
                    ["song.name"] = "Song",
                },
            }
        );

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonNode data = JsonNode.Parse(await response.Content.ReadAsStringAsync())!["data"]!;
        data["success"]!.GetValue<bool>().Should().BeTrue();
        data["output"]!.GetValue<string>().Should().Be("ok");
        data["variables"]!["result"]!.GetValue<string>().Should().Be("1");
        await _host
            .OwnerActions.Received(1)
            .RunAsync(
                Arg.Is<OwnerActionRequest>(r =>
                    r.BroadcasterId == ChannelId
                    && r.ActionType == "run_pipeline"
                    && r.Parameters!["pipeline"].GetString() == "thank-requester"
                    && r.Variables!["target.id"] == "42"
                    && r.Variables["song.name"] == "Song"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Run_refuses_a_caller_below_the_action_level_and_never_reaches_the_executor()
    {
        HttpResponseMessage response = await SendAsync(
            Mod,
            HttpMethod.Post,
            "actions/run",
            new { actionType = "send_message" }
        );

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        JsonNode body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        body["action"]!.GetValue<string>().Should().Be("pipelines:write");
        await _host
            .OwnerActions.DidNotReceiveWithAnyArgs()
            .RunAsync(default!, CancellationToken.None);
    }

    [Fact]
    public async Task Player_buttons_round_trip_with_an_assigned_id_and_the_stored_shape()
    {
        HttpResponseMessage put = await SendAsync(
            Owner,
            HttpMethod.Put,
            "music/player-buttons",
            new[]
            {
                new
                {
                    id = "",
                    label = "  Thank  ",
                    icon = "heart",
                    actionType = "send_message",
                    parameters = new { message = "thanks {{target.name}}" },
                },
            }
        );
        put.StatusCode.Should().Be(HttpStatusCode.OK);

        HttpResponseMessage get = await SendAsync(
            Mod,
            HttpMethod.Get,
            "music/player-buttons",
            null
        );

        get.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonNode button = JsonNode.Parse(await get.Content.ReadAsStringAsync())!["data"]![0]!;
        button["id"]!.GetValue<string>().Should().NotBeNullOrWhiteSpace();
        button["label"]!.GetValue<string>().Should().Be("Thank");
        button["icon"]!.GetValue<string>().Should().Be("heart");
        button["actionType"]!.GetValue<string>().Should().Be("send_message");
        button["parameters"]!["message"]!.GetValue<string>().Should().Be("thanks {{target.name}}");
    }

    [Theory]
    [InlineData("", "heart", "send_message")]
    [InlineData("Label that is far too long to fit", "heart", "send_message")]
    [InlineData("Ok", "Not An Icon", "send_message")]
    [InlineData("Ok", "heart", " ")]
    public async Task Player_buttons_reject_an_invalid_button_and_store_nothing(
        string label,
        string icon,
        string actionType
    )
    {
        HttpResponseMessage put = await SendAsync(
            Owner,
            HttpMethod.Put,
            "music/player-buttons",
            new[]
            {
                new
                {
                    id = "",
                    label,
                    icon,
                    actionType,
                    parameters = new { },
                },
            }
        );
        HttpResponseMessage get = await SendAsync(
            Owner,
            HttpMethod.Get,
            "music/player-buttons",
            null
        );

        put.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        JsonNode.Parse(await get.Content.ReadAsStringAsync())!["data"]!
            .AsArray()
            .Should()
            .BeEmpty();
    }

    [Fact]
    public async Task Player_buttons_reject_more_than_the_maximum()
    {
        var tooMany = Enumerable
            .Range(0, PlayerButtonRules.MaxButtons + 1)
            .Select(i => new
            {
                id = "",
                label = $"B{i}",
                icon = "heart",
                actionType = "send_message",
                parameters = new { },
            })
            .ToArray();

        HttpResponseMessage put = await SendAsync(
            Owner,
            HttpMethod.Put,
            "music/player-buttons",
            tooMany
        );

        put.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task The_now_playing_track_carries_its_uri()
    {
        IMusicService music = Substitute.For<IMusicService>();
        music
            .GetQueueAsync("c", Arg.Any<CancellationToken>())
            .Returns(
                new MusicQueue(
                    new(
                        "Song",
                        "Artist",
                        null,
                        null,
                        1000,
                        0,
                        true,
                        50,
                        null,
                        "spotify",
                        "spotify:track:abc"
                    ),
                    []
                )
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
            .GetConfigAsync("c", Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(new MusicConfigDto(true, "auto", 50, 5, true, true, "everyone"))
            );
        MusicController controller = new(
            music,
            config,
            Substitute.For<ISongRequestPageTokenService>(),
            Substitute.For<IBlockedTrackService>(),
            history,
            Substitute.For<ISongRequestIntakeService>()
        );

        IActionResult result = await controller.GetQueue("c", CancellationToken.None);

        MusicQueueDto dto = result
            .Should()
            .BeOfType<OkObjectResult>()
            .Which.Value.Should()
            .BeOfType<StatusResponseDto<MusicQueueDto>>()
            .Which.Data!;
        dto.NowPlaying!.TrackUri.Should().Be("spotify:track:abc");
    }

    private static async Task SeedAsync(AppDbContext db)
    {
        DateTime now = DateTime.UtcNow;
        db.Users.AddRange(User(Owner, "owner_e"), User(Mod, "mod_e"));
        db.Channels.Add(
            new()
            {
                Id = ChannelId,
                OwnerUserId = Owner,
                Name = "owner_e",
                NameNormalized = "owner_e",
                TwitchChannelId = ChannelId.ToString("N")[^10..],
                ExternalChannelId = ChannelId.ToString("N")[^12..],
                IsOnboarded = true,
            }
        );
        db.ChannelModerators.Add(
            new()
            {
                ChannelId = ChannelId,
                UserId = Mod,
                GrantedAt = now,
            }
        );
        db.ChannelMemberships.Add(
            new()
            {
                BroadcasterId = ChannelId,
                UserId = Mod,
                ManagementRole = ManagementRole.Moderator,
                LevelValue = 10,
                Source = MembershipSource.TwitchBadge,
                GrantedAt = now,
            }
        );
        db.ActionDefinitions.AddRange(
            Definition("pipelines:write", 30),
            Definition("music:config:write", 30),
            Definition("music:queue:read", 10)
        );
        await Task.CompletedTask;
    }

    private static NomNomzBot.Domain.Identity.Entities.ActionDefinition Definition(
        string key,
        int level
    ) =>
        new()
        {
            ActionKey = key,
            Plane = AuthPlane.Management,
            DefaultLevel = level,
            FloorLevel = 10,
            FloorTier = DangerTier.Low,
        };

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
