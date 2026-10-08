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
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Moderation.Services;
using NomNomzBot.Domain.Chat.Entities;
using NomNomzBot.Domain.Community.Events;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Moderation.Entities;
using NomNomzBot.Domain.Moderation.SpamDefense;
using NomNomzBot.Infrastructure.Moderation;
using NomNomzBot.Infrastructure.Moderation.EventHandlers;
using NomNomzBot.Infrastructure.Platform.Auth;
using NomNomzBot.Infrastructure.Platform.Persistence;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Moderation;

/// <summary>
/// The follow-spike wire (spam-defense.md §L3.1 / SD9) against a real database and a recording Twitch
/// users API. The owner's rule is the spine: an account is blocked only when two or more indicators
/// agree AND it has no history here, and the block is the reversible Twitch block, never a ban.
/// </summary>
public class FollowBotSweepServiceTests : IDisposable
{
    private static readonly Guid Channel = Guid.Parse("0199c000-0000-7000-8000-0000000000e1");
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection;
    private readonly FakeTimeProvider _time = new(Now);
    private readonly ITwitchUsersApi _twitch = Substitute.For<ITwitchUsersApi>();
    private readonly Dictionary<string, TwitchUser> _profiles = [];

    public FollowBotSweepServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        using AppDbContext db = NewDbContext();
        db.Database.EnsureCreated();
        db.Database.ExecuteSqlRaw("PRAGMA foreign_keys = OFF;");
        db.Channels.Add(
            new Channel
            {
                Id = Channel,
                OwnerUserId = Guid.NewGuid(),
                Provider = AuthEnums.Platform.Twitch,
                ExternalChannelId = "chan-ext",
                Name = "chan",
                NameNormalized = "chan",
                CreatedAt = Now.UtcDateTime.AddDays(-60),
            }
        );
        db.SaveChanges();

