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
using NomNomzBot.Api.Tests.Authentication;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Platform.Persistence;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// The editor preview may run exactly one action for real: <c>tts_synthesize</c>, on the caller's own channel.
/// Through the real auth pipeline: the allowed action returns the variables it set, every other action type is
/// refused before it reaches the executor, and a channel the caller does not own never reaches it either.
/// </summary>
public sealed class WidgetPreviewActionTests : IAsyncLifetime
{
    private static readonly Guid Owner = Guid.Parse("0192b000-0000-7000-8000-000000000c01");
    private static readonly Guid Other = Guid.Parse("0192b000-0000-7000-8000-000000000d01");
    private static readonly Guid OwnChannel = Guid.Parse("0192b000-0000-7000-8000-000000000c02");
    private static readonly Guid OtherChannel = Guid.Parse("0192b000-0000-7000-8000-000000000d02");

    private ActAsTestHost _host = null!;

    public async Task InitializeAsync()
    {
        _host = await ActAsTestHost.StartAsync(Guid.Parse("0192b000-0000-7000-8000-0000000000ff"));
        await _host.SeedAsync(SeedAsync);
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private async Task<HttpResponseMessage> PostAsync(
        Guid channel,
        string actionType,
        Guid loginChannel
    )
    {
        string token = (await _host.LoginAsync(Owner, loginChannel)).AccessToken;
        using HttpRequestMessage request = new(
            HttpMethod.Post,
            $"/api/v1/channels/{channel}/widgets/preview-action"
        );
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(
            new { actionType, variables = new Dictionary<string, string> { ["message"] = "hi" } }
        );
        return await _host.Client.SendAsync(request);
    }

    [Fact]
    public async Task Tts_synthesize_on_the_callers_own_channel_returns_the_variables_it_set()
    {
        _host
            .OwnerActions.RunAsync(Arg.Any<OwnerActionRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new WidgetActionOutcome(
                        true,
                        "tts_synthesize:voice-1",
                        null,
                        new Dictionary<string, string>
                        {
                            ["tts.audioUrl"] = "https://cdn.example/tts-1.mp3",
                            ["tts.durationMs"] = "1234",
                        }
                    )
                )
            );

        HttpResponseMessage response = await PostAsync(OwnChannel, "tts_synthesize", OwnChannel);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonNode data = JsonNode.Parse(await response.Content.ReadAsStringAsync())!["data"]!;
        data["success"]!.GetValue<bool>().Should().BeTrue();
        data["variables"]!["tts.audioUrl"]!
            .GetValue<string>()
            .Should()
            .Be("https://cdn.example/tts-1.mp3");
        data["variables"]!["tts.durationMs"]!.GetValue<string>().Should().Be("1234");
        await _host
            .OwnerActions.Received(1)
            .RunAsync(
                Arg.Is<OwnerActionRequest>(r =>
                    r.BroadcasterId == OwnChannel
                    && r.ActionType == "tts_synthesize"
                    && r.Variables!["message"] == "hi"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Theory]
    [InlineData("send_message")]
    [InlineData("redemption_refund")]
    public async Task Any_other_action_type_is_refused_and_never_reaches_the_executor(
        string actionType
    )
    {
        HttpResponseMessage response = await PostAsync(OwnChannel, actionType, OwnChannel);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        JsonNode body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        body["code"]!.GetValue<string>().Should().Be("ACTION_NOT_ALLOWED");
        await _host
            .OwnerActions.DidNotReceiveWithAnyArgs()
            .RunAsync(default!, CancellationToken.None);
    }

    [Fact]
    public async Task A_channel_the_caller_does_not_own_is_refused_and_never_reaches_the_executor()
    {
        HttpResponseMessage response = await PostAsync(OtherChannel, "tts_synthesize", OwnChannel);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await _host
            .OwnerActions.DidNotReceiveWithAnyArgs()
            .RunAsync(default!, CancellationToken.None);
    }

    private static async Task SeedAsync(AppDbContext db)
    {
        db.Users.AddRange(User(Owner, "owner_c"), User(Other, "other_d"));
        db.Channels.AddRange(
            Channel(OwnChannel, Owner, "owner_c"),
            Channel(OtherChannel, Other, "other_d")
        );
        db.ActionDefinitions.Add(
            new()
            {
                ActionKey = "widget:write",
                Plane = AuthPlane.Management,
                DefaultLevel = 30,
                FloorLevel = 10,
                FloorTier = DangerTier.Low,
            }
        );
        await Task.CompletedTask;
    }

    private static Channel Channel(Guid id, Guid owner, string name) =>
        new()
        {
            Id = id,
            OwnerUserId = owner,
            Name = name,
            NameNormalized = name,
            TwitchChannelId = id.ToString("N")[^10..],
            ExternalChannelId = id.ToString("N")[^12..],
            IsOnboarded = true,
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
