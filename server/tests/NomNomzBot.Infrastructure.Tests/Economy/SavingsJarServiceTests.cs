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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.DTOs.Economy;
using NomNomzBot.Domain.Economy.Entities;
using NomNomzBot.Domain.Economy.Events;
using NomNomzBot.Infrastructure.Economy;
using NomNomzBot.Infrastructure.EventStore;
using NomNomzBot.Infrastructure.Tests.EventStore;
using NomNomzBot.Infrastructure.Tests.Identity;
using FakeTimeProvider = Microsoft.Extensions.Time.Testing.FakeTimeProvider;

namespace NomNomzBot.Infrastructure.Tests.Economy;

/// <summary>
/// Proves savings jars (economy.md §3.7) against the real SQLite ledger harness: create makes the owner
/// membership; invite→accept is mutual-consent cross-channel federation; the membership predicate blocks a
/// non-member; contribute debits the viewer, grows the jar, and fires the contribution + goal events; a closed
/// jar is rejected; and withdraw credits an account back and shrinks the jar.
/// </summary>
public sealed class SavingsJarServiceTests
{
    private static readonly Guid Owner = Guid.Parse("0192a000-0000-7000-8000-0000000000e1");
    private static readonly Guid Partner = Guid.Parse("0192a000-0000-7000-8000-0000000000e2");
    private static readonly Guid Viewer = Guid.Parse("0192a000-0000-7000-8000-0000000000e3");
    private static readonly FakeTimeProvider Clock = new(
        new DateTimeOffset(2026, 6, 21, 12, 0, 0, TimeSpan.Zero)
    );

    private static (SavingsJarService Sut, EventStoreTestDbContext Db, RecordingEventBus Bus) New(
        SqliteTestDatabase database
    )
    {
        EventStoreTestDbContext db = database.NewContext();
        db.CurrencyConfigs.Add(
            new CurrencyConfig
            {
                BroadcasterId = Owner,
                CurrencyName = "points",
                IsEnabled = true,
                StartingBalance = 100,
            }
        );
        db.SaveChanges();
        EventStoreTestUnitOfWork uow = new(db);
        TenantSequenceAllocator allocator = new(db);
        RecordingEventBus bus = new();
        CurrencyAccountService accounts = new(db, allocator, uow, bus, Clock);
        SavingsJarService sut = new(db, accounts, bus, Clock);
        return (sut, db, bus);
    }

    private static async Task<Guid> CreateJarAsync(
        SavingsJarService sut,
        long? goal = null,
        bool open = true
    )
    {
        Result<SavingsJarDto> jar = await sut.CreateJarAsync(
            Owner,
            new CreateSavingsJarRequest("Charity Pot", null, goal, null, open, null)
        );
        return jar.Value.Id;
    }

    [Fact]
    public async Task CreateJar_makes_the_owner_membership()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        (SavingsJarService sut, _, _) = New(database);

        Guid jarId = await CreateJarAsync(sut);

