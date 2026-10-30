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

using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Application.Abstractions.Templating;

/// <summary>
/// Everything one render knows, handed to every provider: the channel, the caller's seed variables, the
/// clock, a bag for <c>{set:…}</c> values and a per-render cache that <c>PrepareAsync</c> fills.
/// </summary>
public sealed class TemplateRenderScope
{
    public required Guid? BroadcasterId { get; init; }

    /// <summary>The live channel state, or null when the render has no channel.</summary>
    public required ChannelContext? Channel { get; init; }

    /// <summary>The caller's seed variables (case-insensitive). A seed always wins over a provider.</summary>
    public required IReadOnlyDictionary<string, string> Variables { get; init; }

    /// <summary>The render's clock reading (UTC).</summary>
    public required DateTimeOffset Now { get; init; }

    /// <summary>The channel's IANA zone, or null for UTC.</summary>
    public string? Timezone => Channel?.Timezone;

    /// <summary>The channel's language, or null for English.</summary>
    public string? Language => Channel?.Language;

    /// <summary>
    /// The most copies or the widest pad a <c>text.*</c> helper may produce in this render, or null for the usual
    /// limit. A viewer's own text sets it (a viewer must not make one token fill a screen).
    /// </summary>
    public int? TextCountLimit { get; init; }

    /// <summary>
    /// True for a viewer's own text (the viewer resolver sets <see cref="TextCountLimit"/>). A helper that would keep
    /// state keyed by the text, or run work the limit cannot cut, skips it here.
    /// </summary>
    public bool IsViewerText => TextCountLimit is not null;

    /// <summary>Values written by <c>{set:…}</c> during this render.</summary>
    public Dictionary<string, string> SetVariables { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Per-render cache: a provider keeps what <c>PrepareAsync</c> fetched here for <c>RenderAsync</c>.</summary>
    public Dictionary<string, object> Items { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Resolves one key (<c>stream.live</c>, <c>count.deaths</c>) through the same resolver as a token: a seed
    /// first, then a provider, then the as-built helpers. Null when nothing resolves the key. Set by the
    /// resolver; null for a scope built by hand.
    /// </summary>
    public Func<string, CancellationToken, ValueTask<string?>>? ResolveKeyAsync { get; set; }
}
