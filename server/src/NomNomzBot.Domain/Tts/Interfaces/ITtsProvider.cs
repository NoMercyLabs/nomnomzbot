// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Domain.Tts.Interfaces;

/// <summary>
/// Abstraction for text-to-speech synthesis providers (Azure, Edge, Google, etc.).
/// </summary>
public interface ITtsProvider
{
    Task<TtsSynthesisResult> SynthesizeAsync(
        string text,
        string voiceId,
        CancellationToken cancellationToken = default
    );

    Task<IReadOnlyList<TtsVoiceInfo>> GetVoicesAsync(CancellationToken cancellationToken = default);
}

public class TtsSynthesisResult
{
    public required byte[] AudioData { get; init; }
    public required int DurationMs { get; init; }
    public required string Provider { get; init; }
    public required string VoiceId { get; init; }
    public required string ContentHash { get; init; }
}

public class TtsVoiceInfo
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string DisplayName { get; init; }
    public required string Locale { get; init; }
    public required string Gender { get; init; }
    public required string Provider { get; init; }

    // Optional catalogue metadata a provider may expose (ElevenLabs labels + preview, Azure styles). Null when
    // the provider does not carry it; the catalogue sync persists whatever is present onto TtsVoice.
    public string? Accent { get; init; }
    public string? Age { get; init; }
    public IReadOnlyList<string>? Styles { get; init; }
    public IReadOnlyList<string>? Tags { get; init; }
    public string? Description { get; init; }
    public string? PreviewUrl { get; init; }
}
