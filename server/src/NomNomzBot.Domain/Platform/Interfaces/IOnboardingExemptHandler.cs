// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Domain.Platform.Interfaces;

/// <summary>
/// Marker for an event handler that only RECORDS what happened (stores a chat line, notes a viewer, feeds the
/// live dashboard) and never acts outward: no reply, command, automation, timer, TTS, shoutout or moderation.
/// The event bus runs ONLY these handlers for a channel-ingress event of a channel whose broadcaster has not
/// onboarded (<c>Channels.IsOnboarded</c> false), so the bot stays silent there while moderators still see chat.
/// </summary>
public interface IOnboardingExemptHandler;
