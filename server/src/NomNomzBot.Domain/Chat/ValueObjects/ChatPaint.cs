// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Domain.Chat.ValueObjects;

/// <summary>
/// A chatter's 7TV "paint" — the cosmetic their display name is rendered with, and what the overlays use as
/// the theme for their profile.
///
/// <para>
/// Deliberately reduced to something an overlay can paint WITHOUT knowing about 7TV. 7TV models a paint as a
/// stack of typed layers (single colour, linear gradient, radial gradient, image) plus drop shadows; a Vue
/// widget re-implementing that union would have to be updated every time 7TV adds a layer kind, and each of
/// the several widgets that show a name would have to do it identically. So the shape is flattened here,
/// once, into the CSS a browser already understands.
/// </para>
/// </summary>
public sealed record ChatPaint
{
    /// <summary>The 7TV paint id (a ULID) — stable, and the cache key.</summary>
    public required string Id { get; init; }

    /// <summary>Human name ("Emerald Doppler"), for tooltips and for making a debug log readable.</summary>
    public required string Name { get; init; }

    /// <summary>
    /// A ready-to-use CSS <c>background-image</c> value — either a gradient function or a raster
    /// <c>url(...)</c> (see <see cref="IsImageOnly"/> for which) — or null when the paint is a flat colour or
    /// an image whose asset could not be resolved. A consumer applies it with background-clip: text.
    /// </summary>
    public string? BackgroundImage { get; init; }

    /// <summary>
    /// A flat CSS colour for the paint, used when there is no gradient AND as the fallback a renderer shows
    /// while an image layer's asset has not loaded. Null when the paint carries no usable colour at all.
    /// </summary>
    public string? Color { get; init; }

    /// <summary>A ready-to-use CSS <c>text-shadow</c> value, or null when the paint casts none.</summary>
    public string? TextShadow { get; init; }

    /// <summary>
    /// True when this paint is an image layer — whether or not <see cref="BackgroundImage"/> resolved to a
    /// url. When it DID resolve, this tells a consumer the value is a raster picture (so e.g.
    /// background-size: cover fits it, not an unscaled gradient) rather than the colour above being a mere
    /// stand-in for an asset it could not draw at all.
    /// </summary>
    public bool IsImageOnly { get; init; }
}
