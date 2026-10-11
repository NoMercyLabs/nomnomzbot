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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Moderation.Dtos;
using NomNomzBot.Application.Raids;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Moderation.Entities;
using NomNomzBot.Domain.Platform.Events;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Stream.Events;
using NomNomzBot.Infrastructure.Moderation;
using NomNomzBot.Infrastructure.Platform.Persistence;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Moderation;

/// <summary>
/// Every incoming raider is vetted on arrival: the stored row says how old the account is, whether the
/// channel raided with them before, and whether they are banned here or blocked across the network.
/// </summary>
public sealed class RaidAssessmentServiceTests : IDisposable
{
    private static readonly Guid Tenant = Guid.Parse("0199e000-0000-7000-8000-0000000002a1");
    private const string OwnTwitchId = "own-1";
    private const string RaiderId = "raider-9";
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection;
    private readonly ITwitchUsersApi _twitchUsers = Substitute.For<ITwitchUsersApi>();
    private readonly IRaidHistoryReader _history = Substitute.For<IRaidHistoryReader>();
    private readonly IEventBus _bus = Substitute.For<IEventBus>();

    public RaidAssessmentServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        using AppDbContext db = NewDbContext();
        db.Database.EnsureCreated();
        db.Database.ExecuteSqlRaw("PRAGMA foreign_keys = OFF;");
        db.Channels.Add(
            new Channel
            {
                Id = Tenant,
                OwnerUserId = Guid.NewGuid(),
                Provider = AuthEnums.Platform.Twitch,
                ExternalChannelId = OwnTwitchId,
                Name = "own",
                NameNormalized = "own",
            }
        );
        db.SaveChanges();

        SetTwitchAccountCreated(Now.AddDays(-400));
        SetEarlierRaids(0, Now);
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task A_new_raider_with_no_history_is_unknown_and_the_row_keeps_the_account_age()
    {
        Result<RaidAssessmentDto> result = await NewService().AssessAsync(Raid());

        result.IsSuccess.Should().BeTrue();
        RaidAssessment row = await SingleRowAsync();
        row.BroadcasterId.Should().Be(Tenant);
        row.RaiderTwitchUserId.Should().Be(RaiderId);
        row.RaiderLogin.Should().Be("raider");
        row.RaiderDisplayName.Should().Be("Raider");
        row.ViewerCount.Should().Be(42);
        row.AccountCreatedAt.Should().Be(Now.AddDays(-400).UtcDateTime);
        row.EarlierRaidCount.Should().Be(0);
        row.IsBanned.Should().BeFalse();
        row.IsNetworkBlocked.Should().BeFalse();
        row.Verdict.Should().Be(RaidVerdicts.Unknown);
        row.OccurredAt.Should().Be(Now.UtcDateTime);
        result.Value.Verdict.Should().Be(RaidVerdicts.Unknown);
        result.Value.Id.Should().Be(row.Id);
    }

    [Fact]
    public async Task A_raider_the_channel_raided_with_before_is_known()
    {
        SetEarlierRaids(2, Now.AddDays(-3));

        await NewService().AssessAsync(Raid());

        RaidAssessment row = await SingleRowAsync();
        row.EarlierRaidCount.Should().Be(2);
        row.Verdict.Should().Be(RaidVerdicts.Known);
    }

    [Fact]
    public async Task The_raid_already_in_the_journal_does_not_count_as_an_earlier_raid()
    {
        SetEarlierRaids(1, Now.AddSeconds(-2));

        await NewService().AssessAsync(Raid());

        RaidAssessment row = await SingleRowAsync();
        row.EarlierRaidCount.Should().Be(0);
        row.Verdict.Should().Be(RaidVerdicts.Unknown);
    }

    [Fact]
    public async Task A_raider_banned_here_is_hostile_even_with_earlier_raids()
    {
        SetEarlierRaids(3, Now.AddDays(-9));
        await SeedHistoryAsync(ModerationHistoryEntryKinds.Ban, Now.AddDays(-1));

        await NewService().AssessAsync(Raid());

        RaidAssessment row = await SingleRowAsync();
        row.IsBanned.Should().BeTrue();
        row.Verdict.Should().Be(RaidVerdicts.Hostile);
    }

    [Fact]
    public async Task A_raider_unbanned_after_the_ban_is_not_banned()
    {
        await SeedHistoryAsync(ModerationHistoryEntryKinds.Ban, Now.AddDays(-5));
        await SeedHistoryAsync(ModerationHistoryEntryKinds.Unban, Now.AddDays(-2));

        await NewService().AssessAsync(Raid());

        RaidAssessment row = await SingleRowAsync();
        row.IsBanned.Should().BeFalse();
        row.Verdict.Should().Be(RaidVerdicts.Unknown);
    }

