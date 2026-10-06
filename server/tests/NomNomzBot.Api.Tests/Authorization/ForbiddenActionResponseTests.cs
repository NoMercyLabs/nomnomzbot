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
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.Extensions.DependencyInjection;
using NomNomzBot.Api.Authorization;
using NomNomzBot.Api.Tests.Authentication;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Platform.Persistence;

namespace NomNomzBot.Api.Tests.Authorization;

/// <summary>
/// A forbidden <c>[RequireAction]</c> call answers WHY: the action key, the role the action needs in this
/// channel and the role the caller holds, as role names (never ladder numbers). Any other 403 stays as it was.
/// </summary>
public sealed class ForbiddenActionResponseTests : IAsyncLifetime
{
    private static readonly Guid Owner = Guid.Parse("0192b000-0000-7000-8000-000000000a01");
    private static readonly Guid Mod = Guid.Parse("0192b000-0000-7000-8000-000000000b01");
    private static readonly Guid ChannelId = Guid.Parse("0192b000-0000-7000-8000-000000000a02");

    private ActAsTestHost _host = null!;

    public async Task InitializeAsync()
    {
        _host = await ActAsTestHost.StartAsync(Guid.Parse("0192b000-0000-7000-8000-0000000000ff"));
        await _host.SeedAsync(SeedAsync);
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task A_moderator_writing_a_timer_the_channel_raised_to_editor_is_told_which_action_and_both_roles()
    {
        string token = (await _host.LoginAsync(Mod, ChannelId)).AccessToken;

        using HttpRequestMessage request = new(
            HttpMethod.Post,
            $"/api/v1/channels/{ChannelId}/timers"
        );
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new { });
        HttpResponseMessage response = await _host.Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        JsonNode body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        body["code"]!.GetValue<string>().Should().Be("FORBIDDEN_ACTION");
        body["status"]!.GetValue<int>().Should().Be(403);
        body["action"]!.GetValue<string>().Should().Be("timers:write");
        body["requiredRole"]!.GetValue<string>().Should().Be("Editor");
        body["heldRole"]!.GetValue<string>().Should().Be("Moderator");
    }

    [Fact]
    public async Task A_forbid_that_is_not_an_action_requirement_keeps_the_default_empty_403()
    {
        ForbiddenActionResultHandler handler = new();
        ServiceCollection services = new();
        services.AddLogging();
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        DefaultHttpContext context = new()
        {
            RequestServices = services.BuildServiceProvider(),
            Response = { Body = new MemoryStream() },
        };
        PolicyAuthorizationResult forbid = PolicyAuthorizationResult.Forbid(
            AuthorizationFailure.Failed([new OtherRequirement()])
        );
        bool nextCalled = false;

        await handler.HandleAsync(
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            context,
            new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build(),
            forbid
        );

        nextCalled.Should().BeFalse();
        context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        context.Response.Body.Length.Should().Be(0);
    }

    private sealed class OtherRequirement : IAuthorizationRequirement;

    private static async Task SeedAsync(AppDbContext db)
    {
        DateTime now = DateTime.UtcNow;
        db.Users.AddRange(User(Owner, "owner_one"), User(Mod, "mod_one"));
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
        ActionDefinition timersWrite = new()
        {
            ActionKey = "timers:write",
            Plane = AuthPlane.Management,
            DefaultLevel = 10,
            FloorLevel = 10,
            FloorTier = DangerTier.Low,
        };
        db.ActionDefinitions.Add(timersWrite);
        // The broadcaster raised timer writes above the Moderator default.
        db.ChannelActionOverrides.Add(
            new()
            {
                BroadcasterId = ChannelId,
                ActionDefinitionId = timersWrite.Id,
                OverrideLevel = 30,
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
