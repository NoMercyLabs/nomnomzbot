// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.PickLists.Dtos;
using NomNomzBot.Application.PickLists.Services;
using NomNomzBot.Application.Quotes.Dtos;
using NomNomzBot.Application.Quotes.Services;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Platform.Templating;
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.Chatter;
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.Include;
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.Quotes;
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.Variables;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating.Helpers.Composing;

/// <summary>
/// The catalog section 5 data of the composing rows (chatters, quotes, pick list, commands) served by fakes at the
/// service interfaces. Channel A holds the fixture; channel B holds different quotes, commands and chatters, so a
/// test can prove the two never mix. The chatter provider draws from <c>new Random(1)</c>.
/// </summary>
internal sealed class ComposingHelperFixture : IAsyncDisposable
{
    public static readonly Guid ChannelA = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    public static readonly Guid ChannelB = Guid.Parse("00000000-0000-0000-0000-00000000000b");

    private static readonly DateTimeOffset Now = new(2026, 10, 26, 14, 30, 0, TimeSpan.Zero);

    /// <summary>Quote 12 is the catalog quote; 5 has no author, game or date; 7 was said at 23:30Z.</summary>
    private static readonly Dictionary<Guid, Dictionary<int, QuoteDto>> Quotes = new()
    {
        [ChannelA] = new()
        {
            [12] = Quote(12, "that was close", "ViewerTwo", "Just Chatting", Utc(2026, 3, 1, 12)),
            [5] = Quote(5, "bare", null, null, null),
            [7] = Quote(7, "late night", "ViewerOne", null, Utc(2026, 3, 1, 23, 30)),
            [48] = Quote(48, "chat is wild", "ViewerOne", "Just Chatting", Utc(2026, 10, 20, 9)),
        },
        [ChannelB] = new() { [12] = Quote(12, "other channel", "Someone", null, null) },
    };

    private static readonly Dictionary<Guid, int> QuoteTotals = new()
    {
        [ChannelA] = 48,
        [ChannelB] = 1,
    };

    private readonly ServiceProvider _services;
    private readonly TemplateResolver _resolver;

    public ComposingHelperFixture()
    {
        IChannelRegistry channels = Substitute.For<IChannelRegistry>();
        channels.Get(ChannelA).Returns(ContextA());
        channels.Get(ChannelB).Returns(ContextB());

        _services = new ServiceCollection()
            .AddSingleton(QuoteServiceFake())
            .AddSingleton(PickListFake())
            .AddSingleton<ITemplateHelperProvider, ChatterTemplateHelpers>(_ => new(new Random(1)))
            .AddSingleton<ITemplateHelperProvider, QuoteTemplateHelpers>(sp =>
                new(sp.GetRequiredService<IServiceScopeFactory>())
            )
            .AddSingleton<ITemplateHelperProvider, VariableTemplateHelpers>()
            .AddSingleton<ITemplateHelperProvider, IncludeTemplateHelpers>(sp =>
                new(sp.GetRequiredService<IServiceScopeFactory>())
            )
            .AddSingleton<ITemplateHelperRegistry, TemplateHelperRegistryAggregate>()
            .AddSingleton<ITemplateResolver>(sp => new TemplateResolver(
                sp.GetRequiredService<IServiceScopeFactory>(),
                channels,
                NullLogger<TemplateResolver>.Instance,
                new FakeTimeProvider(Now),
                sp.GetRequiredService<ITemplateHelperRegistry>()
            ))
            .BuildServiceProvider();
        Registry = _services.GetRequiredService<ITemplateHelperRegistry>();
        _resolver = (TemplateResolver)_services.GetRequiredService<ITemplateResolver>();
    }

    public ITemplateHelperRegistry Registry { get; }

    /// <summary>Renders as the fixture viewer (ViewerOne, id 1001) triggering a command.</summary>
    public Task<string> RenderAsync(string template, Guid? broadcasterId) =>
        _resolver.ResolveAsync(
            template,
            new Dictionary<string, string> { ["user"] = "ViewerOne", ["user.id"] = "1001" },
            broadcasterId
        );

    /// <summary>Renders with the caller's own seed variables.</summary>
    public Task<string> RenderAsync(
        string template,
        Guid? broadcasterId,
        Dictionary<string, string> seeds
    ) => _resolver.ResolveAsync(template, seeds, broadcasterId);

