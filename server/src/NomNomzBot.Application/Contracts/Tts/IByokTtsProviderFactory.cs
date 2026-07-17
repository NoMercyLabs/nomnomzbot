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
using NomNomzBot.Domain.Tts.Interfaces;

namespace NomNomzBot.Application.Contracts.Tts;

/// <summary>
/// Builds a channel's effective TTS provider (tts.md §3.2). BYOK keys are per-channel and encrypted, so
/// the Azure/ElevenLabs adapters cannot be plain singletons keyed off global config — in byok mode they
/// are constructed per request from the channel's vault-decrypted key.
/// </summary>
public interface IByokTtsProviderFactory
{
    /// <summary>
    /// Builds the provider for <paramref name="provider"/> (<c>edge</c> | <c>azure</c> | <c>elevenlabs</c>):
    /// edge returns the shared key-less adapter; azure/elevenlabs decrypt the channel's BYOK key from its
    /// <c>TtsConfig</c> cipher envelope and bind a fresh adapter to it. Failure <c>NOT_FOUND</c> when no key
    /// is stored, <c>KEY_DESTROYED</c>/<c>DECRYPT_FAILED</c> passed through from the vault, and
    /// <c>VALIDATION_FAILED</c> for an unknown provider.
    /// </summary>
    Task<Result<ITtsProvider>> CreateForChannelAsync(
        Guid broadcasterId,
        string provider,
        CancellationToken ct = default
    );
}
