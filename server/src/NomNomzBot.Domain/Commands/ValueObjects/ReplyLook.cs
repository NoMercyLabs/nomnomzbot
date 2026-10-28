// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json;
using System.Text.Json.Serialization;

namespace NomNomzBot.Domain.Commands.ValueObjects;

/// <summary>
/// The highlight of a chat overlay line, named by design token. The overlay maps the token to its own color, so a
/// channel never types a hex value and the line follows the design system.
/// </summary>
[JsonConverter(typeof(ReplyHighlightJsonConverter))]
public enum ReplyHighlight
{
    Primary = 0,
    Success = 1,
    Warning = 2,
    Destructive = 3,
}

/// <summary>Wire and storage form of <see cref="ReplyHighlight"/>: <c>primary | success | warning | destructive</c>.</summary>
public sealed class ReplyHighlightJsonConverter()
    : JsonStringEnumConverter<ReplyHighlight>(JsonNamingPolicy.CamelCase);

/// <summary>How big a chat overlay line is drawn.</summary>
[JsonConverter(typeof(ReplySizeJsonConverter))]
public enum ReplySize
{
    Normal = 0,
    Large = 1,
}

/// <summary>Wire and storage form of <see cref="ReplySize"/>: <c>normal | large</c>.</summary>
public sealed class ReplySizeJsonConverter()
    : JsonStringEnumConverter<ReplySize>(JsonNamingPolicy.CamelCase);

/// <summary>
/// How a reply looks on the chat overlay: an optional highlight color (a design token name), a size, and how long
/// the line stays before it fades. A missing value means the overlay's own default for that part, so a null look
/// changes nothing about the line.
/// </summary>
public sealed record ReplyLook(
    ReplyHighlight? Highlight = null,
    ReplySize Size = ReplySize.Normal,
    int? DurationSeconds = null
)
{
    /// <summary>The look of a line that chose nothing: every part is the overlay default.</summary>
    public static ReplyLook Default { get; } = new();

    /// <summary>The shortest time a line may stay on screen, in seconds.</summary>
    public const int MinDurationSeconds = 1;

    /// <summary>The longest time a line may stay on screen, in seconds.</summary>
    public const int MaxDurationSeconds = 120;

    /// <summary>True when every part is at the overlay default, so the look carries no information.</summary>
    [JsonIgnore]
    public bool IsDefault =>
        Highlight is null && Size == ReplySize.Normal && DurationSeconds is null;

    /// <summary>The highlight as the lowercase token name the overlay reads, or null for the default color.</summary>
    [JsonIgnore]
    public string? HighlightToken => Highlight?.ToString().ToLowerInvariant();

    /// <summary>The size as the lowercase name the overlay reads.</summary>
    [JsonIgnore]
    public string SizeToken => Size.ToString().ToLowerInvariant();

    /// <summary>What is wrong with this look, or null when it can be stored.</summary>
    public string? Problem() =>
        DurationSeconds is { } seconds
        && (seconds < MinDurationSeconds || seconds > MaxDurationSeconds)
            ? $"The overlay line duration must be between {MinDurationSeconds} and {MaxDurationSeconds} seconds."
            : null;
}
