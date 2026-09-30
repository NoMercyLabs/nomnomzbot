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
using NomNomzBot.Application.Abstractions.Localization;

namespace NomNomzBot.Application.Commands.Dtos;

/// <summary>
/// An event response configuration, as the runtime performs it: while <paramref name="FollowsPlatformDefault"/>
/// is true the enabled flag, type and message are the platform default, not the stored channel row.
/// <paramref name="ToneLines"/> are the lines the bot picks from while the row follows the default: the
/// platform admin's text alone when set, else the catalogue lines for the channel's tone. Empty for a row
/// with its own text. A following row's <paramref name="Message"/> is the first of them.
/// </summary>
public sealed record EventResponseDto(
    Guid Id,
    string EventType,
    bool IsEnabled,
    string ResponseType,
    string? Message,
    Guid? PipelineId,
    Dictionary<string, string> Metadata,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    bool FollowsPlatformDefault,
    bool SpeakWithTts,
    IReadOnlyList<string> ToneLines
);

/// <summary>
/// The channel's auto-provisioned alert overlay (widgets-overlays.md §1.2): its OBS browser-source URL and
/// when it last reported running.
/// </summary>
public sealed record AlertOverlayDto(string OverlayUrl, DateTime? LastRanAt);

/// <summary>
/// Lightweight event response summary (effective values — see <see cref="EventResponseDto"/>).
/// <paramref name="SpeaksInTone"/> is true when a personality change changes what it says: it follows the
/// platform default, or it is a chat row of its own with no text.
/// </summary>
public sealed record EventResponseListItem(
    Guid Id,
    string EventType,
    bool IsEnabled,
    string ResponseType,
    DateTime UpdatedAt,
    bool FollowsPlatformDefault,
    bool SpeaksInTone
);

/// <summary>
/// One catalog preset for an event type: the ready-to-use default template the dashboard pre-fills the
/// message input with, and the exact template variables the trigger source seeds for that event.
/// <see cref="DefaultTemplate"/> is a translation KEY only (S-SCHEMA-I18N-redesign) — the English and Dutch
/// sentences live in the dashboard's <c>strings.xml</c>, never in this assembly.
/// </summary>
public sealed record EventResponsePresetDto(
    string EventType,
    LocalizedText DefaultTemplate,
    IReadOnlyList<string> Variables
);

/// <summary>Request to update an event response configuration.</summary>
public sealed record UpdateEventResponseDto
{
    public bool? IsEnabled { get; init; }

    [RegularExpression("^(chat_message|overlay|pipeline|none)$")]
    public string? ResponseType { get; init; }

    [MaxLength(2000)]
    public string? Message { get; init; }

    public Guid? PipelineId { get; init; }

    public Dictionary<string, string>? Metadata { get; init; }

    /// <summary>A chat_message response also speaks its resolved text through the channel's TTS. Absent leaves it unchanged.</summary>
    public bool? SpeakWithTts { get; init; }
}