        // The owner can read its own jar (membership predicate passes via ownership).
        (await sut.GetJarAsync(Owner, jarId))
            .IsSuccess.Should()
            .BeTrue();
        (await sut.ListJarsForChannelAsync(Owner)).Value.Should().ContainSingle(j => j.Id == jarId);
    }

    [Fact]
    public async Task Invite_then_accept_federates_a_partner_channel()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        (SavingsJarService sut, _, _) = New(database);
        Guid jarId = await CreateJarAsync(sut);

        Result<SavingsJarMembershipDto> invite = await sut.InviteChannelAsync(
            Owner,
            new InviteChannelRequest(jarId, Partner, "Partner", null, null)
        );
        invite.Value.Status.Should().Be("Pending");

        Result<SavingsJarMembershipDto> accept = await sut.AcceptMembershipAsync(
            Partner,
            invite.Value.Id
        );

        accept.Value.Status.Should().Be("Accepted");
        (await sut.ListJarsForChannelAsync(Partner))
            .Value.Should()
            .ContainSingle(j => j.Id == jarId);
    }

    [Fact]
    public async Task A_non_member_cannot_contribute()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        (SavingsJarService sut, _, _) = New(database);
        Guid jarId = await CreateJarAsync(sut);

        Result<JarMovementDto> result = await sut.ContributeAsync(
            Partner, // no membership
            new JarContributeRequest(jarId, Viewer, 10)
        );

        result.ErrorCode.Should().Be("JAR_MEMBERSHIP_REQUIRED");
    }

    [Fact]
    public async Task Contribute_debits_the_viewer_grows_the_jar_and_fires_events()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        (SavingsJarService sut, EventStoreTestDbContext db, RecordingEventBus bus) = New(database);
        Guid jarId = await CreateJarAsync(sut, goal: 30); // reaching 30 crosses the goal

        Result<JarMovementDto> result = await sut.ContributeAsync(
            Owner,
            new JarContributeRequest(jarId, Viewer, 30)
        );

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.JarBalanceAfter.Should().Be(30);
        (await sut.GetJarAsync(Owner, jarId)).Value.Balance.Should().Be(30);
        (
            await new CurrencyAccountService(
                db,
                new TenantSequenceAllocator(db),
                new EventStoreTestUnitOfWork(db),
                bus,
                Clock
            ).GetBalanceAsync(Owner, Viewer)
        )
            .Value.Should()
            .Be(70); // 100 - 30
        bus.Published.OfType<JarContributedEvent>().Should().ContainSingle();
        bus.Published.OfType<JarGoalReachedEvent>().Should().ContainSingle();
    }

    [Fact]
    public async Task Contribute_to_a_closed_jar_is_rejected()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        (SavingsJarService sut, _, _) = New(database);
        Guid jarId = await CreateJarAsync(sut, open: false);

        Result<JarMovementDto> result = await sut.ContributeAsync(
            Owner,
            new JarContributeRequest(jarId, Viewer, 10)
        );

        result.ErrorCode.Should().Be("JAR_NOT_OPEN");
    }

    [Fact]
    public async Task Withdraw_credits_an_account_and_shrinks_the_jar()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        (SavingsJarService sut, _, _) = New(database);
        Guid jarId = await CreateJarAsync(sut);
        await sut.ContributeAsync(Owner, new JarContributeRequest(jarId, Viewer, 30)); // jar = 30

        Result<JarMovementDto> result = await sut.WithdrawAsync(
            Owner,
            new JarWithdrawRequest(jarId, Viewer, 20, Owner)
        );

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.JarBalanceAfter.Should().Be(10); // 30 - 20
        (await sut.GetJarAsync(Owner, jarId)).Value.Balance.Should().Be(10);
    }

    [Fact]
    public async Task Contribute_enforces_the_per_stream_contribution_sum_cap()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        (SavingsJarService sut, EventStoreTestDbContext db, _) = New(database);
        db.Streams.Add(
            new NomNomzBot.Domain.Stream.Entities.Stream
            {
                Id = "s1",
                ChannelId = Owner,
                StartedAt = new DateTimeOffset(2026, 6, 21, 11, 0, 0, TimeSpan.Zero),
            }
        );
        db.SaveChanges();
        Guid jarId = await CreateJarAsync(sut);
        SavingsJarMembership owner = db.SavingsJarMemberships.Single(m =>
            m.JarId == jarId && m.MemberBroadcasterId == Owner
        );
        owner.ContributionCapPerStream = 50;
        db.SaveChanges();

        (await sut.ContributeAsync(Owner, new JarContributeRequest(jarId, Viewer, 30)))
            .IsSuccess.Should()
            .BeTrue();
        (await sut.ContributeAsync(Owner, new JarContributeRequest(jarId, Viewer, 30)))
            .ErrorCode.Should()
            .Be("JAR_CAP_EXCEEDED"); // 30 already + 30 > 50
    }
}
