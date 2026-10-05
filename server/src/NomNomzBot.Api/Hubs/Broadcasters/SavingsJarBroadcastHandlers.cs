// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Domain.Economy.Enums;
using NomNomzBot.Domain.Economy.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Api.Hubs.Broadcasters;

/// <summary>
/// Pushes one generic <c>savings-jar</c> config change to every channel in a jar group: the owner channel plus
/// each Accepted member. The event's own <c>BroadcasterId</c> is the acting channel, which may be a member, so
/// it is never enough on its own. A jar is cross-tenant, so every open dashboard of the group must refetch.
/// </summary>
internal static class SavingsJarGroupPush
{
    public const string Domain = "savings-jar";

    public static async Task SendAsync(
        IApplicationDbContext db,
        IDashboardNotifier notifier,
        Guid jarId,
        string action,
        IReadOnlyCollection<Guid> extraChannels,
        CancellationToken ct
    )
    {
        HashSet<Guid> channels = [.. extraChannels];

        Guid? owner = await db
            .SavingsJars.Where(j => j.Id == jarId)
            .Select(j => (Guid?)j.OwnerBroadcasterId)
            .FirstOrDefaultAsync(ct);
        if (owner is not null)
            channels.Add(owner.Value);

        List<Guid> members = await db
            .SavingsJarMemberships.Where(m =>
                m.JarId == jarId && m.Status == JarMembershipStatus.Accepted
            )
            .Select(m => m.MemberBroadcasterId)
            .ToListAsync(ct);
        channels.UnionWith(members);

        foreach (Guid channel in channels)
        {
            if (channel == Guid.Empty)
                continue;

            string id = channel.ToString();
            await notifier.SendConfigChangedAsync(
                id,
                new(id, Domain, jarId.ToString(), action),
                ct
            );
        }
    }
}

/// <summary>A contribution reaches the whole jar group so every balance on screen reloads.</summary>
public sealed class JarContributedBroadcastHandler(
    IApplicationDbContext db,
    IDashboardNotifier notifier
) : IEventHandler<JarContributedEvent>
{
    public Task HandleAsync(JarContributedEvent @event, CancellationToken ct = default) =>
        SavingsJarGroupPush.SendAsync(db, notifier, @event.JarId, "contributed", [], ct);
}

/// <summary>A withdrawal reaches the whole jar group.</summary>
public sealed class JarWithdrawnBroadcastHandler(
    IApplicationDbContext db,
    IDashboardNotifier notifier
) : IEventHandler<JarWithdrawnEvent>
{
    public Task HandleAsync(JarWithdrawnEvent @event, CancellationToken ct = default) =>
        SavingsJarGroupPush.SendAsync(db, notifier, @event.JarId, "withdrawn", [], ct);
}

/// <summary>Reaching the goal reaches the whole jar group.</summary>
public sealed class JarGoalReachedBroadcastHandler(
    IApplicationDbContext db,
    IDashboardNotifier notifier
) : IEventHandler<JarGoalReachedEvent>
{
    public Task HandleAsync(JarGoalReachedEvent @event, CancellationToken ct = default) =>
        SavingsJarGroupPush.SendAsync(db, notifier, @event.JarId, "goal_reached", [], ct);
}

/// <summary>
/// A membership change reaches the group and the channel whose status changed, which may have just left it
/// (declined or revoked) and so is no longer an Accepted member.
/// </summary>
public sealed class SavingsJarMembershipChangedBroadcastHandler(
    IApplicationDbContext db,
    IDashboardNotifier notifier
) : IEventHandler<SavingsJarMembershipChangedEvent>
{
    public Task HandleAsync(
        SavingsJarMembershipChangedEvent @event,
        CancellationToken ct = default
    ) =>
        SavingsJarGroupPush.SendAsync(
            db,
            notifier,
            @event.JarId,
            "membership_changed",
            [@event.MemberBroadcasterId],
            ct
        );
}

/// <summary>An invite reaches the group and the invited channel, so the invite shows up at once.</summary>
public sealed class SavingsJarInviteSentBroadcastHandler(
    IApplicationDbContext db,
    IDashboardNotifier notifier
) : IEventHandler<SavingsJarInviteSentEvent>
{
    public Task HandleAsync(SavingsJarInviteSentEvent @event, CancellationToken ct = default) =>
        SavingsJarGroupPush.SendAsync(
            db,
            notifier,
            @event.JarId,
            "invite_sent",
            [@event.InvitedBroadcasterId],
            ct
        );
}
