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
public sealed record ReplyDelivery(
    ReplyTarget Target = ReplyTarget.Chat,
    bool Html = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ReplyLook? Look = null
)
{
    /// <summary>The default: reply in the platform chat, as plain text.</summary>
    public static ReplyDelivery ChatOnly { get; } = new();

    /// <summary>
    /// The value a row stores for <paramref name="delivery"/>: plain Twitch chat is stored as null (the default
    /// every row had before the setting existed), so a client that sends the chat target back resets a row. A look
    /// at the overlay default is dropped, so a stored delivery only carries a look that changes the line.
    /// </summary>
    public static ReplyDelivery? ForStorage(ReplyDelivery? delivery) =>
        delivery switch
        {
            { Target: ReplyTarget.Chat } => null,
            { Look.IsDefault: true } => delivery with { Look = null },
            _ => delivery,
        };

    /// <summary>What is wrong with this delivery, or null when it can be stored.</summary>
    public string? Problem() => Look?.Problem();

    [JsonIgnore]
    public bool SendsToChat => Target is ReplyTarget.Chat or ReplyTarget.Both;

    [JsonIgnore]
    public bool SendsToOverlay => Target is ReplyTarget.Overlay or ReplyTarget.Both;
}
