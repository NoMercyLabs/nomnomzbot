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

using System.ComponentModel.DataAnnotations;
using NomNomzBot.Domain.Tts.Entities;

namespace NomNomzBot.Application.Tts.Dtos;

/// <summary>
/// TTS configuration for a channel (tts.md P.1). BYOK keys never leave the server — only their
/// presence is surfaced, so the dashboard can show configured-state truthfully.
/// <paramref name="DefaultVoiceId"/> is the EFFECTIVE voice (the channel's own pick, else the platform default
/// voice), so the page never shows a voice the runtime does not speak; <paramref name="FollowsPlatformDefaultVoice"/>
/// says which of the two it is.
/// </summary>
public sealed record TtsConfigDto(
    bool IsEnabled,
    string Mode,
    string DefaultProvider,
    string? DefaultVoiceId,
    int MaxCharacters,
    string MinPermission,
    bool SkipBotMessages,
    bool ReadUsernames,
    bool ProfanityCensorEnabled,
    bool ModApprovalRequired,
    int? MinBitsToTts,
    bool ViewerVoiceSelfServiceEnabled,
    bool HasAzureByokKey,
    bool HasElevenLabsByokKey,
    string? AzureRegion,
    bool FollowsPlatformDefaultVoice,
    int CooldownSeconds
);

/// <summary>
/// What the TTS settings page may offer: the providers, dispatch modes and standings the update accepts, and
/// the longest message cap. Served from <see cref="TtsConfigOptions"/>, the same place the update validates against.
/// </summary>
public sealed record TtsConfigOptionsDto(
    IReadOnlyList<string> Providers,
    IReadOnlyList<string> Modes,
    IReadOnlyList<string> MinPermissions,
    int MaxCharactersLimit,
    int CooldownSecondsDefault,
    int CooldownSecondsMax
)
{
    /// <summary>The one place the options response is built, so the page and the update check share their limits.</summary>
    public static TtsConfigOptionsDto Current() =>
        new(
            TtsConfigOptions.Providers,
            TtsConfigOptions.Modes,
            TtsConfigOptions.MinPermissions,
            TtsConfigOptions.MaxCharactersLimit,
            TtsConfigOptions.CooldownSecondsDefault,
            TtsConfigOptions.CooldownSecondsMax
        );
}

/// <summary>
/// The channel's auto-provisioned TTS overlay (widgets-overlays.md §1.2 system surface): the OBS browser-source
/// URL to add once, and when it last reported running (audit B5) — so the dashboard can show a real "is this
/// actually in OBS" signal instead of assuming.
/// </summary>
public sealed record TtsOverlayDto(string OverlayUrl, DateTime? LastRanAt);

/// <summary>Request to store a BYOK provider API key; the key is vault-encrypted at rest and never echoed.</summary>
public sealed record SetTtsByokKeyDto
{
    [Required]
    [MaxLength(512)]
    public string ApiKey { get; init; } = null!;

    /// <summary>Azure only — the Cognitive Services region the key belongs to.</summary>
    [MaxLength(50)]
    public string? Region { get; init; }
}

/// <summary>Request to update TTS configuration; every field optional — only what is sent changes.</summary>
public sealed record UpdateTtsConfigDto
{
    public bool? IsEnabled { get; init; }

    /// <summary>Dispatch plane: byok | self_host (client_edge is retired and reads as self_host).</summary>
    [RegularExpression(TtsConfigOptions.ModePattern)]
    public string? Mode { get; init; }

    /// <summary>Preferred synthesis provider: edge | azure | elevenlabs.</summary>
    [RegularExpression(TtsConfigOptions.ProviderPattern)]
    public string? DefaultProvider { get; init; }

    [MaxLength(255)]
    public string? DefaultVoiceId { get; init; }

    /// <summary>True drops the channel's own voice pick so it follows the platform default voice again.</summary>
    public bool? FollowPlatformDefaultVoice { get; init; }

    [Range(1, TtsConfigOptions.MaxCharactersLimit)]
    public int? MaxCharacters { get; init; }

    [RegularExpression(TtsConfigOptions.MinPermissionPattern)]
    public string? MinPermission { get; init; }

    /// <summary>Seconds a viewer waits between spoken messages; 0 turns the cooldown off.</summary>
    [Range(0, TtsConfigOptions.CooldownSecondsMax)]
    public int? CooldownSeconds { get; init; }

    public bool? SkipBotMessages { get; init; }
    public bool? ReadUsernames { get; init; }

    /// <summary>Opt-out light swear filter — masks mild profanity before the text is spoken.</summary>
    public bool? ProfanityCensorEnabled { get; init; }

    /// <summary>When true, utterances enter the moderator approval queue instead of playing immediately.</summary>
    public bool? ModApprovalRequired { get; init; }

    /// <summary>Minimum bits attached to a message for TTS; 0 clears the gate.</summary>
    [Range(0, 1_000_000)]
    public int? MinBitsToTts { get; init; }

    /// <summary>When true, viewers may pick their own voice via <c>!voice</c>; false locks it to mods only.</summary>
    public bool? ViewerVoiceSelfServiceEnabled { get; init; }

    /// <summary>Azure Cognitive Services region for the channel's BYOK key; an empty string clears it.</summary>
    [MaxLength(50)]
    public string? AzureRegion { get; init; }
}

/// <summary>A viewer's assigned TTS voice within a channel (overrides the channel default when they speak).</summary>
public sealed record UserTtsVoiceDto(string UserId, string VoiceId);

/// <summary>Request to assign a specific TTS voice to a viewer.</summary>
public sealed record SetUserVoiceDto
{
    [Required]
    [MaxLength(255)]
    public string VoiceId { get; init; } = null!;
}
