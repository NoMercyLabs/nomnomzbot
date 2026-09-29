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
/// One built-in response slot as the admin editor shows it. <paramref name="ShippedTemplate"/> is the wording a
/// channel on the default tone gets out of the box (null = the built-in's own fallback);
/// <paramref name="PlatformTemplate"/> is the admin's replacement (null = none), which wins over every tone.
/// <paramref name="ChannelsWithOwnReply"/> is how many channels have their own text for this exact slot (every slot
/// takes a channel's own text since commands-pipelines.md §11) — those channels keep it when the platform changes.
/// </summary>
public sealed record BuiltinReplyDefaultDto(
    string BuiltinKey,
    string Slot,
    string? ShippedTemplate,
    string? PlatformTemplate,
    int ChannelsWithOwnReply
);

/// <summary>A proposed reply text (null = back to the shipped wording) — the preview body.</summary>
public sealed record BuiltinReplyDefaultChange(string? Template);

/// <summary>Saves a reply text. <paramref name="ConfirmedChannelsAffected"/> must equal the preview's live count.</summary>
public sealed record SetBuiltinReplyDefaultRequest(string? Template, int ConfirmedChannelsAffected);
