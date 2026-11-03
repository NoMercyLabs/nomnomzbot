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

using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using NomNomzBot.Api.Authorization;
using NomNomzBot.Api.Controllers.V1;
using NomNomzBot.Api.Models;
using NomNomzBot.Api.RateLimiting;
using NomNomzBot.Api.Tests.Authentication;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Alerts.Services;
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Platform.Persistence;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Authorization;

/// <summary>
/// POST event-responses/{eventType}/test is gated like editing an event response and rate limited as expensive
/// writes. A viewer is refused with 403 before anything is fired; the action answers the service result.
/// </summary>
public sealed class EventResponseTestEndpointTests : IAsyncLifetime
{
    private static readonly Guid Owner = Guid.Parse("0192b100-0000-7000-8000-000000000a01");
    private static readonly Guid Mod = Guid.Parse("0192b100-0000-7000-8000-000000000b01");
    private static readonly Guid Viewer = Guid.Parse("0192b100-0000-7000-8000-000000000c01");
    private static readonly Guid ChannelId = Guid.Parse("0192b100-0000-7000-8000-000000000a02");

    private ActAsTestHost _host = null!;

    public async Task InitializeAsync()
    {
        _host = await ActAsTestHost.StartAsync(Guid.Parse("0192b100-0000-7000-8000-0000000000ff"));
        await _host.SeedAsync(SeedAsync);
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private async Task<HttpResponseMessage> PostTestAsync(Guid caller)
    {
        string token = (await _host.LoginAsync(caller, ChannelId)).AccessToken;
        using HttpRequestMessage request = new(
            HttpMethod.Post,
            $"/api/v1/channels/{ChannelId}/event-responses/channel.raid/test"
        );
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _host.Client.SendAsync(request);
    }

    [Fact]
    public void The_route_needs_the_event_response_write_key_and_the_expensive_write_limit()
    {
        MethodInfo action = typeof(EventResponsesController).GetMethod(
            nameof(EventResponsesController.TestEventResponse)
        )!;

        action
            .GetCustomAttribute<RequireActionAttribute>()!
            .ActionKey.Should()
            .Be("eventresponses:write");
        action
            .GetCustomAttribute<EnableRateLimitingAttribute>()!
            .PolicyName.Should()
            .Be(RateLimitPolicyNames.WriteExpensive);
    }

    [Fact]
    public async Task A_viewer_is_refused_with_403_and_nothing_is_fired()
    {
        HttpResponseMessage response = await PostTestAsync(Viewer);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        JsonNode body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        body["action"]!.GetValue<string>().Should().Be("eventresponses:write");
    }

    [Fact]
    public async Task A_fire_answers_200_with_the_result_as_the_data_the_service_returned()
    {
        EventResponseTestResult fired = new(
            "channel.raid",
            new Dictionary<string, string> { ["viewers"] = "25" },
            "Welcome 25",
            null,
            [],
            [],
            1,
            0,
            0,
            ["no_overlay_source"],
            []
        );
        IEventResponseTestService service = Substitute.For<IEventResponseTestService>();
        service
            .FireAsync(ChannelId, "channel.raid", Mod, Arg.Any<CancellationToken>())
            .Returns(Result.Success(fired));

        IActionResult answer = await ControllerFor(service, Mod)
            .TestEventResponse(ChannelId.ToString(), "channel.raid", CancellationToken.None);

        StatusResponseDto<EventResponseTestResult> body = answer
            .Should()
            .BeOfType<OkObjectResult>()
            .Which.Value.Should()
            .BeOfType<StatusResponseDto<EventResponseTestResult>>()
            .Subject;
        body.Data.Should().Be(fired);
    }

    [Fact]
    public async Task An_event_type_without_a_response_answers_404()
    {
        IEventResponseTestService service = Substitute.For<IEventResponseTestService>();
        service
            .FireAsync(ChannelId, "channel.nope", Mod, Arg.Any<CancellationToken>())
            .Returns(Result.Failure<EventResponseTestResult>("unknown", "NOT_FOUND"));

        IActionResult answer = await ControllerFor(service, Mod)
            .TestEventResponse(ChannelId.ToString(), "channel.nope", CancellationToken.None);

        answer.Should().BeAssignableTo<ObjectResult>().Which.StatusCode.Should().Be(404);
    }

    private static EventResponsesController ControllerFor(
        IEventResponseTestService service,
        Guid caller
    )
    {
        ICurrentUserService currentUser = Substitute.For<ICurrentUserService>();
        currentUser.UserId.Returns(caller.ToString());
        return new(
            Substitute.For<IEventResponseService>(),
            Substitute.For<IWidgetService>(),
            Substitute.For<IAlertQueueService>(),
            service,
            currentUser
        );
    }

    private static async Task SeedAsync(AppDbContext db)
    {
        DateTime now = DateTime.UtcNow;
        db.Users.AddRange(
            User(Owner, "owner_one"),
            User(Mod, "mod_one"),
            User(Viewer, "viewer_one")
        );
        db.Channels.Add(
            new()
            {
                Id = ChannelId,
                OwnerUserId = Owner,
                Name = "owner_one",
                NameNormalized = "owner_one",
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
        db.ActionDefinitions.Add(
            new()
            {
                ActionKey = "eventresponses:write",
                Plane = AuthPlane.Management,
                DefaultLevel = 10,
                FloorLevel = 10,
                FloorTier = DangerTier.Low,
            }
        );
        await Task.CompletedTask;
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
