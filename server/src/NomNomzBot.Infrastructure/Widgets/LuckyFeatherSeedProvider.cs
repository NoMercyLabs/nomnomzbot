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
using NomNomzBot.Application.Contracts.CustomCode;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Widgets.Entities;
using NomNomzBot.Infrastructure.Marketplace.FirstPartyBundles;

namespace NomNomzBot.Infrastructure.Widgets;

/// <summary>
/// Gives the Lucky Feather card its holder after a reload. The steal script keeps the holder in the channel's
/// script storage; this replays that stored value as the <c>steal</c> frame the widget already reads live (no
/// previous holder). No stored holder, or a value without an id, gives no frame, so the card stays idle.
/// </summary>
internal sealed class LuckyFeatherSeedProvider(IScriptStorageService storage) : IWidgetSeedProvider
{
    public string NaturalKey => "lucky_feather";

    public async Task<IReadOnlyList<WidgetSeedFrame>> SeedAsync(
        Guid broadcasterId,
        Widget widget,
        CancellationToken cancellationToken
    )
    {
        string? stored = await storage.GetAsync(
            broadcasterId,
            LuckyFeatherBundle.HolderStorageKey,
            cancellationToken
        );
        if (ReadHolder(stored) is not JsonElement holder)
            return [];

        return
        [
            new WidgetSeedFrame(
                "steal",
                new { previousHolder = (object?)null, newHolder = holder },
                DateTimeOffset.UtcNow
            ),
        ];
    }

    private static JsonElement? ReadHolder(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored))
            return null;

        try
        {
            using JsonDocument document = JsonDocument.Parse(stored);
            JsonElement root = document.RootElement;
            bool hasId =
                root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("id", out JsonElement id)
                && id.ValueKind == JsonValueKind.String
                && !string.IsNullOrEmpty(id.GetString());
            return hasId ? root.Clone() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
