// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Platform.Templating;
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.Bot;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating.Helpers.Bot;

/// <summary>
/// The catalog section 5 bot: the name SampleBot, version 1.42.0, up 3 days 4 hours, in channel
/// <c>samplechannel</c> (prefix <c>!</c>, Europe/Amsterdam, English), the fakes standing at the service
/// interfaces and a fixed clock.
/// </summary>
internal sealed class BotHelperFixture : IAsyncDisposable
{
    public static readonly Guid ChannelA = Guid.Parse("00000000-0000-0000-0000-00000000000a");

    public static readonly DateTimeOffset Now = new(2026, 10, 26, 14, 30, 0, TimeSpan.Zero);

    private readonly ServiceProvider _services;
    private readonly TemplateResolver _resolver;

    public BotHelperFixture(BotWorld? world = null)
    {
        BotWorld set = world ?? new();

        IChannelRegistry channels = Substitute.For<IChannelRegistry>();
        channels.Get(ChannelA).Returns(Context(set));

        Identity = Substitute.For<IBotIdentityDirectory>();
        Identity
            .NameAsync(Arg.Any<IServiceProvider>(), Arg.Any<CancellationToken>())
            .Returns("SampleBot");

        Builtins = Substitute.For<IBuiltinCommandService>();
        Builtins
            .ListAsync(ChannelA.ToString(), Arg.Any<CancellationToken>())
            .Returns(
                set.BuiltinsFail
                    ? Result.Failure<IReadOnlyList<BuiltinCommandDto>>("down", "UNAVAILABLE")
                    : Result.Success<IReadOnlyList<BuiltinCommandDto>>(set.Builtins ?? Catalogue())
            );

        Timers = Substitute.For<ITimerManagementService>();
        Timers
            .ListAsync(
                ChannelA.ToString(),
                Arg.Any<PaginationParams>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                set.TimersFail
                    ? Result.Failure<PagedList<TimerListItem>>("down", "UNAVAILABLE")
                    : Result.Success(new PagedList<TimerListItem>(TimerRows(), 1, 100, 6))
            );

        Moderator = Substitute.For<IBotModeratorStatusReader>();
        Moderator
            .ReadAsync(ChannelA, Arg.Any<CancellationToken>())
            .Returns(new BotModeratorReading(set.Standing, null, null));

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                set.BaseUrl is null
                    ? []
                    : new Dictionary<string, string?> { ["App:BaseUrl"] = set.BaseUrl }
            )
            .Build();

        _services = new ServiceCollection()
            .AddSingleton(Identity)
            .AddSingleton<IBotProcessInfo>(new FixedProcess())
            .AddSingleton(Builtins)
            .AddSingleton(Timers)
            .AddSingleton(Moderator)
            .AddSingleton(configuration)
            .AddTemplateHelpers(typeof(TemplateResolver).Assembly)
            .BuildServiceProvider();
        Registry = _services.GetRequiredService<ITemplateHelperRegistry>();
        _resolver = new TemplateResolver(
            _services.GetRequiredService<IServiceScopeFactory>(),
            channels,
            NullLogger<TemplateResolver>.Instance,
            new FakeTimeProvider(Now),
            Registry
        );
    }

    public IBotIdentityDirectory Identity { get; }

    public IBuiltinCommandService Builtins { get; }

    public ITimerManagementService Timers { get; }

    public IBotModeratorStatusReader Moderator { get; }

    public ITemplateHelperRegistry Registry { get; }

    /// <summary>The seeds of a chat command typed by ViewerOne, a Tier 1 subscriber.</summary>
    public static Dictionary<string, string> Seeds(string role = "subscriber") =>
        new()
        {
            ["user"] = "ViewerOne",
            ["user.name"] = "viewerone",
            ["user.id"] = "1001",
            ["user.role"] = role,
        };

    public Task<string> RenderAsync(string template, Dictionary<string, string>? seeds = null) =>
        _resolver.ResolveAsync(template, seeds ?? Seeds(), ChannelA);

    public ValueTask DisposeAsync() => _services.DisposeAsync();

    /// <summary>A built-in row as the service lists it: <c>!so</c> and <c>!uptime</c> for moderators, <c>!sr</c> for subscribers.</summary>
    public static BuiltinCommandDto Builtin(
        string key,
        string floor,
        bool enabled = true,
        string? channelFloor = null
    ) =>
        new(
            key,
            "!" + key,
            enabled,
            15,
            0,
            floor,
            key,
            false,
            false,
            false,
            null,
            null,
            channelFloor,
            0
        );

    public static CachedCommand Command(
        string name,
        int level = 0,
        string[]? aliases = null,
        string prefixMode = "Default",
        string? customPrefix = null,
        string matchMode = "StartsWith"
    ) =>
        new()
        {
            Name = name,
            TemplateResponses = ["reply"],
            GlobalCooldown = 0,
            UserCooldown = 0,
            MinPermissionLevel = level,
            Tier = "template",
            Aliases = aliases ?? [],
            PrefixMode = prefixMode,
            CustomPrefix = customPrefix,
            MatchMode = matchMode,
        };

    private static List<BuiltinCommandDto> Catalogue() =>
        [
            Builtin("so", "Moderator"),
            Builtin("uptime", "Everyone", channelFloor: "Moderator"),
            Builtin("sr", "Subscriber"),
            Builtin("hidden", "Everyone", enabled: false),
        ];

    private static List<TimerListItem> TimerRows()
    {
        List<TimerListItem> rows = [];
        for (int i = 1; i <= 6; i++)
            rows.Add(new(Guid.NewGuid(), $"timer{i}", 10, i != 6, false, null, 1, DateTime.UtcNow));
        return rows;
    }

    private static ChannelContext Context(BotWorld world)
    {
        ChannelContext context = new()
        {
            BroadcasterId = ChannelA,
            TwitchChannelId = "123456",
            ChannelName = "samplechannel",
            DisplayName = "SampleChannel",
            Timezone = "Europe/Amsterdam",
            Language = world.Language,
            CommandPrefix = "!",
            LoadedAt = Now - TimeSpan.FromMinutes(140),
        };
        foreach (CachedCommand command in world.Commands ?? DefaultCommands())
        {
            context.Commands[command.Name.TrimStart('!')] = command;
            foreach (string alias in command.Aliases)
                context.Commands[alias] = command;
        }
        return context;
    }

    private static List<CachedCommand> DefaultCommands() =>
        [Command("hug", aliases: ["hugs"]), Command("lurk"), Command("rx", matchMode: "Regex")];

    /// <summary>Up 3 days 4 hours at the fixed clock; build 1.42.0.</summary>
    private sealed class FixedProcess : IBotProcessInfo
    {
        public DateTimeOffset StartedAt { get; } = Now - TimeSpan.FromHours(76);

        public string Version => "1.42.0";
    }
}

/// <summary>What differs from the catalog fixture in one test.</summary>
internal sealed record BotWorld(
    string? Language = "en",
    string? BaseUrl = "https://nomnomz.bot",
    BotModeratorStanding Standing = BotModeratorStanding.Moderator,
    List<CachedCommand>? Commands = null,
    List<BuiltinCommandDto>? Builtins = null,
    bool BuiltinsFail = false,
    bool TimersFail = false
);
