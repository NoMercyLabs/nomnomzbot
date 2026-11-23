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

using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Commands.ValueObjects;

namespace NomNomzBot.Application.Abstractions.Pipeline;

/// <summary>
/// One complete shoutout run: native Helix, then either the resolved announcement with optional TTS, or the
/// <paramref name="Response"/> the channel chose to run instead of posting text.
/// </summary>
public sealed record ShoutoutRequest(
    Guid BroadcasterId,
    TwitchUser Target,
    string Announcement,
    bool Speak,
    bool SkipNative = false,
    string? DeliveryKey = null,
    BindingTarget? Response = null,
    IReadOnlyDictionary<string, string>? ResponseVariables = null
);