    [Theory]
    [InlineData(NetworkBlockStatus.Active, true)]
    [InlineData(NetworkBlockStatus.Partial, true)]
    [InlineData(NetworkBlockStatus.Lifted, false)]
    public async Task A_network_block_makes_the_raider_hostile_unless_it_was_lifted(
        string status,
        bool hostile
    )
    {
        await using (AppDbContext db = NewDbContext())
        {
            db.NetworkBlocks.Add(
                new NetworkBlock
                {
                    TargetUserId = Guid.NewGuid(),
                    TargetTwitchUserId = RaiderId,
                    Justification = "Ticket #1",
                    AppliedByPrincipalId = Guid.NewGuid(),
                    AppliedAt = Now.UtcDateTime,
                    Status = status,
                }
            );
            await db.SaveChangesAsync();
        }

        await NewService().AssessAsync(Raid());

        RaidAssessment row = await SingleRowAsync();
        row.IsNetworkBlocked.Should().Be(hostile);
        row.Verdict.Should().Be(hostile ? RaidVerdicts.Hostile : RaidVerdicts.Unknown);
    }

    [Fact]
    public async Task A_failed_twitch_lookup_still_stores_the_row_with_no_account_age()
    {
        _twitchUsers
            .GetUsersByIdsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<IReadOnlyList<TwitchUser>>("down", "TWITCH_DOWN"));

        Result<RaidAssessmentDto> result = await NewService().AssessAsync(Raid());

        result.IsSuccess.Should().BeTrue();
        RaidAssessment row = await SingleRowAsync();
        row.AccountCreatedAt.Should().BeNull();
        row.Verdict.Should().Be(RaidVerdicts.Unknown);
    }

    [Fact]
    public async Task Storing_the_row_tells_the_dashboard_through_a_config_change_event()
    {
        ChannelConfigChangedEvent? published = null;
        await _bus.PublishAsync(
            Arg.Do<ChannelConfigChangedEvent>(e => published = e),
            Arg.Any<CancellationToken>()
        );

        await NewService().AssessAsync(Raid());

        RaidAssessment row = await SingleRowAsync();
        published.Should().NotBeNull();
        published!.BroadcasterId.Should().Be(Tenant);
        published.Domain.Should().Be("raid-assessments");
        published.EntityId.Should().Be(row.Id.ToString());
        published.Action.Should().Be("created");
    }

    [Fact]
    public async Task The_list_returns_the_newest_raids_first_for_one_channel_only()
    {
        RaidAssessmentService service = NewService();
        await service.AssessAsync(Raid(Now.AddHours(-2), "older"));
        await service.AssessAsync(Raid(Now, "newer"));
        await using (AppDbContext db = NewDbContext())
        {
            db.RaidAssessments.Add(
                new RaidAssessment
                {
                    BroadcasterId = Guid.NewGuid(),
                    RaiderTwitchUserId = "x",
                    RaiderLogin = "x",
                    RaiderDisplayName = "X",
                    OccurredAt = Now.UtcDateTime,
                }
            );
            await db.SaveChangesAsync();
        }

        Result<PagedList<RaidAssessmentDto>> list = await service.ListRecentAsync(
            Tenant,
            new PaginationParams(1, 10)
        );

        list.IsSuccess.Should().BeTrue();
        list.Value.TotalCount.Should().Be(2);
        list.Value.Items.Select(i => i.RaiderLogin).Should().Equal("newer", "older");
    }

    private RaidAssessmentService NewService() => new(NewDbContext(), _twitchUsers, _history, _bus);

    private AppDbContext NewDbContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);

    private static RaidEvent Raid(DateTimeOffset? at = null, string login = "raider") =>
        new()
        {
            BroadcasterId = Tenant,
            OccurredAt = at ?? Now,
            FromUserId = RaiderId,
            FromDisplayName = char.ToUpperInvariant(login[0]) + login[1..],
            FromLogin = login,
            ViewerCount = 42,
        };

    private async Task<RaidAssessment> SingleRowAsync()
    {
        await using AppDbContext db = NewDbContext();
        return await db.RaidAssessments.AsNoTracking().SingleAsync();
    }

    private async Task SeedHistoryAsync(string kind, DateTimeOffset at)
    {
        await using AppDbContext db = NewDbContext();
        db.ModerationHistoryEntries.Add(
            new ModerationHistoryEntry
            {
                BroadcasterId = Tenant,
                SubjectUserId = Guid.NewGuid(),
                SubjectTwitchUserId = RaiderId,
                ActionType = kind,
                OccurredAt = at.UtcDateTime,
            }
        );
        await db.SaveChangesAsync();
    }

    private void SetTwitchAccountCreated(DateTimeOffset createdAt) =>
        _twitchUsers
            .GetUsersByIdsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Success<IReadOnlyList<TwitchUser>>([
                    new TwitchUser(RaiderId, "raider", "Raider", "", "", "", "", "", 0, createdAt),
                ])
            );

    private void SetEarlierRaids(int count, DateTimeOffset lastAt)
    {
        Dictionary<string, RaidStats> incoming = [];
        if (count > 0)
            incoming[RaiderId] = new RaidStats(count, lastAt);
        _history
            .GetHistoryAsync(Tenant, OwnTwitchId, Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(new RaidHistory(new Dictionary<string, RaidStats>(), incoming))
            );
    }
}
