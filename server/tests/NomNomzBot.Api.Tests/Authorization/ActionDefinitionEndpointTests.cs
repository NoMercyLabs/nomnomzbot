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
using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NomNomzBot.Api.Authorization;
using NomNomzBot.Api.Controllers;
using NomNomzBot.Api.Tests.Authentication;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Moderation.Services;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Infrastructure.Content.Identity;
using NomNomzBot.Infrastructure.Moderation;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Authorization;

public sealed class ActionDefinitionEndpointTests : IAsyncLifetime
{
    private static readonly Guid Owner = Guid.Parse("0192b000-0000-7000-8000-000000000c01");
    private static readonly Guid Viewer = Guid.Parse("0192b000-0000-7000-8000-000000000c02");
    private static readonly Guid Subject = Guid.Parse("0192b000-0000-7000-8000-000000000c03");
    private static readonly Guid ChannelId = Guid.Parse("0192b000-0000-7000-8000-000000000c04");

    private ActAsTestHost _host = null!;

    public async Task InitializeAsync()
    {
        _host = await ActAsTestHost.StartAsync(
            Guid.Empty,
            services =>
            {
                services.AddScoped<IModerationService, ModerationService>();
                services.AddScoped<IModerationHistoryService, ModerationHistoryService>();
                services.AddSingleton(Substitute.For<IOperatorNetworkBanService>());
                services.AddSingleton(Substitute.For<IViewerReportService>());
                services.AddSingleton(Substitute.For<IModerationQueueService>());
                services.AddSingleton(Substitute.For<ISharedBanService>());
                services.AddSingleton(Substitute.For<INetworkNukeService>());
                services.AddSingleton(Substitute.For<IModerationEscalationService>());
                services.AddSingleton(Substitute.For<IMassBanConsentService>());
                services.AddSingleton(Substitute.For<ITwitchChatApi>());
                services.AddSingleton(Substitute.For<ITwitchUsersApi>());
                services.AddSingleton(Substitute.For<IShoutoutQueue>());
                services.AddSingleton(Substitute.For<ITwitchModerationApi>());
                services.AddSingleton(Substitute.For<ITemplateHelperValidator>());
                services.AddSingleton(Substitute.For<IShoutoutSuggestionService>());
            }
        );
        await _host.SeedAsync(async db =>
        {
            db.Users.AddRange(
                User(Owner, "owner"),
                User(Viewer, "viewer"),
                User(Subject, "subject")
            );
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

    [Fact]
    public async Task Every_controller_action_key_exists_in_the_seeded_catalogue()
    {
        HashSet<string> required = typeof(BaseController)
            .Assembly.GetTypes()
            .Where(type =>
                type is { IsAbstract: false, IsPublic: true }
                && typeof(ControllerBase).IsAssignableFrom(type)
            )
            .SelectMany(type =>
                type.GetMethods(
                        BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly
                    )
                    .Where(method =>
                        !method.IsSpecialName
                        && method.GetCustomAttribute<NonActionAttribute>() is null
                        && method.GetCustomAttributes<HttpMethodAttribute>().Any()
                    )
                    .SelectMany(method =>
                        type.GetCustomAttributes<RequireActionAttribute>(true)
                            .Concat(method.GetCustomAttributes<RequireActionAttribute>(true))
                    )
            )
            .Select(attribute => attribute.ActionKey)
            .ToHashSet(StringComparer.Ordinal);
        HashSet<string> seeded = await _host.ReadAsync(async db =>
            (await db.ActionDefinitions.Select(action => action.ActionKey).ToListAsync()).ToHashSet(
                StringComparer.Ordinal
            )
        );

        required
            .Except(seeded)
            .Should()
            .BeEmpty("every controller action must have a seeded Gate-2 key");
    }

    [Fact]
    public async Task Broadcaster_can_add_a_history_note_but_viewer_cannot()
    {
        string route = $"/api/v1/channels/{ChannelId}/moderation/history/{Subject}/notes";
        HttpResponseMessage ownerResponse = await PostNoteAsync(
            Owner,
            route,
            "context for next shift"
        );
        ownerResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        HttpResponseMessage viewerResponse = await PostNoteAsync(
            Viewer,
            route,
            "viewer should not write"
        );
        viewerResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        List<string> notes = await _host.ReadAsync(async db =>
            await db
                .Records.Where(record =>
                    record.BroadcasterId == ChannelId && record.RecordType == "user_note"
                )
                .Select(record => record.Data)
                .ToListAsync()
        );
        notes.Should().ContainSingle().Which.Should().Contain("context for next shift");
        notes.Single().Should().NotContain("viewer should not write");
    }

    private async Task<HttpResponseMessage> PostNoteAsync(Guid caller, string route, string note)
    {
        string token = (await _host.LoginAsync(caller, ChannelId)).AccessToken;
        using HttpRequestMessage request = new(HttpMethod.Post, route);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new { Note = note });
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
