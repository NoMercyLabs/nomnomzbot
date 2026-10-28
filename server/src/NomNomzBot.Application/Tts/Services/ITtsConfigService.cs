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

    /// <summary>
    /// The defaults a channel's settings return to on <see cref="ResetConfigAsync"/> — the same values a
    /// channel with no row reads as. Lets the dashboard show "current → default" before a reset writes.
    /// </summary>
    Task<Result<TtsConfigDto>> GetDefaultConfigAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Put the channel's TTS settings (enabled, mode, provider, max characters, minimum permission, skip-bot,
    /// read-usernames, profanity censor, mod approval, bits gate, viewer voice self-service) back to the
    /// defaults and return the resulting config. Never touches the BYOK keys, the default voice, the
    /// pronunciation lexicon, or per-viewer voice assignments. A channel with no row is already at the
    /// defaults — nothing is written.
    /// </summary>
    Task<Result<TtsConfigDto>> ResetConfigAsync(
        Guid broadcasterId,
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

    /// <summary>
    /// Bulk-import per-viewer voice assignments (≤500 rows) — the migration surface for another bot's
    /// user→voice table. Each row upserts the viewer's assignment; a Twitch user the bot has never seen or a
    /// voice not in the catalogue is reported back as skipped (with a reason), never an error. With
    /// <paramref name="createMissing"/>, an unknown Twitch user whose row carries a username is created as a
    /// bare viewer User first (the chat-ingest get-or-create seam) — these are real legacy-bot viewers who
    /// simply have not chatted yet; without it (the default) NO user rows are ever created.
    /// </summary>
    Task<Result<TtsVoiceImportResultDto>> ImportUserVoiceAssignmentsAsync(
        Guid broadcasterId,
        IReadOnlyList<TtsVoiceAssignmentRowDto> rows,
        bool createMissing = false,
        CancellationToken cancellationToken = default
    );

    /// <summary>Remove a viewer's voice assignment so they fall back to the channel default.</summary>
    Task<Result> ClearUserVoiceAsync(
        Guid broadcasterId,
        string userId,
        CancellationToken cancellationToken = default
    );

    // ── Viewer self-service (the !voice command) ────────────────────────────────────────────────────────
    // The caller acts on their OWN voice, keyed by their platform user id (what the dispatch resolver reads).
    // Gated on the channel's TTS being enabled AND ViewerVoiceSelfServiceEnabled — FEATURE_DISABLED otherwise.
    // A moderator can still override any viewer via SetUserVoiceAsync regardless of this toggle.

    /// <summary>The caller picks their own voice (validated against the catalogue). Toggle-gated.</summary>
    Task<Result<UserTtsVoiceDto>> SetOwnVoiceAsync(
        Guid broadcasterId,
        string viewerUserId,
        SetUserVoiceDto request,
        CancellationToken cancellationToken = default
    );

    /// <summary>The caller's own assigned voice, or null when they use the channel default.</summary>
    Task<Result<UserTtsVoiceDto?>> GetOwnVoiceAsync(
        Guid broadcasterId,
        string viewerUserId,
        CancellationToken cancellationToken = default
    );

    /// <summary>The caller resets their own voice back to the channel default. Toggle-gated.</summary>
    Task<Result> ClearOwnVoiceAsync(
        Guid broadcasterId,
        string viewerUserId,
        CancellationToken cancellationToken = default
    );
}
