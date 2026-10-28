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

/// <summary>Where an authored reply goes: the platform chat, the chat overlay, both, or nowhere.</summary>
[JsonConverter(typeof(ReplyTargetJsonConverter))]
public enum ReplyTarget
{
    /// <summary>The platform chat only. This is what every reply did before the target existed.</summary>
    Chat = 0,

    /// <summary>The chat overlay (OBS chat widgets) only. Nothing goes to the platform chat.</summary>
    Overlay = 1,

    /// <summary>The platform chat and the chat overlay.</summary>
    Both = 2,

    /// <summary>Nowhere. The command still runs; it just says nothing.</summary>
    None = 3,
}

/// <summary>Wire and storage form of <see cref="ReplyTarget"/>: <c>chat | overlay | both | none</c>.</summary>
public sealed class ReplyTargetJsonConverter()
    : JsonStringEnumConverter<ReplyTarget>(JsonNamingPolicy.CamelCase);

/// <summary>
/// How a channel-authored reply is delivered. <see cref="Html"/> makes the overlay line render the authored
/// text as sanitized HTML; the platform chat always gets plain text. A missing value everywhere means
/// <see cref="ChatOnly"/>, the behavior before this setting existed.
/// </summary>
public sealed record ReplyDelivery(ReplyTarget Target = ReplyTarget.Chat, bool Html = false)
{
    /// <summary>The default: reply in the platform chat, as plain text.</summary>
    public static ReplyDelivery ChatOnly { get; } = new();

    /// <summary>
    /// The value a row stores for <paramref name="delivery"/>: plain Twitch chat is stored as null (the default
    /// every row had before the setting existed), so a client that sends the chat target back resets a row.
    /// </summary>
    public static ReplyDelivery? ForStorage(ReplyDelivery? delivery) =>
        delivery is { Target: ReplyTarget.Chat } ? null : delivery;

    [JsonIgnore]
    public bool SendsToChat => Target is ReplyTarget.Chat or ReplyTarget.Both;

    [JsonIgnore]
    public bool SendsToOverlay => Target is ReplyTarget.Overlay or ReplyTarget.Both;
}
