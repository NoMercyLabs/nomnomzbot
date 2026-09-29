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

namespace NomNomzBot.Application.Commands.Services;

/// <summary>
/// Manages per-channel enable/disable state and overrides for built-in commands.
/// Absent row = enabled with catalog defaults.
/// </summary>
public interface IBuiltinCommandService
{
    /// <summary>Returns all built-ins for a channel, merging catalog defaults with stored overrides.</summary>
    Task<Result<IReadOnlyList<BuiltinCommandDto>>> ListAsync(
        string broadcasterId,
        CancellationToken ct = default
    );

    /// <summary>One built-in as the channel sees it — defaults, the channel's overrides, and what a reset clears.</summary>
    Task<Result<BuiltinCommandDto>> GetAsync(
        string broadcasterId,
        string builtinKey,
        CancellationToken ct = default
    );

    /// <summary>Enables or disables a built-in for the channel. Upserts the toggle row.</summary>
    Task<Result> SetEnabledAsync(
        string broadcasterId,
        string builtinKey,
        bool enabled,
        CancellationToken ct = default
    );

    /// <summary>
    /// Enables or disables the channel's "speak with TTS" option for a built-in that supports it (S-OBS-12,
    /// e.g. <c>!quote</c>) — stored alongside the channel's reply texts (<see cref="IBuiltinReplyService"/>)
    /// in the same <c>ChannelBuiltinCommand.OverridesJson</c> blob, merged so setting one never
    /// clears the other. Default off: the built-in posts to chat only until explicitly turned on.
    /// </summary>
    Task<Result> SetSpeakWithTtsAsync(
        string broadcasterId,
        string builtinKey,
        bool enabled,
        CancellationToken ct = default
    );

    /// <summary>
    /// Sets the channel's cooldown and permission floor for a built-in (commands-pipelines.md §4.5). A null field
    /// (or a value equal to the catalogue's) inherits the default. The floor can be raised, never lowered below
    /// the built-in's own floor — that floor is a safety gate (e.g. <c>!whisper</c> is moderator-only so a viewer
    /// cannot make the bot DM a stranger). Reserved data-rights built-ins cannot be changed.
    /// </summary>
    Task<Result<BuiltinCommandDto>> UpdateSettingsAsync(
        string broadcasterId,
        string builtinKey,
        BuiltinSettingsUpdate update,
        CancellationToken ct = default
    );

    /// <summary>
    /// Puts a built-in back to its defaults for the channel: enabled, no TTS, the catalogue cooldown and floor,
    /// and the default wording for every reply it speaks with. Returns the built-in as it now resolves.
    /// </summary>
    Task<Result<BuiltinCommandDto>> ResetAsync(
        string broadcasterId,
        string builtinKey,
        CancellationToken ct = default
    );
}

/// <summary>The channel's behaviour settings for one built-in; null = inherit the catalogue default.</summary>
/// <param name="CooldownSeconds">Seconds between uses, 0–3600.</param>
/// <param name="MinPermissionLevel">A permission rung name (e.g. <c>Subscriber</c>), at or above the built-in's floor.</param>
public sealed record BuiltinSettingsUpdate(int? CooldownSeconds, string? MinPermissionLevel);

/// <summary>A built-in command as one channel sees it.</summary>
/// <param name="BuiltinKey">The bare catalogue key (e.g. <c>sr</c>).</param>
/// <param name="Name">The chat trigger (e.g. <c>!sr</c>).</param>
/// <param name="IsEnabled">Whether the command answers in this channel.</param>
/// <param name="DefaultCooldownSeconds">The catalogue cooldown.</param>
/// <param name="DefaultMinPermissionLevel">The catalogue permission floor (rung name).</param>
/// <param name="ReplyGroup">The reply group the built-in speaks with (e.g. <c>unlurk</c> → <c>lurk</c>).</param>
/// <param name="SpeakWithTts">Whether the reply is also read out by TTS.</param>
/// <param name="IsReserved">A data-rights command: always on, settings locked.</param>
/// <param name="CooldownSecondsOverride">The channel's own cooldown; null = the default applies.</param>
/// <param name="MinPermissionLevelOverride">The channel's own floor (rung name); null = the default applies.</param>
/// <param name="ReplyOverrideCount">How many replies of its reply group the channel has reworded.</param>
public sealed record BuiltinCommandDto(
    string BuiltinKey,
    string Name,
    bool IsEnabled,
    int DefaultCooldownSeconds,
    string DefaultMinPermissionLevel,
    string ReplyGroup,
    bool SpeakWithTts,
    bool IsReserved,
    int? CooldownSecondsOverride,
    string? MinPermissionLevelOverride,
    int ReplyOverrideCount
);
