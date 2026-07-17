// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Tts.Entities;

/// <summary>
/// Per-channel TTS behavior (tts.md P.1) — one row per channel, replacing the legacy JSON-blob
/// <c>Configuration</c> row keyed <c>tts:config</c>. Carries the dispatch-plane selection
/// (<see cref="Mode"/>), the provider/voice defaults, the moderation gates (censor + approval queue),
/// and the BYOK cipher envelope: provider API keys stored as gdpr-crypto <c>CipherPayload</c> columns,
/// AEAD-wrapped under <see cref="SubjectKeyId"/> so destroying that key crypto-shreds them.
/// </summary>
public class TtsConfig : SoftDeletableEntity, ITenantScoped
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>One config row per channel (unique).</summary>
    public Guid BroadcasterId { get; set; }

    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Dispatch plane: <c>client_edge</c> | <c>byok</c> | <c>self_host</c>. Defaults to
    /// <c>self_host</c> — the only plane the dispatcher executes today; the binding spec default
    /// (<c>client_edge</c>) takes over when the client-edge widget handler ships.
    /// </summary>
    [MaxLength(20)]
    public string Mode { get; set; } = "self_host";

    /// <summary>Synthesis provider the channel prefers: <c>edge</c> | <c>azure</c> | <c>elevenlabs</c>.</summary>
    [MaxLength(20)]
    public string DefaultProvider { get; set; } = "edge";

    /// <summary>Channel default voice (→ TtsVoice.Id); viewers may override per-user.</summary>
    [MaxLength(255)]
    public string? DefaultVoiceId { get; set; } = "en-US-AriaNeural";

    /// <summary>Opt-OUT light swear filter — defaults ON; the streamer may disable (tts.md §3.5).</summary>
    public bool ProfanityCensorEnabled { get; set; } = true;

    /// <summary>When true, utterances enter the mod approval queue (P.1a) instead of direct dispatch.</summary>
    public bool ModApprovalRequired { get; set; }

    /// <summary>Minimum bits attached to a message for it to be read out; null = no bits gate.</summary>
    public int? MinBitsToTts { get; set; }

    /// <summary>The streamer's own per-utterance cap; the billing tier clamps it further at dispatch.</summary>
    public int MaxCharacters { get; set; } = 200;

    /// <summary>Lowest community standing allowed to trigger TTS: everyone | subscribers | vip | moderators | broadcaster.</summary>
    [MaxLength(20)]
    public string MinPermission { get; set; } = "everyone";

    public bool SkipBotMessages { get; set; } = true;

    public bool ReadUsernames { get; set; } = true;

    /// <summary>BYOK Azure key — base64 CipherPayload.CipherText (gdpr-crypto §4.1), AEAD under <see cref="SubjectKeyId"/>.</summary>
    public string? AzureApiKeyCipher { get; set; }

    /// <summary>base64 CipherPayload.Nonce for the Azure cipher.</summary>
    public string? AzureApiKeyNonce { get; set; }

    /// <summary>CryptoKey row version bound into the Azure cipher's AAD.</summary>
    public int? AzureKeyVersion { get; set; }

    [MaxLength(50)]
    public string? AzureRegion { get; set; }

    /// <summary>BYOK ElevenLabs key — base64 CipherPayload.CipherText, AEAD under <see cref="SubjectKeyId"/>.</summary>
    public string? ElevenLabsApiKeyCipher { get; set; }

    /// <summary>base64 CipherPayload.Nonce for the ElevenLabs cipher.</summary>
    public string? ElevenLabsApiKeyNonce { get; set; }

    /// <summary>CryptoKey row version bound into the ElevenLabs cipher's AAD.</summary>
    public int? ElevenLabsKeyVersion { get; set; }

    /// <summary>The DEK wrapping the BYOK keys; destroying it crypto-shreds them.</summary>
    public Guid? SubjectKeyId { get; set; }

    [ForeignKey(nameof(BroadcasterId))]
    public virtual Channel Channel { get; set; } = null!;

    [ForeignKey(nameof(SubjectKeyId))]
    public virtual CryptoKey? SubjectKey { get; set; }
}
