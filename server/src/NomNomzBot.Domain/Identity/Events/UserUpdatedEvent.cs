// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Identity.Events;

/// <summary>
/// Published when an authorized user updates their Twitch account (EventSub <c>user.update</c>). This is a
/// user-scoped event — the tenant is the user, not a channel — so the publisher sets <c>BroadcasterId</c> to the
/// dispatcher-resolved tenant (may be the platform sentinel for a pure user flow). <see cref="Email"/> is present
/// only when the subscription holds the <c>user:read:email</c> scope; otherwise it is absent.
/// </summary>
public sealed class UserUpdatedEvent : DomainEventBase
{
    public required string UserId { get; init; }
    public required string UserLogin { get; init; }
    public required string UserDisplayName { get; init; }

    /// <summary>The user's email, or <c>null</c> when the subscription lacks the <c>user:read:email</c> scope.</summary>
    public string? Email { get; init; }
    public required string Description { get; init; }
}
