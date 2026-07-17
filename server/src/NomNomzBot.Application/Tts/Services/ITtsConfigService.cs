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
/// Manages per-channel TTS configuration and voice enumeration.
/// </summary>
public interface ITtsConfigService
{
    Task<Result<TtsConfigDto>> GetConfigAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken = default
    );
    Task<Result<TtsConfigDto>> UpdateConfigAsync(
        Guid broadcasterId,
        UpdateTtsConfigDto request,
        CancellationToken cancellationToken = default
    );

    /// <summary>Vault-encrypt and store a BYOK provider API key (azure | elevenlabs) on the channel's config.</summary>
    Task<Result<TtsConfigDto>> SetByokKeyAsync(
        Guid broadcasterId,
        string provider,
        SetTtsByokKeyDto request,
        CancellationToken cancellationToken = default
    );

    /// <summary>Remove a stored BYOK key so the provider falls back to the operator/app configuration.</summary>
    Task<Result<TtsConfigDto>> ClearByokKeyAsync(
        Guid broadcasterId,
        string provider,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Search the global voice catalogue: free-text <c>Q</c> matches name/display-name/description/tags,
    /// the rest are equality filters, ordered provider→locale→name and paged. Backs the dashboard voice
    /// picker and the <c>!voice</c> command's fuzzy match. Falls back to live provider enumeration only when
    /// the catalogue table is empty (pre-sync).
    /// </summary>
    Task<Result<PagedList<TtsVoiceDto>>> SearchVoicesAsync(
        TtsVoiceQuery query,
        CancellationToken cancellationToken = default
    );
    Task<Result<TtsTestResultDto>> TestVoiceAsync(
        Guid broadcasterId,
        TtsTestRequestDto request,
        CancellationToken cancellationToken = default
    );

    /// <summary>Get a viewer's assigned voice for the channel, or NOT_FOUND when they use the channel default.</summary>
    Task<Result<UserTtsVoiceDto>> GetUserVoiceAsync(
        Guid broadcasterId,
        string userId,
        CancellationToken cancellationToken = default
    );

    /// <summary>Assign (or reassign) a viewer's voice; validated against the channel's synthesizable voices.</summary>
    Task<Result<UserTtsVoiceDto>> SetUserVoiceAsync(
        Guid broadcasterId,
        string userId,
        SetUserVoiceDto request,
        CancellationToken cancellationToken = default
    );

    /// <summary>Remove a viewer's voice assignment so they fall back to the channel default.</summary>
    Task<Result> ClearUserVoiceAsync(
        Guid broadcasterId,
        string userId,
        CancellationToken cancellationToken = default
    );
}
