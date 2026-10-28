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

using System.Globalization;
using NomNomzBot.Application.Contracts.Twitch;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Emotes;

/// <summary>One emote by name: does it exist here, its picture, its network, and its repeats and walls.</summary>
internal sealed class EmoteLookupHelpers()
    : EmoteHelperGroup(
        EmoteRow.Custom("emote", "catJAM", EmoteSources.Catalogue, NameOrFallbackAsync, 1, 2),
        EmoteRow.Custom(
            "emote.image",
            "https://cdn.7tv.app/emote/catjam01/2x.webp",
            EmoteSources.Catalogue,
            call => FieldAsync(call, e => e.ImageUrl),
            1,
            1
        ),
        EmoteRow.Custom(
            "emote.provider",
            "7TV",
            EmoteSources.Catalogue,
            call => FieldAsync(call, e => e.Provider),
            1,
            1
        ),
        EmoteRow.Custom(
            "emote.repeat",
            "catJAM catJAM catJAM",
            EmoteSources.Catalogue,
            RepeatAsync,
            2,
            2
        ),
        EmoteRow.Custom(
            "emote.wall",
            "catJAM catJAM / catJAM catJAM",
            EmoteSources.Catalogue,
            WallAsync,
            3,
            3
        )
    )
{
    private const string RowBreak = " / ";

    /// <summary>The emote when the channel has it, else the fallback text, else nothing.</summary>
    private static async Task<string?> NameOrFallbackAsync(EmoteCall call)
    {
        EmoteItem? found = EmoteText.Find(await call.Data.AllAsync(), call.Args[0]);
        return found?.Code ?? (call.Args.Count > 1 ? call.Args[1] : string.Empty);
    }

    /// <summary>One field of the named emote; empty when the channel does not have it.</summary>
    private static async Task<string?> FieldAsync(EmoteCall call, Func<EmoteItem, string?> field)
    {
        EmoteItem? found = EmoteText.Find(await call.Data.AllAsync(), call.Args[0]);
        return found is null ? string.Empty : field(found) ?? string.Empty;
    }

    private static async Task<string?> RepeatAsync(EmoteCall call)
    {
        if (!TryCount(call.Args[1], EmoteText.MaxRepeat, out int times))
            return null;

        return await FieldAsync(call, e => string.Join(' ', Enumerable.Repeat(e.Code, times)));
    }

    private static async Task<string?> WallAsync(EmoteCall call)
    {
        if (
            !TryCount(call.Args[1], EmoteText.MaxWallRows, out int rows)
            || !TryCount(call.Args[2], EmoteText.MaxWallColumns, out int columns)
        )
            return null;

        return await FieldAsync(
            call,
            e =>
            {
                string line = string.Join(' ', Enumerable.Repeat(e.Code, columns));
                return string.Join(RowBreak, Enumerable.Repeat(line, rows));
            }
        );
    }

    private static bool TryCount(string text, int max, out int count) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out count)
        && count >= 1
        && count <= max;
}

/// <summary>The cheermote of a Bits amount: the biggest tier that fits, such as <c>Cheer100</c>.</summary>
internal sealed class EmoteCheermoteHelpers()
    : EmoteHelperGroup(
        EmoteRow.Custom(
            "emote.cheermote",
            "Cheer100",
            EmoteSources.Cheermotes,
            CheermoteAsync,
            1,
            1
        )
    )
{
    private const string FirstPartyPrefix = "Cheer";

    private static async Task<string?> CheermoteAsync(EmoteCall call)
    {
        if (
            !int.TryParse(
                call.Args[0],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int bits
            )
            || bits < 1
        )
            return null;

        IReadOnlyList<TwitchCheermote> all = await call.Data.CheermotesAsync();
        TwitchCheermote? cheermote =
            all.FirstOrDefault(c =>
                string.Equals(c.Prefix, FirstPartyPrefix, StringComparison.OrdinalIgnoreCase)
            ) ?? all.FirstOrDefault();
        TwitchCheermoteTier? tier = cheermote
            ?.Tiers.Where(t => t.CanCheer && t.MinBits <= bits)
            .OrderByDescending(t => t.MinBits)
            .FirstOrDefault();

        return tier is null
            ? string.Empty
            : cheermote!.Prefix + tier.MinBits.ToString(CultureInfo.InvariantCulture);
    }
}
