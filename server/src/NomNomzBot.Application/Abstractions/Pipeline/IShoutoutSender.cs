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

namespace NomNomzBot.Application.Abstractions.Pipeline;

/// <summary>
/// Sends one shoutout in full: the native Helix call, then the templated chat announcement and
/// the optional TTS, then stamps the channel's shoutout cooldowns. The one send path for a direct run of the
/// shoutout step and for a run released from the <see cref="IShoutoutQueue"/>.
/// </summary>
public interface IShoutoutSender
{
    /// <summary>
    /// Picks the announcement template for a target: the step's own override, else the broadcaster's note for
    /// this person, else the target's own template, else the broadcaster's template. When none is set the
    /// selection carries no template and the shouting channel's tone, so the caller composes the default line.
    /// </summary>
    Task<ShoutoutTemplateSelection> SelectTemplateAsync(
        Guid broadcasterId,
        TwitchUser target,
        string templateOverride,
        CancellationToken cancellationToken
    );

    Task<ActionResult> SendAsync(ShoutoutRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// The announcement template a shoutout picked, or <see langword="null"/> when no custom template is set, plus
/// the shouting channel's personality tone for composing the default line.
/// </summary>
public sealed record ShoutoutTemplateSelection(string? Template, string Personality);
