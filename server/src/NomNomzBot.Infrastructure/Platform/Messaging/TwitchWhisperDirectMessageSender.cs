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
using NomNomzBot.Application.Contracts.Platform;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Identity.Enums;

namespace NomNomzBot.Infrastructure.Platform.Messaging;

/// <summary>
/// Twitch <see cref="IPlatformDirectMessageSender"/> — wraps the Helix Send Whisper call
/// (<see cref="ITwitchWhispersApi"/>). Registered alongside any future per-platform sender (S065).
/// </summary>
public sealed class TwitchWhisperDirectMessageSender : IPlatformDirectMessageSender
{
    private readonly ITwitchWhispersApi _whispers;

    public TwitchWhisperDirectMessageSender(ITwitchWhispersApi whispers)
    {
        _whispers = whispers;
    }

    public string Provider => AuthEnums.Platform.Twitch;

    public Task<Result> SendAsync(
        Guid broadcasterId,
        string providerUserId,
        string message,
        CancellationToken ct = default
    ) => _whispers.SendWhisperAsync(broadcasterId, providerUserId, message, ct);
}
