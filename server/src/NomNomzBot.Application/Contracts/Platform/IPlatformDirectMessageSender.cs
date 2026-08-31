// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Application.Contracts.Platform;

/// <summary>
/// Per-platform private/direct-message sender (S065) — mirrors the <c>IChatPlatform</c> multi-bind
/// pattern: one implementation per platform, each declaring the <see cref="Provider"/> it serves
/// (<see cref="NomNomzBot.Domain.Identity.Enums.AuthEnums.Platform"/> string set), multi-bound in DI and
/// resolved by a caller that knows the target's platform (e.g. a giveaway winner's <c>Provider</c>).
/// There is no router service — callers with a small, stable set of senders (like
/// <c>GiveawayFulfillment</c>) resolve directly from the injected set; a router is warranted only once a
/// second caller needs the same resolution logic (Rule of Three).
/// </summary>
public interface IPlatformDirectMessageSender
{
    /// <summary>The platform this sender serves (<c>AuthEnums.Platform</c> string set).</summary>
    string Provider { get; }

    /// <summary>
    /// Sends a private/direct message from the tenant to <paramref name="providerUserId"/> (the
    /// recipient's native id on <see cref="Provider"/>). Returns <see cref="Result"/> — never throws for
    /// an expected send failure, so a swallowed failure can never masquerade as a successful send.
    /// </summary>
    Task<Result> SendAsync(
        Guid broadcasterId,
        string providerUserId,
        string message,
        CancellationToken ct = default
    );
}
