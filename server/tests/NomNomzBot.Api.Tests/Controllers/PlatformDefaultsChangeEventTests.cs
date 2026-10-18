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
using NomNomzBot.Api.Controllers.V1;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.PlatformDefaults.Dtos;
using NomNomzBot.Application.PlatformDefaults.Services;
using NomNomzBot.Domain.Platform.Events;
using NomNomzBot.Domain.Platform.Interfaces;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// The platform defaults pages reload live: every successful save on any of the four default sets publishes
/// one <see cref="PlatformDefaultsChangedEvent"/> naming the set (and the key when the set has keys); a refused
/// save publishes nothing.
/// </summary>
public sealed class PlatformDefaultsChangeEventTests
{
    private static ICurrentUserService Caller()
    {
        ICurrentUserService currentUser = Substitute.For<ICurrentUserService>();
        currentUser.UserId.Returns(Guid.NewGuid().ToString());
        return currentUser;
    }

    private static PlatformDefaultsChangedEvent Published(IEventBus bus) =>
        bus.ReceivedCalls()
            .Select(call => call.GetArguments()[0])
            .OfType<PlatformDefaultsChangedEvent>()
            .Single();

    [Fact]
    public async Task Saving_an_action_default_publishes_the_actions_set_with_the_action_key()
    {
        IActionDefaultsAdminService service = Substitute.For<IActionDefaultsAdminService>();
        IEventBus bus = Substitute.For<IEventBus>();
        service
            .SetAsync(
                "chat.timeout",
                Arg.Any<SetActionDefaultRequest>(),
                Arg.Any<Guid>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success<ActionDefaultDto>(null!));
        ActionDefaultsAdminController controller = new(service, Caller(), bus);

        await controller.Set("chat.timeout", new SetActionDefaultRequest(2, 0, false), default);

        PlatformDefaultsChangedEvent published = Published(bus);
        published.Set.Should().Be("actions");
        published.Key.Should().Be("chat.timeout");
        published.BroadcasterId.Should().Be(Guid.Empty);
    }

    [Fact]
    public async Task A_refused_action_default_save_publishes_nothing()
    {
        IActionDefaultsAdminService service = Substitute.For<IActionDefaultsAdminService>();
        IEventBus bus = Substitute.For<IEventBus>();
        service
            .SetAsync(
                Arg.Any<string>(),
                Arg.Any<SetActionDefaultRequest>(),
                Arg.Any<Guid>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Failure<ActionDefaultDto>("stale", "STALE_BLAST_RADIUS"));
        ActionDefaultsAdminController controller = new(service, Caller(), bus);

        await controller.Set("chat.timeout", new SetActionDefaultRequest(2, 0, false), default);

        bus.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Saving_a_builtin_reply_default_publishes_the_builtin_replies_set_with_key_and_slot()
    {
        IBuiltinReplyDefaultsAdminService service =
            Substitute.For<IBuiltinReplyDefaultsAdminService>();
        IEventBus bus = Substitute.For<IEventBus>();
        service
            .SetAsync(
                "hug",
                "success",
                Arg.Any<SetBuiltinReplyDefaultRequest>(),
                Arg.Any<Guid>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success<BuiltinReplyDefaultDto>(null!));
        BuiltinReplyDefaultsAdminController controller = new(service, Caller(), bus);

        await controller.Set("hug", "success", new SetBuiltinReplyDefaultRequest("hi", 0), default);

        PlatformDefaultsChangedEvent published = Published(bus);
        published.Set.Should().Be("builtin-replies");
        published.Key.Should().Be("hug/success");
    }

    [Fact]
    public async Task Saving_an_event_response_default_publishes_the_event_responses_set_with_the_event_type()
    {
        IEventResponseDefaultsAdminService service =
            Substitute.For<IEventResponseDefaultsAdminService>();
        IEventBus bus = Substitute.For<IEventBus>();
        service
            .SetAsync(
                "channel.follow",
                Arg.Any<SetEventResponseDefaultRequest>(),
                Arg.Any<Guid>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success<EventResponseDefaultDto>(null!));
        EventResponseDefaultsAdminController controller = new(service, Caller(), bus);

        await controller.Set(
            "channel.follow",
            new SetEventResponseDefaultRequest(true, "hi", 0),
            default
        );

        PlatformDefaultsChangedEvent published = Published(bus);
        published.Set.Should().Be("event-responses");
        published.Key.Should().Be("channel.follow");
    }

    [Fact]
    public async Task Saving_the_tts_voice_default_publishes_the_tts_voice_set_without_a_key()
    {
        ITtsVoiceDefaultsAdminService service = Substitute.For<ITtsVoiceDefaultsAdminService>();
        IEventBus bus = Substitute.For<IEventBus>();
        service
            .SetAsync(
                Arg.Any<SetTtsVoiceDefaultRequest>(),
                Arg.Any<Guid>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success<TtsVoiceDefaultDto>(null!));
        TtsVoiceDefaultsAdminController controller = new(service, Caller(), bus);

        await controller.Set(new SetTtsVoiceDefaultRequest("voice-1", 0), default);

        PlatformDefaultsChangedEvent published = Published(bus);
        published.Set.Should().Be("tts-voice");
        published.Key.Should().BeNull();
    }
}
