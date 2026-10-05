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

/// <summary>
/// Sends one shoutout in full: the native Helix call (unless skipped), the templated chat announcement and
/// the optional TTS, then stamps the channel's shoutout cooldowns. The one send path for a direct run of the
/// shoutout step and for a run released from the <see cref="IShoutoutQueue"/>.
/// </summary>
public interface IShoutoutSender
{
    /// <summary>
    /// Picks the announcement template for a target: the step's own override, else the broadcaster's note for
    /// this person, else the target's own template, else the broadcaster's template, else the default.
    /// </summary>
    Task<string> SelectTemplateAsync(
        Guid broadcasterId,
        TwitchUser target,
        string templateOverride,
        CancellationToken cancellationToken
    );

    Task<ActionResult> SendAsync(ShoutoutRequest request, CancellationToken cancellationToken);
}
