// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Reflection;

namespace NomNomzBot.Domain.Platform;

/// <summary>
/// Resolves the real, MAPPED tenant column of an <see cref="ITenantScoped"/> entity.
/// <para>
/// Most entities implement <see cref="ITenantScoped.BroadcasterId"/> as a public auto-property, but a few
/// implement it EXPLICITLY over a differently-named public key (<c>ChannelModerator</c> → <c>ChannelId</c>).
/// An expression built against the interface member is not a mapped property, so EF cannot translate it —
/// every tenant predicate must bind to the concrete public property instead. Both the global query filter and
/// the delete-preview counters resolve it here, so the two can never disagree about which column is the
/// tenant.
/// </para>
/// <para>
/// The lookup is STRUCTURAL, not interface-gated: a handful of entities carry a real <c>BroadcasterId</c>
/// column without declaring <see cref="ITenantScoped"/> (auth sessions, crypto keys, the event journal).
/// They still belong to exactly one channel and still die with it, so the delete preview must be able to
/// count them. Callers that need the interface's guarantee (the global query filter does) check it
/// themselves.
/// </para>
/// </summary>
public static class TenantKey
{
    /// <summary>
    /// The entity's mapped tenant <see cref="Guid"/> property, or <c>null</c> when the type has no mappable
    /// tenant column (the caller then omits the tenant predicate rather than guessing one).
    /// </summary>
    public static PropertyInfo? ResolveProperty(Type entityType)
    {
        PropertyInfo? property =
            entityType.GetProperty(nameof(ITenantScoped.BroadcasterId))
            ?? entityType.GetProperty("OwnerBroadcasterId")
            ?? entityType.GetProperty("MemberBroadcasterId")
            ?? entityType.GetProperty("ChannelId");

        if (property is null)
            return null;

        // Guid? is a real tenant column too: rows that MAY be platform-global (auth sessions, integration
        // connections, the event journal) still belong to exactly one channel whenever the value is set.
        return property.PropertyType == typeof(Guid) || property.PropertyType == typeof(Guid?)
            ? property
            : null;
    }
}
