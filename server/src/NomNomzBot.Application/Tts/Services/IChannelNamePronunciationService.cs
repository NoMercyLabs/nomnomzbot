// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Tts.Dtos;

namespace NomNomzBot.Application.Tts.Services;

/// <summary>
/// The spoken form of channel names. A channel that sets <c>Channel.UsernamePronunciation</c> wants its login
/// read that way by TTS ("xX_JD_Xx" as "Jaydee") wherever the name appears in an utterance, not only in its own
/// channel — the lexicon applies these after the channel's own rules.
/// </summary>
public interface IChannelNamePronunciationService
{
    /// <summary>Every channel that has a pronunciation set, as name and spoken form.</summary>
    Task<IReadOnlyList<ChannelNamePronunciation>> ListAsync(
        CancellationToken cancellationToken = default
    );

    /// <summary>The channel's own name and its pronunciation (null when unset).</summary>
    Task<Result<ChannelNamePronunciationDto>> GetAsync(
        Guid channelId,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Sets how TTS says the channel's name. The value is trimmed; null or blank clears it. A value over
    /// <see cref="MaxLength"/> characters or with a control character is refused and nothing is written.
    /// </summary>
    Task<Result<ChannelNamePronunciationDto>> SetAsync(
        Guid channelId,
        string? pronunciation,
        CancellationToken cancellationToken = default
    );

    /// <summary>The longest pronunciation a channel can store (matches the column).</summary>
    const int MaxLength = 100;
}
