// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Globalization;
using NomNomzBot.Domain.Chat.ValueObjects;

namespace NomNomzBot.Infrastructure.Chat.Providers;

/// <summary>
/// Turns 7TV's typed paint layers into the CSS a browser already understands.
///
/// <para>
/// Done here, once, rather than in each widget that renders a name: 7TV's layer union grows over time, and
/// several separate Vue overlays re-implementing it would drift apart the first time it changed. Verified
/// against the live v4 API on 2026-09-04 — of 1,024 paints the layer kinds in actual use are linear
/// gradient (622), radial gradient (264) and image (131).
/// </para>
/// </summary>
public static class SevenTvPaintMapper
{
    /// <summary>7TV layer type discriminators, as they appear in the GraphQL <c>__typename</c>.</summary>
    private const string LinearGradient = "PaintLayerTypeLinearGradient";
    private const string RadialGradient = "PaintLayerTypeRadialGradient";
    private const string SingleColor = "PaintLayerTypeSingleColor";
    private const string Image = "PaintLayerTypeImage";

    /// <summary>One colour stop: a position in 0..1 and an <c>#RRGGBBAA</c> colour.</summary>
    public readonly record struct Stop(double At, string Hex);

    /// <summary>
    /// One raster asset 7TV offers for an image layer — one of several scale/format variants for the SAME
    /// picture. <c>FrameCount</c> greater than 1 marks the animated encoding of that format (7TV ships both
    /// an animated and a static-preview variant side by side, not just one).
    /// </summary>
    public readonly record struct ImageVariant(string Url, string Mime, int Scale, int FrameCount);

    /// <summary>One layer, already discriminated by <paramref name="Type"/>.</summary>
    public sealed record Layer(
        string Type,
        string? Hex = null,
        double Angle = 0,
        bool Repeating = false,
        string? Shape = null,
        IReadOnlyList<Stop>? Stops = null,
        IReadOnlyList<ImageVariant>? Images = null
    );

    /// <summary>One drop shadow. Offsets and blur are in px, as 7TV supplies them.</summary>
    public readonly record struct Shadow(string Hex, double OffsetX, double OffsetY, double Blur);

    /// <summary>
    /// Flattens [layers] and [shadows] into a renderable paint, or null when there is nothing to render.
    ///
    /// <para>
    /// Only the FIRST layer that produces something is used. 7TV stacks layers, but a name painted with
    /// background-clip has exactly one background to give: compositing them would need a real layer engine
    /// in every widget, and picking the first is what the 7TV extension visibly does too.
    /// </para>
    /// </summary>
    public static ChatPaint? Map(
        string id,
        string name,
        IReadOnlyList<Layer> layers,
        IReadOnlyList<Shadow> shadows
    )
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;

        string? backgroundImage = null;
        string? color = null;
        bool imageOnly = false;
        // Distinct from `imageOnly`: that flag says "an image LAYER was present"; this says the winning
        // backgroundImage specifically came FROM one — needed because a later gradient layer can still win
        // the loop after an earlier image layer failed to resolve a url (see the two IsImageOnly tests).
        bool backgroundIsImage = false;

        foreach (Layer layer in layers)
        {
            switch (layer.Type)
            {
                case LinearGradient when HasStops(layer):
                    backgroundImage =
                        $"{(layer.Repeating ? "repeating-linear-gradient" : "linear-gradient")}("
                        + $"{Number(layer.Angle)}deg, {StopList(layer.Stops!)})";
                    break;

                case RadialGradient when HasStops(layer):
                    // 7TV's shape is "circle"/"ellipse"; CSS takes the same words, so an unknown value falls
                    // back to circle rather than emitting something the browser will drop the whole rule for.
                    string shape = layer.Shape is "ellipse" ? "ellipse" : "circle";
                    backgroundImage =
                        $"{(layer.Repeating ? "repeating-radial-gradient" : "radial-gradient")}("
                        + $"{shape}, {StopList(layer.Stops!)})";
                    break;

                case SingleColor when !string.IsNullOrWhiteSpace(layer.Hex):
                    color = layer.Hex;
                    break;

                case Image:
                    imageOnly = true;
                    // 7TV ships several scale/format variants of the SAME picture, not one asset — pick the
                    // one an overlay should actually use before this can render anything.
                    string? imageUrl = BestImageVariantUrl(layer.Images);
                    if (imageUrl is not null)
                    {
                        backgroundImage = $"url(\"{imageUrl}\")";
                        backgroundIsImage = true;
                    }
                    break;

                default:
                    continue;
            }

            if (backgroundImage is not null || color is not null)
                break;
        }

