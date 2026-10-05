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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Contracts.Tts;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.Community.Events;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Community.EventHandlers;
using NomNomzBot.Infrastructure.Content.Commands;
using NomNomzBot.Infrastructure.Platform.Eventing;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Community.EventHandlers;

/// <summary>
/// A finished poll posts its winner in chat, like the old bot (S-PAR-POLL-END). Runs the real handler, the
/// real executor and the seeded platform default, and reads the line that reaches chat.
/// </summary>
public sealed class PollEndedChatLineTests
{
    private const string PollEnd = "channel.poll.end";
    private static readonly Guid Channel = Guid.Parse("0199f400-0000-7000-8000-00000000d301");

    private sealed record Harness(AuthDbContext Db, PollEndedHandler Handler, IChatProvider Chat);

    /// <summary>A resolver that only swaps <c>{name}</c> for its variable, which is all the poll line needs.</summary>
    private static ITemplateResolver SubstitutingResolver()
    {
        ITemplateResolver resolver = Substitute.For<ITemplateResolver>();
        resolver
            .ResolveAsync(
                Arg.Any<string>(),
                Arg.Any<IDictionary<string, string>>(),
                Arg.Any<Guid?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                string text = call.ArgAt<string>(0);
                foreach (
                    KeyValuePair<string, string> pair in call.ArgAt<IDictionary<string, string>>(1)
                )
                    text = text.Replace("{" + pair.Key + "}", pair.Value, StringComparison.Ordinal);
                return Task.FromResult(text);
            });
        return resolver;
    }

    private static async Task<Harness> BuildAsync()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Channels.Add(
            new()
            {
                Id = Channel,
                OwnerUserId = Channel,
                Provider = AuthEnums.Platform.Twitch,
                ExternalChannelId = "pollchannel-ext",
                Name = "pollchannel",
                NameNormalized = "pollchannel",
                Status = AuthEnums.ChannelStatus.Active,
                Personality = PersonalityTone.Informative,
            }
        );
        await db.SaveChangesAsync();
        await new PlatformEventResponseDefaultsSeeder(db).SeedAsync();
        await new EventResponseDefaultsSeeder(db).SeedAsync();

        IChatProvider chat = Substitute.For<IChatProvider>();
        EventResponseExecutor executor = new(
            db,
            Substitute.For<IPipelineEngine>(),
            SubstitutingResolver(),
            chat,
            Substitute.For<IEventResponseOverlayNotifier>(),
            Substitute.For<ITtsDispatchService>(),
            NullLogger<EventResponseExecutor>.Instance
        );
        ServiceProvider provider = new ServiceCollection()
            .AddSingleton<IApplicationDbContext>(db)
            .AddSingleton<IEventResponseExecutor>(executor)
            .BuildServiceProvider();
        PollEndedHandler handler = new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Substitute.For<IPipelineEngine>(),
            NullLogger<PollEndedHandler>.Instance
        );
        return new(db, handler, chat);
    }

    private static PollEndedEvent Ended(string status, params PollChoice[] choices) =>
        new()
        {
            BroadcasterId = Channel,
            OccurredAt = DateTimeOffset.UtcNow,
            PollId = "poll-1",
            Title = "Which game next?",
            Status = status,
            Choices = choices,
        };

    private static List<string> Sent(Harness h) =>
        [
            .. h
                .Chat.ReceivedCalls()
                .Where(c => c.GetMethodInfo().Name == nameof(IChatProvider.SendMessageAsync))
                .Select(c => (string)c.GetArguments()[1]!),
        ];

    [Fact]
    public async Task A_completed_poll_posts_the_winner_with_its_percentage_and_every_result()
    {
        Harness h = await BuildAsync();

        await h.Handler.HandleAsync(
            Ended(
                "completed",
                new PollChoice("c1", "Elden Ring", 14, 3),
                new PollChoice("c2", "Hades II", 31, 8)
            )
        );

        Sent(h)
            .Should()
            .Equal(
                "\U0001F4CA Poll ended: \"Which game next?\" — Winner: Hades II (68%) | "
                    + "Elden Ring: 14 (31%) | Hades II: 31 (68%)"
            );
    }

    [Fact]
    public async Task A_completed_poll_with_no_votes_names_the_winner_without_a_percentage()
    {
        Harness h = await BuildAsync();

        await h.Handler.HandleAsync(
            Ended(
                "completed",
                new PollChoice("c1", "Elden Ring", 0, 0),
                new PollChoice("c2", "Hades II", 0, 0)
            )
        );

        Sent(h)
            .Should()
            .Equal(
                "\U0001F4CA Poll ended: \"Which game next?\" — Winner: Elden Ring | "
                    + "Elden Ring: 0 | Hades II: 0"
            );
    }

    [Theory]
    [InlineData("terminated")]
    [InlineData("archived")]
    public async Task A_poll_that_was_cut_short_posts_nothing_but_is_still_logged(string status)
    {
        Harness h = await BuildAsync();

        await h.Handler.HandleAsync(Ended(status, new PollChoice("c1", "Elden Ring", 5, 0)));

        Sent(h).Should().BeEmpty();
        (await h.Db.ChannelEvents.CountAsync(e => e.Type == PollEnd))
            .Should()
            .Be(1, "the poll is still in the activity feed");
    }

    [Fact]
    public async Task Every_channel_gets_the_poll_end_response_on_by_default()
    {
        Harness h = await BuildAsync();

        PlatformEventResponseDefault platform =
            await h.Db.PlatformEventResponseDefaults.SingleAsync(d => d.EventType == PollEnd);
        EventResponse row = await h.Db.EventResponses.SingleAsync(r =>
            r.BroadcasterId == Channel && r.EventType == PollEnd
        );

        platform.IsEnabled.Should().BeTrue();
        platform.Message.Should().BeNull("the line comes from the tone catalogue");
        row.FollowsPlatformDefault.Should().BeTrue();
    }
}
