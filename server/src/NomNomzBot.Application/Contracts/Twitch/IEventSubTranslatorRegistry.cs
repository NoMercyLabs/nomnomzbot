// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;

namespace NomNomzBot.Application.Contracts.Twitch;

/// <summary>
/// Resolves the <see cref="IEventSubEventTranslator"/> that owns a given Twitch subscription type
/// (twitch-eventsub §3.7). Built once from every auto-discovered translator and keyed by
/// <see cref="IEventSubEventTranslator.SubscriptionType"/>; an unknown type yields <c>false</c> so the
/// dispatcher journals it without a typed fan-out (no event is ever lost, even before its translator exists).
/// </summary>
public interface IEventSubTranslatorRegistry
{
    /// <summary>Looks up the translator for <paramref name="subscriptionType"/>; <c>false</c> when none is registered.</summary>
    bool TryGet(
        string subscriptionType,
        [NotNullWhen(true)] out IEventSubEventTranslator? translator
    );
}