        // A gradient's first stop doubles as the flat fallback: anywhere the gradient cannot apply (a plain
        // text colour, an older renderer) still gets a colour from the same paint rather than the default.
        color ??= layers.FirstOrDefault(l => HasStops(l))?.Stops!.FirstOrDefault().Hex;

        string? textShadow = ShadowList(shadows);

        // An image-only paint carries no colour and often no shadow, so everything above can be null and it
        // is STILL a paint the chatter is wearing. Dropping it would make "wears a paint we cannot draw"
        // indistinguishable from "wears none", and the consumer could never fall back to their 7TV colour.
        if (backgroundImage is null && color is null && textShadow is null && !imageOnly)
            return null;

        return new()
        {
            Id = id,
            Name = name,
            BackgroundImage = backgroundImage,
            Color = color,
            TextShadow = textShadow,
            // True either when we could not resolve the image (nothing to draw, the pre-existing meaning)
            // OR when what WAS resolved is a raster picture rather than a CSS gradient function — a consumer
            // needs to know that to pick e.g. background-size: cover over an unscaled gradient.
            IsImageOnly = imageOnly && (backgroundImage is null || backgroundIsImage),
        };
    }

    /// <summary>
    /// Picks ONE asset out of the several scale/format variants 7TV offers for an image layer — the same
    /// picture repeated as webp/avif/gif/png at 1x-4x, animated and static. Prefers the animated encoding
    /// (this is a live overlay, not a static thumbnail) in the smallest well-supported format at the
    /// largest available scale — webp first (small, animates, broadly supported in a Chromium overlay
    /// browser source), falling back through avif/gif/png, then to whatever exists if none of those match.
    /// </summary>
    private static string? BestImageVariantUrl(IReadOnlyList<ImageVariant>? images)
    {
        if (images is not { Count: > 0 })
            return null;

        bool hasAnimated = images.Any(i => i.FrameCount > 1);
        IEnumerable<ImageVariant> pool = hasAnimated ? images.Where(i => i.FrameCount > 1) : images;
        ImageVariant[] candidates = [.. pool];

        foreach (string mime in PreferredImageMimes)
        {
            ImageVariant? best = candidates
                .Where(i => string.Equals(i.Mime, mime, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(i => i.Scale)
                .Cast<ImageVariant?>()
                .FirstOrDefault();
            if (best is not null)
                return best.Value.Url;
        }

        return candidates
            .OrderByDescending(i => i.Scale)
            .Cast<ImageVariant?>()
            .FirstOrDefault()
            ?.Url;
    }

    private static readonly string[] PreferredImageMimes =
    [
        "image/webp",
        "image/avif",
        "image/gif",
        "image/png",
    ];

    private static bool HasStops(Layer layer) => layer.Stops is { Count: > 0 };

    /// <summary>7TV positions a stop in 0..1; CSS wants a percentage.</summary>
    private static string StopList(IReadOnlyList<Stop> stops) =>
        string.Join(", ", stops.Select(s => $"{s.Hex} {Number(s.At * 100)}%"));

    private static string? ShadowList(IReadOnlyList<Shadow> shadows) =>
        shadows.Count == 0
            ? null
            : string.Join(
                ", ",
                shadows.Select(s =>
                    $"{Number(s.OffsetX)}px {Number(s.OffsetY)}px {Number(s.Blur)}px {s.Hex}"
                )
            );

    /// <summary>
    /// Invariant formatting, always. A machine with a comma decimal separator would otherwise emit
    /// <c>66,5deg</c>, which does not just look wrong — the comma ends the CSS argument and the browser
    /// drops the whole declaration, so every paint would vanish on that machine and nowhere else.
    /// </summary>
    private static string Number(double value) =>
        Math.Round(value, 4).ToString(CultureInfo.InvariantCulture);
}
