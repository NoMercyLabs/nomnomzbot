// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text;
using System.Text.Json;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Lookups;

/// <summary>
/// MyMemory translation (no key: 5,000 characters per day anonymous). The query is at most 500 bytes, so a
/// longer text is not sent. A translation is kept for a day per text and language pair.
/// </summary>
internal sealed class MyMemoryTranslationClient(
    LookupHttp http,
    LookupCache cache,
    TimeSpan? timeout = null
) : ITranslationClient
{
    public const int MaxQueryBytes = 500;

    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(1);
    private readonly TimeSpan _timeout = timeout ?? TimeSpan.FromSeconds(3);

    public Task<string?> TranslateAsync(
        string text,
        string from,
        string to,
        CancellationToken cancellationToken
    ) =>
        Encoding.UTF8.GetByteCount(text) > MaxQueryBytes
            ? Task.FromResult<string?>(null)
            : cache.GetOrFetchAsync(
                "mymemory",
                $"{from}|{to}|{text}",
                Lifetime,
                () => FetchAsync(text, from, to, cancellationToken)
            );

    private async Task<string?> FetchAsync(
        string text,
        string from,
        string to,
        CancellationToken cancellationToken
    )
    {
        Uri uri = new(
            "https://api.mymemory.translated.net/get?q="
                + Uri.EscapeDataString(text)
                + "&langpair="
                + Uri.EscapeDataString(from + "|" + to)
        );
        using JsonDocument? body = await http.GetJsonAsync(uri, _timeout, cancellationToken);
        return
            body is not null
            && body.RootElement.TryGetProperty("responseData", out JsonElement data)
            && data.TryGetProperty("translatedText", out JsonElement translated)
            && translated.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(translated.GetString())
            ? translated.GetString()
            : null;
    }
}