        _twitch
            .GetUsersByIdsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
                Result.Success<IReadOnlyList<TwitchUser>>([
                    .. call.Arg<IReadOnlyList<string>>()
                        .Where(_profiles.ContainsKey)
                        .Select(id => _profiles[id]),
                ])
            );
        _twitch
            .BlockUserAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success());
    }

    public void Dispose() => _connection.Dispose();

    private AppDbContext NewDbContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);

    // ---- Fixtures -------------------------------------------------------------------------------

    /// <summary>A generated handle on a two-hour-old account with no bio and the default avatar.</summary>
    private void AddBot(string id)
    {
        _profiles[id] = new TwitchUser(
            id,
            $"viewer{id}8042193",
            $"Viewer{id}",
            "",
            "",
            "",
            "https://static-cdn.jtvnw.net/user-default-pictures-uv/abc-profile_image-300x300.png",
            "",
            0,
            Now.AddHours(-2)
        );
    }

    /// <summary>An ordinary person: a normal handle, a bio, a custom avatar, years old.</summary>
    private void AddRealViewer(string id)
    {
        _profiles[id] = new TwitchUser(
            id,
            $"kate_{id}",
            $"Kate{id}",
            "",
            "",
            "I play too much Factorio",
            "https://static-cdn.jtvnw.net/jtv_user_pictures/custom-profile_image-300x300.png",
            "",
            12,
            Now.AddDays(-900)
        );
    }

    // The follow event carries the same login Twitch reports for the account.
    private FollowSpikeWindow Window(params string[] ids) =>
        new(
            Guid.Parse("0199c000-0000-7000-8000-0000000000b1"),
            [
                .. ids.Select(id => new FollowObservation(
                    id,
                    _profiles.TryGetValue(id, out TwitchUser? profile)
                        ? profile.Login
                        : $"login{id}",
                    $"Login{id}",
                    Now
                )),
            ],
            ids.Length
        );

    private static SpamDefenseSettings Live => new() { DryRun = false };

    private FollowBotSweepService NewSweep(AppDbContext db) =>
        new(db, _twitch, _time, NullLogger<FollowBotSweepService>.Instance);

    private async Task<List<FollowBotBlock>> StoredBlocksAsync()
    {
        using AppDbContext read = NewDbContext();
        return await read
            .FollowBotBlocks.IgnoreQueryFilters()
            .OrderBy(b => b.SubjectPlatformUserId)
            .ToListAsync();
    }

    private int BlockCalls() =>
        _twitch
            .ReceivedCalls()
            .Count(c => c.GetMethodInfo().Name == nameof(ITwitchUsersApi.BlockUserAsync));

    // ---- The block ------------------------------------------------------------------------------

    [Fact]
    public async Task EachAccountWithTwoAgreeingIndicatorsAndNoHistory_IsBlockedOnce_AndRecordedWithItsEvidence()
    {
        AddBot("1001");
        AddBot("1002");
        AddBot("1003");
        foreach (string id in new[] { "2001", "2002", "2003", "2004" })
            AddRealViewer(id);

        using AppDbContext db = NewDbContext();
        FollowBotSweepOutcome outcome = await NewSweep(db)
            .SweepAsync(
                Channel,
                Window("1001", "2001", "1002", "2002", "1003", "2003", "2004"),
                Live
            );

        outcome.Should().Be(new FollowBotSweepOutcome(7, 3, 3, 0, false));
        foreach (string id in new[] { "1001", "1002", "1003" })
            await _twitch
                .Received(1)
                .BlockUserAsync(
                    Channel,
                    id,
                    Arg.Any<string?>(),
                    "spam",
                    Arg.Any<CancellationToken>()
                );
        BlockCalls().Should().Be(3, "the four real viewers must never reach Twitch");

        List<FollowBotBlock> rows = await StoredBlocksAsync();
        rows.Select(r => r.SubjectPlatformUserId).Should().Equal("1001", "1002", "1003");
        rows.Should().OnlyContain(r => r.BroadcasterId == Channel);
        rows.Select(r => r.BatchId).Distinct().Should().ContainSingle();
        rows.Should()
            .OnlyContain(r => r.BatchExamined == 7 && !r.WasDryRun && r.RestoredAt == null);
        rows.Should()
            .OnlyContain(r =>
                r.Indicators.Contains(nameof(FollowBotIndicator.GeneratedHandlePattern))
                && r.Indicators.Contains(nameof(FollowBotIndicator.ZeroHistoryFreshAccount))
            );
    }

    [Fact]
    public async Task AnAccountWithOnlyOneIndicator_IsLeftAlone()
    {
        // A generated-looking handle on an old account with a bio: one signal, never enough alone.
        _profiles["3001"] = new TwitchUser(
            "3001",
            "player1234567",
            "Player",
            "",
            "",
            "hello",
            "https://static-cdn.jtvnw.net/jtv_user_pictures/x-profile_image-300x300.png",
            "",
            0,
            Now.AddDays(-400)
        );

        using AppDbContext db = NewDbContext();
        FollowBotSweepOutcome outcome = await NewSweep(db)
            .SweepAsync(Channel, Window("3001"), Live);

        outcome.Flagged.Should().Be(0);
        BlockCalls().Should().Be(0);
        (await StoredBlocksAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task AnAccountThatHasChattedAnywhereOnTheInstance_IsLeftAlone_EvenWithTwoIndicators()
    {
        AddBot("1001");
        AddBot("1002");
        using (AppDbContext seed = NewDbContext())
        {
            seed.ChatMessages.Add(
                new ChatMessage
                {
                    Id = Guid.NewGuid().ToString(),
                    BroadcasterId = Guid.Parse("0199c000-0000-7000-8000-0000000000e9"),
                    UserId = "1001",
                    Username = "viewer",
                    DisplayName = "Viewer",
                    UserType = "viewer",
                    Message = "hello from another channel",
                    CreatedAt = Now.UtcDateTime.AddDays(-3),
                }
            );
            seed.SaveChanges();
        }

        using AppDbContext db = NewDbContext();
        await NewSweep(db).SweepAsync(Channel, Window("1001", "1002"), Live);

        await _twitch
            .DidNotReceive()
            .BlockUserAsync(
                Channel,
                "1001",
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            );
        (await StoredBlocksAsync()).Select(r => r.SubjectPlatformUserId).Should().Equal("1002");
    }

    [Fact]
    public async Task AnAccountThatFollowedThisChannelBefore_IsLeftAlone_EvenWithTwoIndicators()
    {
        AddBot("1001");
        AddBot("1002");
        Guid userId = Guid.NewGuid();
        using (AppDbContext seed = NewDbContext())
        {
            seed.Users.Add(
                new User
                {
                    Id = userId,
                    TwitchUserId = "1001",
                    Username = "viewer10018042193",
                    UsernameNormalized = "viewer10018042193",
                    DisplayName = "Viewer1001",
                }
            );
            seed.ChannelEvents.Add(
                new ChannelEvent
                {
                    Id = Guid.NewGuid().ToString("N"),
                    ChannelId = Channel,
                    UserId = userId,
                    Type = "channel.follow",
                    CreatedAt = Now.UtcDateTime.AddDays(-10),
                }
            );
            seed.SaveChanges();
        }

        using AppDbContext db = NewDbContext();
        await NewSweep(db).SweepAsync(Channel, Window("1001", "1002"), Live);

        (await StoredBlocksAsync()).Select(r => r.SubjectPlatformUserId).Should().Equal("1002");
        BlockCalls().Should().Be(1);
    }

    [Fact]
    public async Task AModeratorOfTheChannel_HasStanding_AndIsLeftAlone()
    {
        AddBot("1001");
        AddBot("1002");
        Guid userId = Guid.NewGuid();
        using (AppDbContext seed = NewDbContext())
        {
            seed.Users.Add(
                new User
                {
                    Id = userId,
                    TwitchUserId = "1001",
                    Username = "viewer10018042193",
                    UsernameNormalized = "viewer10018042193",
                    DisplayName = "Viewer1001",
                }
            );
            seed.ChannelModerators.Add(
                new ChannelModerator
                {
                    ChannelId = Channel,
                    UserId = userId,
                    GrantedAt = Now.UtcDateTime.AddDays(-1),
                }
            );
            seed.SaveChanges();
        }

        using AppDbContext db = NewDbContext();
        await NewSweep(db).SweepAsync(Channel, Window("1001", "1002"), Live);

        (await StoredBlocksAsync()).Select(r => r.SubjectPlatformUserId).Should().Equal("1002");
        BlockCalls().Should().Be(1);
    }

    [Fact]
    public async Task WhenTwitchCannotSayWhoTheAccountIs_NothingIsBlocked()
    {
        // No profile means no age and no bio: a missing fact must never become evidence.
        using AppDbContext db = NewDbContext();
        FollowBotSweepOutcome outcome = await NewSweep(db)
            .SweepAsync(Channel, Window("1001", "1002"), Live);

        outcome.Flagged.Should().Be(0);
        BlockCalls().Should().Be(0);
        (await StoredBlocksAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task ABlockTwitchRefuses_LeavesNoRowClaimingItHappened()
    {
        AddBot("1001");
        AddBot("1002");
        _twitch
            .BlockUserAsync(
                Channel,
                "1001",
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Failure("twitch said no"));

        using AppDbContext db = NewDbContext();
        FollowBotSweepOutcome outcome = await NewSweep(db)
            .SweepAsync(Channel, Window("1001", "1002"), Live);

        outcome.Blocked.Should().Be(1);
        outcome.Failed.Should().Be(1);
        (await StoredBlocksAsync()).Select(r => r.SubjectPlatformUserId).Should().Equal("1002");
    }

    // ---- Dry run and the observation window ----------------------------------------------------

    [Fact]
    public async Task DryRun_RecordsTheRows_AndMakesNoTwitchCall()
    {
        AddBot("1001");
        AddBot("1002");

        using AppDbContext db = NewDbContext();
        FollowBotSweepOutcome outcome = await NewSweep(db)
            .SweepAsync(Channel, Window("1001", "1002"), new SpamDefenseSettings { DryRun = true });

        outcome.Should().Be(new FollowBotSweepOutcome(2, 2, 0, 0, true));
        BlockCalls().Should().Be(0);
        List<FollowBotBlock> rows = await StoredBlocksAsync();
        rows.Should().HaveCount(2);
        rows.Should().OnlyContain(r => r.WasDryRun, "a row that blocked nothing must say so");
    }

    [Fact]
    public async Task InsideTheSevenDayWindow_ALiveChannelStillOnlyObserves()
    {
        AddBot("1001");
        AddBot("1002");
        using (AppDbContext young = NewDbContext())
            young
                .Channels.Where(c => c.Id == Channel)
                .ExecuteUpdate(s => s.SetProperty(c => c.CreatedAt, Now.UtcDateTime.AddDays(-2)));

        using AppDbContext db = NewDbContext();
        FollowBotSweepOutcome outcome = await NewSweep(db)
            .SweepAsync(Channel, Window("1001", "1002"), Live);

        outcome.DryRun.Should().BeTrue();
        BlockCalls().Should().Be(0);
        (await StoredBlocksAsync()).Should().OnlyContain(r => r.WasDryRun);
    }

    // ---- The live path: a follow event reaches the sweep through the handler --------------------

    private ServiceProvider Services(SpamDefenseSettings? settings = null)
    {
        ServiceCollection services = new();
        services.AddSingleton<TimeProvider>(_time);
        services.AddSingleton(_twitch);
        services.AddSingleton(Substitute.For<IModerationService>());
        services.AddSingleton<ICurrentTenantService>(new CurrentTenantService());
        services.AddScoped(_ => NewDbContext());
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddScoped<ISpamDefenseService, SpamDefenseService>();
        services.AddScoped<FollowBotSweepService>();
        services.AddSingleton<FollowSpikeTracker>();
        services.AddLogging();
        ServiceProvider provider = services.BuildServiceProvider();
        if (settings is not null)
        {
            using IServiceScope scope = provider.CreateScope();
            scope
                .ServiceProvider.GetRequiredService<ISpamDefenseService>()
                .UpdateSettingsAsync(Channel, settings)
                .GetAwaiter()
                .GetResult();
        }
        return provider;
    }

    private static FollowEvent FollowAt(
        string id,
        DateTimeOffset at,
        string provider = AuthEnums.Platform.Twitch
    ) =>
        new()
        {
            BroadcasterId = Channel,
            Provider = provider,
            UserId = id,
            UserLogin = $"viewer{id}8042193",
            UserDisplayName = $"Viewer{id}",
            FollowedAt = at,
        };

    private static FollowSpikeHandler HandlerFor(ServiceProvider provider) =>
        new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<FollowSpikeTracker>(),
            NullLogger<FollowSpikeHandler>.Instance
        );

    private async Task QuietTwelveMinutesAsync(FollowSpikeHandler handler)
    {
        for (int minute = 0; minute < 12; minute++)
        {
            string id = $"calm{minute}";
            AddRealViewer(id);
            await handler.HandleAsync(
                FollowAt(id, Now.AddMinutes(minute - 20)),
                CancellationToken.None
            );
        }
    }

    [Fact]
    public async Task ASimulatedFollowSpike_BlocksEachBotOnce_AndRecordsOneBatch()
    {
        using ServiceProvider provider = Services(Live);
        FollowSpikeHandler handler = HandlerFor(provider);
        await QuietTwelveMinutesAsync(handler);
        BlockCalls().Should().Be(0, "an ordinary channel's follows are never swept");

        string[] botIds = ["1001", "1002", "1003", "1004", "1005", "1006"];
        foreach (string id in botIds)
            AddBot(id);
        DateTimeOffset spike = Now.AddMinutes(-8);
        foreach (string id in botIds)
            await handler.HandleAsync(
                FollowAt(id, spike.AddSeconds(Array.IndexOf(botIds, id))),
                CancellationToken.None
            );

        foreach (string id in botIds)
            await _twitch
                .Received(1)
                .BlockUserAsync(
                    Channel,
                    id,
                    Arg.Any<string?>(),
                    "spam",
                    Arg.Any<CancellationToken>()
                );
        BlockCalls().Should().Be(6);
        List<FollowBotBlock> rows = await StoredBlocksAsync();
        rows.Select(r => r.SubjectPlatformUserId).Should().BeEquivalentTo(botIds);
        rows.Select(r => r.BatchId).Distinct().Should().ContainSingle();
    }

    [Fact]
    public async Task AViralMomentOfRealPeople_IsASpikeButBlocksNobody()
    {
        using ServiceProvider provider = Services(Live);
        FollowSpikeHandler handler = HandlerFor(provider);
        await QuietTwelveMinutesAsync(handler);

        DateTimeOffset spike = Now.AddMinutes(-8);
        for (int i = 0; i < 20; i++)
        {
            string id = $"fan{i}";
            AddRealViewer(id);
            await handler.HandleAsync(FollowAt(id, spike.AddSeconds(i)), CancellationToken.None);
        }

        BlockCalls().Should().Be(0);
        (await StoredBlocksAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task AChannelWithTheStackOff_NeverSweeps()
    {
        using ServiceProvider provider = Services(
            new SpamDefenseSettings { IsEnabled = false, DryRun = false }
        );
        FollowSpikeHandler handler = HandlerFor(provider);
        await QuietTwelveMinutesAsync(handler);
        for (int i = 0; i < 8; i++)
        {
            AddBot($"90{i}");
            await handler.HandleAsync(
                FollowAt($"90{i}", Now.AddMinutes(-8).AddSeconds(i)),
                CancellationToken.None
            );
        }

        BlockCalls().Should().Be(0);
        (await StoredBlocksAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task AKickFollow_IsNotSwept_BecauseBlockIsATwitchAction()
    {
        using ServiceProvider provider = Services(Live);
        FollowSpikeHandler handler = HandlerFor(provider);
        await QuietTwelveMinutesAsync(handler);
        for (int i = 0; i < 8; i++)
        {
            AddBot($"80{i}");
            await handler.HandleAsync(
                FollowAt($"80{i}", Now.AddMinutes(-8).AddSeconds(i), AuthEnums.Platform.Kick),
                CancellationToken.None
            );
        }

        BlockCalls().Should().Be(0);
        (await StoredBlocksAsync()).Should().BeEmpty();
    }
}
