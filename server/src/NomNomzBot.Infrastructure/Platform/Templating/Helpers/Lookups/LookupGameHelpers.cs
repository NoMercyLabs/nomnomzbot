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

using NomNomzBot.Application.Abstractions.Templating;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Lookups;

/// <summary>
/// <c>lookup.game:&lt;name&gt;.boxart</c> and <c>lookup.game.top</c>: Twitch categories through the bot's own
/// Helix client.
/// </summary>
internal sealed class LookupGameHelpers()
    : LookupHelperGroup(
        new LookupRow(
            "lookup.game",
            "https://static-cdn.jtvnw.net/ttv-boxart/27471-285x380.jpg",
            "twitch",
            TemplateHelperFreshness.Live,
            1,
            1,
            BoxartAsync
        ),
        new LookupRow(
            "lookup.game.top",
            "Just Chatting",
            "twitch",
            TemplateHelperFreshness.Live,
            0,
            0,
            TopAsync
        )
    )
{
    private static readonly string[] Fields = ["boxart"];

    private static async Task<string?> BoxartAsync(LookupCall call)
    {
        (string name, string? field) = LookupArguments.Split(call.Args[0], Fields);
        if (field is null || name.Length == 0)
            return null;

        return await call.Data.GameAsync(name, call.Ct) is { } game
            ? game.BoxArtUrl.Replace("{width}", "285").Replace("{height}", "380")
            : call.Fallback(LookupFallbacks.Twitch);
    }

    private static async Task<string?> TopAsync(LookupCall call) =>
        await call.Data.TopGameAsync(call.Ct) is { } game
            ? game.Name
            : call.Fallback(LookupFallbacks.Twitch);
}
