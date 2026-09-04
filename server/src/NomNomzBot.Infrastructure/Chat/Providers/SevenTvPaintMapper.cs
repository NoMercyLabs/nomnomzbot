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

    /// <summary>One layer, already discriminated by <paramref name="Type"/>.</summary>
    public sealed record Layer(
        string Type,
        string? Hex = null,
        double Angle = 0,
        bool Repeating = false,
        string? Shape = null,
        IReadOnlyList<Stop>? Stops = null
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
                    // The asset lives behind a url this mapper is not given. Flagged so a consumer can decide
                    // whether its surface can live with the fallback colour instead of silently showing one.
                    imageOnly = true;
                    continue;

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
            IsImageOnly = imageOnly && backgroundImage is null,
        };
    }

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
