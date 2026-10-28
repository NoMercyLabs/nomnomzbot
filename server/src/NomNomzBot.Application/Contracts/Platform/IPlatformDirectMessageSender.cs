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
