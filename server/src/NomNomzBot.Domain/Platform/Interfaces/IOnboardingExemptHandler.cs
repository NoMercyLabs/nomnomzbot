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

namespace NomNomzBot.Domain.Platform.Interfaces;

/// <summary>
/// Marker for an event handler that only RECORDS what happened (stores a chat line, notes a viewer, feeds the
/// live dashboard) and never acts outward: no reply, command, automation, timer, TTS, shoutout or moderation.
/// The event bus runs ONLY these handlers for a channel-ingress event of a channel whose broadcaster has not
/// onboarded (<c>Channels.IsOnboarded</c> false), so the bot stays silent there while moderators still see chat.
/// </summary>
public interface IOnboardingExemptHandler;
