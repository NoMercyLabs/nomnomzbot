// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Chat.Dtos;
using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Application.Chat.Services;

/// <summary>The channel owner's own chat identity on Twitch: name color and worn badges.</summary>
public interface IChatIdentityService
{
    /// <summary>Error code when Twitch refuses a hex color because the user has no Turbo or Prime.</summary>
    public const string HexNeedsTurboCode = "CHAT_COLOR_NEEDS_TURBO";

    Task<Result<ChatIdentityDto>> GetAsync(Guid broadcasterId, CancellationToken ct = default);

    /// <summary>
    /// Sets the owner's chat color. Fails with <c>VALIDATION_FAILED</c> for a value that is neither a named
    /// color nor a 6-digit hex, <see cref="HexNeedsTurboCode"/> when Twitch refuses a hex value, and
    /// <c>missing_scope</c> when the connection lacks <c>user:manage:chat_color</c>.
    /// </summary>
    Task<Result<ChatIdentityDto>> SetColorAsync(
        Guid broadcasterId,
        string color,
        CancellationToken ct = default
    );
}
