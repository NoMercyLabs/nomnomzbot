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
