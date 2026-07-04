// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Infrastructure.Chat.EventHandlers;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace NomNomzBot.Infrastructure.Tests.Chat;

/// <summary>
/// Proves the GAP E3-1 wiring: <see cref="PronounHydrationHandler"/> is the chat-ingest caller
/// <c>IPronounResolutionService.ResolveAndApplyAsync</c> never had. One chat message resolves the viewer's
/// internal <c>User</c> row and calls the pronoun service exactly once; a throwing provider/service must never
/// surface out of chat ingest — command execution and persistence run as separate handlers on the same event
/// and must never see this handler's failure.
/// </summary>
public sealed class PronounHydrationHandlerTests
{
    [Fact]
    public async Task Resolves_the_viewer_then_calls_pronoun_resolution_exactly_once()
    {
        IUserService userService = Substitute.For<IUserService>();
        IPronounResolutionService pronouns = Substitute.For<IPronounResolutionService>();
        Guid viewerUserId = Guid.CreateVersion7();
        userService
            .GetOrCreateAsync("u1", "stoney_eagle", "Stoney", Arg.Any<CancellationToken>())
            .Returns(Result.Success(Dto(viewerUserId)));

        PronounHydrationHandler handler = new(
            userService,
            pronouns,
            NullLogger<PronounHydrationHandler>.Instance
        );

        await handler.HandleAsync(Event());

        await pronouns
            .Received(1)
            .ResolveAndApplyAsync(viewerUserId, "stoney_eagle", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_throwing_pronoun_service_does_not_propagate_chat_ingest_survives()
    {
        IUserService userService = Substitute.For<IUserService>();
        IPronounResolutionService pronouns = Substitute.For<IPronounResolutionService>();
        Guid viewerUserId = Guid.CreateVersion7();
        userService
            .GetOrCreateAsync("u1", "stoney_eagle", "Stoney", Arg.Any<CancellationToken>())
            .Returns(Result.Success(Dto(viewerUserId)));
        pronouns
            .ResolveAndApplyAsync(viewerUserId, "stoney_eagle", Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("alejo.io is down"));

        PronounHydrationHandler handler = new(
            userService,
            pronouns,
            NullLogger<PronounHydrationHandler>.Instance
        );

        Func<Task> act = () => handler.HandleAsync(Event());

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task A_failed_get_or_create_skips_pronoun_resolution_without_throwing()
    {
        IUserService userService = Substitute.For<IUserService>();
        IPronounResolutionService pronouns = Substitute.For<IPronounResolutionService>();
        userService
            .GetOrCreateAsync("u1", "stoney_eagle", "Stoney", Arg.Any<CancellationToken>())
            .Returns(Result.Failure<UserDto>("boom"));

        PronounHydrationHandler handler = new(
            userService,
            pronouns,
            NullLogger<PronounHydrationHandler>.Instance
        );

        await handler.HandleAsync(Event());

        await pronouns
            .DidNotReceive()
            .ResolveAndApplyAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Platform_sentinel_channel_never_calls_user_service()
    {
        IUserService userService = Substitute.For<IUserService>();
        IPronounResolutionService pronouns = Substitute.For<IPronounResolutionService>();
        PronounHydrationHandler handler = new(
            userService,
            pronouns,
            NullLogger<PronounHydrationHandler>.Instance
        );

        await handler.HandleAsync(Event(broadcasterId: Guid.Empty));

        await userService
            .DidNotReceive()
            .GetOrCreateAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
    }

    private static UserDto Dto(Guid id) =>
        new(
            Id: id.ToString(),
            Username: "stoney_eagle",
            DisplayName: "Stoney",
            ProfileImageUrl: null,
            Email: null,
            CreatedAt: DateTime.UtcNow,
            LastLoginAt: DateTime.UtcNow
        );

    private static ChatMessageReceivedEvent Event(Guid? broadcasterId = null) =>
        new()
        {
            BroadcasterId = broadcasterId ?? Guid.CreateVersion7(),
            MessageId = "m1",
            TwitchBroadcasterId = "123",
            UserId = "u1",
            UserDisplayName = "Stoney",
            UserLogin = "stoney_eagle",
            Message = "hello",
            Fragments = [],
            Badges = [],
            IsSubscriber = false,
            IsVip = false,
            IsModerator = false,
            IsBroadcaster = false,
        };
}