    public ValueTask DisposeAsync() => _services.DisposeAsync();

    private static DateTime Utc(int year, int month, int day, int hour, int minute = 0) =>
        new(year, month, day, hour, minute, 0, DateTimeKind.Utc);

    private static QuoteDto Quote(
        int number,
        string text,
        string? author,
        string? game,
        DateTime? quotedAt
    ) => new(Guid.NewGuid(), number, text, author, game, quotedAt, DateTime.UnixEpoch);

    private static IQuoteService QuoteServiceFake()
    {
        IQuoteService quotes = Substitute.For<IQuoteService>();
        quotes
            .GetAsync(Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call =>
                Task.FromResult(
                    Quotes[call.ArgAt<Guid>(0)].TryGetValue(call.ArgAt<int>(1), out QuoteDto? found)
                        ? Result.Success(found)
                        : Result.Failure<QuoteDto>("missing", "NOT_FOUND")
                )
            );
        quotes
            .GetRandomAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(Result.Success(Quotes[call.ArgAt<Guid>(0)][12])));
        quotes
            .ListAsync(
                Arg.Any<Guid>(),
                Arg.Any<QuoteSearch>(),
                Arg.Any<PaginationParams>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                Guid channel = call.ArgAt<Guid>(0);
                QuoteDto newest = Quotes[channel].Values.MaxBy(q => q.Number)!;
                return Task.FromResult(
                    Result.Success(new PagedList<QuoteDto>([newest], 1, 1, QuoteTotals[channel]))
                );
            });
        return quotes;
    }

    private static IPickListService PickListFake()
    {
        IPickListService lists = Substitute.For<IPickListService>();
        lists
            .GetByNameAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call =>
                Task.FromResult(
                    call.ArgAt<string>(1) == "snacks"
                        ? Result.Success(
                            new PickListDto(
                                Guid.NewGuid(),
                                "snacks",
                                null,
                                call.ArgAt<Guid>(0) == ChannelA
                                    ? ["chips", "cookies", "fruit"]
                                    : ["tea"],
                                DateTime.UnixEpoch,
                                DateTime.UnixEpoch
                            )
                        )
                        : Result.Failure<PickListDto>("missing", "NOT_FOUND")
                )
            );
        return lists;
    }

    private static CachedCommand Command(string name, params string[] responses) =>
        new()
        {
            Name = name,
            TemplateResponses = responses,
            GlobalCooldown = 0,
            UserCooldown = 0,
            MinPermissionLevel = 0,
            Tier = responses.Length == 0 ? "pipeline" : "template",
        };

    private static ChannelContext ContextA()
    {
        ChannelContext context = new()
        {
            BroadcasterId = ChannelA,
            TwitchChannelId = "123456",
            ChannelName = "samplechannel",
            DisplayName = "SampleChannel",
            Timezone = "Europe/Amsterdam",
            Language = "en",
        };
        context.SessionChatters["1001"] = "ViewerOne";
        context.SessionChatters["1002"] = "ViewerTwo";
        foreach (
            CachedCommand command in new[]
            {
                Command("hug", "{user} sends a hug"),
                Command("pipelined"),
                Command("loopa", "a>{include.command:loopb}"),
                Command("loopb", "b>{include.command:loopa}"),
                Command("selfy", "{include.command:selfy}"),
                Command("c1", "1>{include.command:c2}"),
                Command("c2", "2>{include.command:c3}"),
                Command("c3", "3>{include.command:c4}"),
                Command("c4", "4>{include.command:c5}"),
                Command("c5", "5>{include.command:c6}"),
                Command("c6", "6>{include.command:c7}"),
                Command("c7", "end"),
            }
        )
            context.Commands[command.Name] = command;
        return context;
    }

    private static ChannelContext ContextB()
    {
        ChannelContext context = new()
        {
            BroadcasterId = ChannelB,
            TwitchChannelId = "654321",
            ChannelName = "otherchannel",
            DisplayName = "OtherChannel",
        };
        context.SessionChatters["2001"] = "OtherViewer";
        context.Commands["hug"] = Command("hug", "other hug");
        return context;
    }
}
