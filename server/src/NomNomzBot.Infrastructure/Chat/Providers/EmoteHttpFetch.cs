// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Net;
using Newtonsoft.Json;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Chat.ValueObjects;

namespace NomNomzBot.Infrastructure.Chat.Providers;

/// <summary>
/// The shared GET-then-parse path for the third-party emote adapters (BTTV/FFZ/7TV) — identical plumbing across
/// all three, so it lives here rather than being triplicated. A 404 means "no emotes for this channel" (success,
/// empty); any other non-success, transport, or JSON error is a failure the refresh worker falls back from
/// (stale-OK). It never throws.
/// </summary>
internal static class EmoteHttpFetch
{
    public static async Task<Result<IReadOnlyList<ChatEmote>>> GetAsync(
        IHttpClientFactory httpClientFactory,
        string url,
        Func<string, IReadOnlyList<ChatEmote>> parse,
        string provider,
        CancellationToken ct
    )
    {
        try
        {
            HttpClient client = httpClientFactory.CreateClient(ChatEmoteHttpClient.Name);
            using HttpResponseMessage response = await client.GetAsync(url, ct);

            if (response.StatusCode == HttpStatusCode.NotFound)
                return Result.Success<IReadOnlyList<ChatEmote>>([]);
            if (!response.IsSuccessStatusCode)
                return Result.Failure<IReadOnlyList<ChatEmote>>(
                    $"{provider} returned {(int)response.StatusCode}.",
                    "EMOTE_PROVIDER_ERROR"
                );

            string json = await response.Content.ReadAsStringAsync(ct);
            return Result.Success(parse(json));
        }
        catch (Exception ex)
            when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return Result.Failure<IReadOnlyList<ChatEmote>>(ex.Message, "EMOTE_PROVIDER_ERROR");
        }
    }
}
