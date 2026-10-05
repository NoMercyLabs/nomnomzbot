// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Api.Hubs;
using NomNomzBot.Api.Hubs.Broadcasters;
using NomNomzBot.Api.Hubs.Dtos;
using NomNomzBot.Api.Tests.Controllers;
using NomNomzBot.Domain.Economy.Entities;
using NomNomzBot.Domain.Economy.Enums;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Hubs;

/// <summary>
/// Proves a savings-jar change reaches every channel in the jar group (the owner plus each Accepted member),
/// not only the channel named on the event, and never a Pending invitee or an unrelated channel.
/// </summary>
public sealed class SavingsJarBroadcastHandlersTests
{
    private sealed class Jar : IAsyncDisposable
    {
        public ApiTestDbContext Db { get; } = ApiTestDbContext.New();
        public IDashboardNotifier Notifier { get; } = Substitute.For<IDashboardNotifier>();
        public Guid JarId { get; } = Guid.CreateVersion7();
        public Guid Owner { get; } = Guid.CreateVersion7();
        public Guid Member { get; } = Guid.CreateVersion7();
        public Guid OtherMember { get; } = Guid.CreateVersion7();
        public Guid Pending { get; } = Guid.CreateVersion7();
        public Guid Unrelated { get; } = Guid.CreateVersion7();

        public static async Task<Jar> SeedAsync()
        {
            Jar jar = new();
            jar.Db.SavingsJars.Add(
                new()
                {
                    Id = jar.JarId,
                    OwnerBroadcasterId = jar.Owner,
                    Name = "Shared",
                }
            );
            jar.Db.SavingsJarMemberships.AddRange(
                Membership(jar.JarId, jar.Member, JarMembershipStatus.Accepted),
                Membership(jar.JarId, jar.OtherMember, JarMembershipStatus.Accepted),
                Membership(jar.JarId, jar.Pending, JarMembershipStatus.Pending)
            );
            await jar.Db.SaveChangesAsync();
            return jar;
        }

        private static SavingsJarMembership Membership(
            Guid jarId,
            Guid channel,
            JarMembershipStatus status
        ) =>
            new()
            {
                JarId = jarId,
                MemberBroadcasterId = channel,
                Status = status,
                Role = JarRole.Partner,
            };

        /// <summary>Asserts exactly these channels got one savings-jar push each, and nobody else.</summary>
        public async Task AssertPushedToAsync(string action, params Guid[] channels)
        {
            foreach (Guid channel in channels)
                await Notifier
                    .Received(1)
                    .SendConfigChangedAsync(
                        channel.ToString(),
                        Arg.Is<ConfigChangedDto>(dto =>
                            dto.BroadcasterId == channel.ToString()
                            && dto.Domain == "savings-jar"
                            && dto.EntityId == JarId.ToString()
                            && dto.Action == action
                        ),
                        Arg.Any<CancellationToken>()
                    );
            await Notifier
                .Received(channels.Length)
                .SendConfigChangedAsync(
                    Arg.Any<string>(),
                    Arg.Any<ConfigChangedDto>(),
                    Arg.Any<CancellationToken>()
                );
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    [Fact]
    public async Task A_contribution_from_a_member_channel_reaches_the_owner_and_every_accepted_member()
    {
        await using Jar jar = await Jar.SeedAsync();

        await new JarContributedBroadcastHandler(jar.Db, jar.Notifier).HandleAsync(
            new()
            {
                BroadcasterId = jar.Member,
                JarId = jar.JarId,
                SourceBroadcasterId = jar.Member,
                ContributorUserId = Guid.CreateVersion7(),
                Amount = 50,
                JarBalanceAfter = 150,
                ContributionId = 1,
            }
        );

        await jar.AssertPushedToAsync("contributed", jar.Owner, jar.Member, jar.OtherMember);
    }

    [Fact]
    public async Task A_withdrawal_reaches_the_owner_and_every_accepted_member()
    {
        await using Jar jar = await Jar.SeedAsync();

        await new JarWithdrawnBroadcastHandler(jar.Db, jar.Notifier).HandleAsync(
            new()
            {
                BroadcasterId = jar.Member,
                JarId = jar.JarId,
                SourceBroadcasterId = jar.Member,
                ActorUserId = Guid.CreateVersion7(),
                Amount = 20,
                JarBalanceAfter = 80,
                ContributionId = 2,
            }
        );

        await jar.AssertPushedToAsync("withdrawn", jar.Owner, jar.Member, jar.OtherMember);
    }

    [Fact]
    public async Task Reaching_the_goal_reaches_the_owner_and_every_accepted_member()
    {
        await using Jar jar = await Jar.SeedAsync();

        await new JarGoalReachedBroadcastHandler(jar.Db, jar.Notifier).HandleAsync(
            new()
            {
                BroadcasterId = jar.Member,
                JarId = jar.JarId,
                GoalAmount = 100,
                Balance = 100,
            }
        );

        await jar.AssertPushedToAsync("goal_reached", jar.Owner, jar.Member, jar.OtherMember);
    }

    [Fact]
    public async Task A_membership_change_reaches_the_group_and_the_channel_whose_status_changed()
    {
        await using Jar jar = await Jar.SeedAsync();

        await new SavingsJarMembershipChangedBroadcastHandler(jar.Db, jar.Notifier).HandleAsync(
            new()
            {
                BroadcasterId = jar.Owner,
                JarId = jar.JarId,
                MemberBroadcasterId = jar.Pending,
                Status = "Revoked",
            }
        );

        await jar.AssertPushedToAsync(
            "membership_changed",
            jar.Owner,
            jar.Member,
            jar.OtherMember,
            jar.Pending
        );
    }

    [Fact]
    public async Task An_invite_reaches_the_group_and_the_invited_channel()
    {
        await using Jar jar = await Jar.SeedAsync();

        await new SavingsJarInviteSentBroadcastHandler(jar.Db, jar.Notifier).HandleAsync(
            new()
            {
                BroadcasterId = jar.Owner,
                JarId = jar.JarId,
                OwnerBroadcasterId = jar.Owner,
                InvitedBroadcasterId = jar.Pending,
                Role = "Partner",
            }
        );

        await jar.AssertPushedToAsync(
            "invite_sent",
            jar.Owner,
            jar.Member,
            jar.OtherMember,
            jar.Pending
        );
    }
}
