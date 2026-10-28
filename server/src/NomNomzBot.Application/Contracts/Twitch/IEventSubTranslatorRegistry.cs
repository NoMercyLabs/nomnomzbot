// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
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
