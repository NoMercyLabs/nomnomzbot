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
    /// <summary>The Twitch user id (a number as text) of the user.</summary>
    public required string UserId { get; init; }

    /// <summary>The login name of the user, in lowercase.</summary>
    public required string UserLogin { get; init; }

    /// <summary>The name of the user as shown in chat.</summary>
    public required string UserDisplayName { get; init; }

    /// <summary>The user's email, or <c>null</c> when the subscription lacks the <c>user:read:email</c> scope.</summary>
    public string? Email { get; init; }

    /// <summary>The profile description of the user.</summary>
    public required string Description { get; init; }
}
