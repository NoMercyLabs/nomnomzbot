// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.PlatformDefaults.Dtos;

/// <summary>
/// One event type's platform default, as the admin editor shows it: whether following channels respond and
/// with which chat message, the template variables the event fills (for the editor hint), and how many
/// channels follow the default versus answer with their own saved response.
/// </summary>
public sealed record EventResponseDefaultDto(
    string EventType,
    bool IsEnabled,
    string? Message,
    IReadOnlyList<string> Variables,
    int ChannelsFollowing,
    int ChannelsWithOwnResponse,
    DateTime UpdatedAt,
    bool SpeakWithTts
);

/// <summary>
/// A proposed platform default for one event type — the body of both the preview and the save.
/// <paramref name="SpeakWithTts"/>: following channels also speak the message through their TTS.
/// </summary>
public sealed record EventResponseDefaultChange(
    bool IsEnabled,
    string? Message,
    bool SpeakWithTts = false
);

/// <summary>
/// Saves a platform default. <paramref name="ConfirmedChannelsAffected"/> must equal the preview's live count.
/// </summary>
public sealed record SetEventResponseDefaultRequest(
    bool IsEnabled,
    string? Message,
    int ConfirmedChannelsAffected,
    bool SpeakWithTts = false
);
