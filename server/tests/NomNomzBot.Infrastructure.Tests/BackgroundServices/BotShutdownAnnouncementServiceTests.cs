// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Collections.Concurrent;
using System.Diagnostics;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Commands.Builtin.Personality;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.BackgroundServices;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.BackgroundServices;

/// <summary>
/// The "going offline" chat line: posted to every live channel the bot serves when it stops with no
/// successor, in that channel's personality — and NOT posted on a blue/green handover, and never allowed to
/// hold shutdown open when a send hangs.
/// </summary>
public sealed class BotShutdownAnnouncementServiceTests
{
    private readonly ConcurrentBag<(Guid Channel, string Message)> _sent = [];
    private readonly ConcurrentBag<BuiltinResponseRequest> _composed = [];

    [Fact]
    public async Task Stop_without_a_successor_posts_the_tone_line_to_every_live_channel_only()
    {
        Guid liveSassy = Guid.NewGuid();
        Guid liveDefault = Guid.NewGuid();
        Guid offline = Guid.NewGuid();
        Guid suspended = Guid.NewGuid();
        BotShutdownAnnouncementService service = Build(
            successorWaiting: false,
            chat: RecordingChat(),
            Channel(liveSassy, live: true, PersonalityTone.Sassy),
            Channel(liveDefault, live: true, PersonalityTone.Informative),
            Channel(offline, live: false, PersonalityTone.Informative),
            Channel(
                suspended,
                live: true,
                PersonalityTone.Informative,
                AuthEnums.ChannelStatus.Suspended
            )
        );

        await service.StopAsync(CancellationToken.None);

        _sent.Select(s => s.Channel).Should().BeEquivalentTo([liveSassy, liveDefault]);
        _sent
            .Single(s => s.Channel == liveSassy)
            .Message.Should()
            .BeOneOf(
                ToneTemplateCatalog.Get(
                    PersonalityTone.Sassy,
                    BuiltinResponseSlots.BotStatus.Key,
                    BuiltinResponseSlots.BotStatus.GoingOffline
                )
            );
        _sent
            .Single(s => s.Channel == liveDefault)
            .Message.Should()
            .Be("Restarting for an update — back in a moment.");
        _composed
            .Should()
            .OnlyContain(r =>
                r.BuiltinKey == BuiltinResponseSlots.BotStatus.Key
                && r.Slot == BuiltinResponseSlots.BotStatus.GoingOffline
            );
    }

    [Fact]
    public async Task A_blue_green_handover_posts_nothing()
    {
        BotShutdownAnnouncementService service = Build(
            successorWaiting: true,
            chat: RecordingChat(),
            Channel(Guid.NewGuid(), live: true, PersonalityTone.Informative)
        );

        await service.StopAsync(CancellationToken.None);

        _sent.Should().BeEmpty();
        _composed.Should().BeEmpty();
    }

    [Fact]
    public async Task A_hung_send_never_holds_shutdown_past_the_budget()
    {
        Guid hung = Guid.NewGuid();
        Guid healthy = Guid.NewGuid();
        IChatProvider chat = Substitute.For<IChatProvider>();
        chat.SendMessageAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                Guid channel = call.ArgAt<Guid>(0);
                if (channel == hung)
                    return new TaskCompletionSource<bool>().Task; // never completes, ignores the token
                _sent.Add((channel, call.ArgAt<string>(1)));
                return Task.FromResult(true);
            });
        BotShutdownAnnouncementService service = Build(
            successorWaiting: false,
            chat,
            Channel(hung, live: true, PersonalityTone.Informative),
            Channel(healthy, live: true, PersonalityTone.Informative)
        );

        Stopwatch elapsed = Stopwatch.StartNew();
        await service.StopAsync(CancellationToken.None);
        elapsed.Stop();

        elapsed.Elapsed.Should().BeLessThan(BotShutdownAnnouncementService.SendBudget * 2);
        _sent.Select(s => s.Channel).Should().Equal(healthy);
    }

    private IChatProvider RecordingChat()
    {
        IChatProvider chat = Substitute.For<IChatProvider>();
        chat.SendMessageAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                _sent.Add((call.ArgAt<Guid>(0), call.ArgAt<string>(1)));
                return Task.FromResult(true);
            });
        return chat;
    }

    private BotShutdownAnnouncementService Build(
        bool successorWaiting,
        IChatProvider chat,
        params Channel[] channels
    )
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Channels.AddRange(channels);
        db.SaveChanges();

        IBuiltinResponseComposer composer = Substitute.For<IBuiltinResponseComposer>();
        composer
            .ComposeAsync(Arg.Any<BuiltinResponseRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                BuiltinResponseRequest request = call.ArgAt<BuiltinResponseRequest>(0);
                _composed.Add(request);
                return Task.FromResult(
                    ToneTemplateCatalog.Pick(request.Personality, request.BuiltinKey, request.Slot)
                        ?? request.NeutralFallback
                );
            });

        ServiceProvider provider = new ServiceCollection()
            .AddSingleton<IApplicationDbContext>(db)
            .AddSingleton(composer)
            .AddSingleton(chat)
            .BuildServiceProvider();

        IActiveInstanceGate gate = Substitute.For<IActiveInstanceGate>();
        gate.HasWaitingSuccessorAsync(Arg.Any<CancellationToken>()).Returns(successorWaiting);

        return new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            gate,
            NullLogger<BotShutdownAnnouncementService>.Instance
        );
    }

    private static Channel Channel(
        Guid id,
        bool live,
        string personality,
        string status = AuthEnums.ChannelStatus.Active
    ) =>
        new()
        {
            Id = id,
            OwnerUserId = Guid.NewGuid(),
            Name = $"chan{id:N}",
            NameNormalized = $"chan{id:N}",
            Enabled = true,
            IsOnboarded = true,
            IsLive = live,
            Status = status,
            Personality = personality,
        };
}
