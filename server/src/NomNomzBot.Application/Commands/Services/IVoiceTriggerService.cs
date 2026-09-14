// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Application.Commands.Services;

/// <summary>
/// CRUD over the channel's voice triggers (spoken-word counters + overlay stickers), plus the report path the
/// voice-listener page calls when Chrome's <c>webkitSpeechRecognition</c> hears a defined word. A report scans
/// every enabled trigger for a case-insensitive substring match against the transcript, applies the per-trigger
/// cooldown, increments the live count, and publishes <see cref="Domain.Commands.Events.VoiceTriggerFiredEvent"/>
/// so the overlay sticker widget updates over the existing OverlayHub push.
/// </summary>
public interface IVoiceTriggerService
{
    Task<Result<IReadOnlyList<VoiceTriggerDto>>> ListAsync(
        string broadcasterId,
        CancellationToken cancellationToken = default
    );

    Task<Result<VoiceTriggerDto>> CreateAsync(
        string broadcasterId,
        CreateVoiceTriggerRequest request,
        CancellationToken cancellationToken = default
    );

    Task<Result<VoiceTriggerDto>> UpdateAsync(
        string broadcasterId,
        Guid triggerId,
        UpdateVoiceTriggerRequest request,
        CancellationToken cancellationToken = default
    );

    Task<Result> DeleteAsync(
        string broadcasterId,
        Guid triggerId,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// The listener page's report call. Matches every enabled trigger against <paramref name="transcript"/>;
    /// a match still inside its cooldown window is silently skipped (not an error — the listener keeps
    /// running). Only the FIRST matching trigger fires per report (one utterance, one sticker) — a transcript
    /// that happens to contain two different trigger words at once is the rare case, and firing every match
    /// at once would spam the overlay for something that sounds like one moment to the streamer.
    /// </summary>
    Task<Result<VoiceTriggerReportResultDto>> ReportAsync(
        Guid broadcasterId,
        string transcript,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// The channel's overlay token (the SAME credential OBS browser sources and the overlay SDK already use) —
    /// what the dashboard's "copy your listener link" button embeds in the <c>/voice-listener?token=</c> URL.
    /// No new token type: the listener page is conceptually just another overlay client.
    /// </summary>
    Task<Result<string>> GetOverlayTokenAsync(
        string broadcasterId,
        CancellationToken cancellationToken = default
    );
}
