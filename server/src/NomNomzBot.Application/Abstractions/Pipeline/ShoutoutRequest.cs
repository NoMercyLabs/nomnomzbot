// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Contracts.Twitch;

namespace NomNomzBot.Application.Abstractions.Pipeline;

/// <summary>One shoutout run: the native Helix call (unless skipped), the resolved announcement and the TTS.</summary>
public sealed record ShoutoutRequest(
    Guid BroadcasterId,
    TwitchUser Target,
    string Announcement,
    bool Speak,
    bool SkipNativeCall
);
