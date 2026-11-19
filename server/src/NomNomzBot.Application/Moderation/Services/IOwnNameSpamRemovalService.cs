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
using NomNomzBot.Application.Moderation.Dtos;

namespace NomNomzBot.Application.Moderation.Services;

/// <summary>
/// A moderator's own yes for a channel that did not onboard (Stoney, 2026-11-19: "Your own yes is enough").
/// While it is on and the moderator reads that channel's chat, spam defence deletes, times out and bans in the
/// moderator's own name. It is off until the moderator turns it on, and it is theirs alone: nobody sets it for
/// someone else.
/// </summary>
public interface IOwnNameSpamRemovalService
{
    /// <summary>The moderator's switch for the channel. Off, and not applying, when they hold no role there.</summary>
    Task<OwnNameSpamRemovalDto> GetAsync(
        Guid broadcasterId,
        Guid moderatorUserId,
        CancellationToken ct = default
    );

    /// <summary>
    /// Turns the moderator's switch on or off. Fails with <c>FORBIDDEN</c> when they hold no management role on
    /// the channel, and with <c>VALIDATION_FAILED</c> when the channel onboarded (its owner's access acts there).
    /// </summary>
    Task<Result<OwnNameSpamRemovalDto>> SetAsync(
        Guid broadcasterId,
        Guid moderatorUserId,
        bool enabled,
        CancellationToken ct = default
    );
}
