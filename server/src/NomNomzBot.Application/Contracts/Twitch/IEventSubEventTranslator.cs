// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.DTOs.Twitch.EventSub;

namespace NomNomzBot.Application.Contracts.Twitch;

/// <summary>
/// Translates one Twitch EventSub subscription type's raw event payload into the strongly-typed domain
/// event(s) it represents and publishes them on the bus (twitch-eventsub §3.7). Exactly one implementation
/// per <see cref="SubscriptionType"/>; <see cref="INotificationDispatcher"/> resolves the matching translator
/// from <see cref="IEventSubTranslatorRegistry"/> after the notification is journaled and invokes it on the
/// genuinely-new path only (a redelivery already fanned out the first time).
/// <para>
/// A translator publishes its <em>concrete</em> domain event type so the bus resolves
/// <c>IEventHandler&lt;TConcrete&gt;</c> correctly — the bus binds handlers by the compile-time event type, so a
/// translator that knows its concrete type at the publish call site is the seam that turns a raw envelope into
/// typed delivery without reflection.
/// </para>
/// </summary>
public interface IEventSubEventTranslator
{
    /// <summary>The Twitch subscription type this translator owns (e.g. <c>"channel.follow"</c>).</summary>
    string SubscriptionType { get; }

    /// <summary>Parse the notification's raw event payload and publish the matching domain event(s).</summary>
    Task TranslateAsync(EventSubNotification notification, CancellationToken ct = default);
}
