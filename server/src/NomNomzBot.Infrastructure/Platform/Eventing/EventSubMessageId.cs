// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Infrastructure.Platform.Eventing;

/// <summary>
/// Derives the journal <c>EventId</c> deterministically from a Twitch EventSub message-id (twitch-eventsub
/// §9 decision 1): a name-based UUIDv5 (SHA-1), namespace-scoped to <c>eventsub</c>. The same wire message
/// always maps to the same <see cref="Guid"/>, so the journal's <c>Unique(EventId)</c> and the
/// <c>IdempotencyKey(Scope="eventsub", Key=message-id)</c> agree and replays are exact.
/// </summary>
public static class EventSubMessageId
{
    // A fixed, project-owned namespace UUID for the "eventsub" message-id space. Any stable v4 value works;
    // this one is constant so the derivation is reproducible across processes and deployments.
    private static readonly Guid EventSubNamespace = Guid.Parse(
        "8b1f0d3e-2a6c-4f1b-9c2d-6e5a4b3c2d1e"
    );

    /// <summary>The deterministic v5 GUID for a Twitch message-id. Idempotent: same input ⇒ same output.</summary>
    public static Guid ForMessageId(string messageId) =>
        NameBasedGuid.Version5(EventSubNamespace, messageId);
}
