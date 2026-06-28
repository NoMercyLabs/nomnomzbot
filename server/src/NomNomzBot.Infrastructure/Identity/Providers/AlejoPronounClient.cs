// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using NomNomzBot.Application.Identity.Services;
using Polly;

namespace NomNomzBot.Infrastructure.Identity.Providers;

/// <summary>
/// Fetches the live pronoun reference set from the alejo.io pronoun API
/// (<c>GET https://api.pronouns.alejo.io/v1/pronouns</c>) — an id-keyed object of pronouns, the
/// same source the Twitch pronoun chat plugins read. Best-effort: any transport, status, or JSON
/// error degrades to <c>null</c> (the seeder then upserts its bundled fallback) — it never throws.
/// </summary>
public sealed class AlejoPronounClient : IAlejoPronounClient
{
    private const string PronounsUrl = "https://api.pronouns.alejo.io/v1/pronouns";
    private const string UsersBaseUrl = "https://api.pronouns.alejo.io/v1/users/";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AlejoPronounClient> _logger;

    public AlejoPronounClient(
        IHttpClientFactory httpClientFactory,
        ILogger<AlejoPronounClient> logger
    )
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<IReadOnlyList<PronounRecord>?> FetchAsync(CancellationToken ct = default)
    {
        try
        {
            HttpClient client = _httpClientFactory.CreateClient(AlejoHttpClient.Name);
            using HttpResponseMessage response = await client.GetAsync(PronounsUrl, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "alejo.io pronouns fetch returned {StatusCode}; falling back to the bundled set.",
                    (int)response.StatusCode
                );
                return null;
            }

            string json = await response.Content.ReadAsStringAsync(ct);
            return Parse(json);
        }
        // ExecutionRejectedException covers the resilience pipeline rejecting the call (open circuit /
        // Polly timeout); all of these degrade to the bundled fallback rather than crashing the seed.
        catch (Exception ex)
            when (ex
                    is HttpRequestException
                        or TaskCanceledException
                        or JsonException
                        or ExecutionRejectedException
            )
        {
            _logger.LogWarning(
                ex,
                "alejo.io pronouns fetch failed; falling back to the bundled set."
            );
            return null;
        }
    }

    public async Task<AlejoUserPronoun?> LookupUserAsync(
        string twitchLogin,
        CancellationToken ct = default
    )
    {
        try
        {
            HttpClient client = _httpClientFactory.CreateClient(AlejoHttpClient.Name);
            using HttpResponseMessage response = await client.GetAsync(
                $"{UsersBaseUrl}{Uri.EscapeDataString(twitchLogin)}",
                ct
            );
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return null;
            if (!response.IsSuccessStatusCode)
                return null;

            string json = await response.Content.ReadAsStringAsync(ct);
            AlejoUserResponse? user = JsonConvert.DeserializeObject<AlejoUserResponse>(json);
            if (user is null || string.IsNullOrWhiteSpace(user.PronounId))
                return null;

            return new AlejoUserPronoun(user.PronounId, user.AltPronounId);
        }
        catch (Exception ex)
            when (ex
                    is HttpRequestException
                        or TaskCanceledException
                        or JsonException
                        or ExecutionRejectedException
            )
        {
            _logger.LogDebug(ex, "alejo.io user lookup failed for {Login}.", twitchLogin);
            return null;
        }
    }

    /// <summary>
    /// Maps the id-keyed alejo payload (<c>{"theythem":{"subject":"They","object":"Them",...}}</c>)
    /// into pronoun records, dropping any entry missing a subject or object. Internal for the
    /// mapping behaviour test.
    /// </summary>
    internal static IReadOnlyList<PronounRecord> Parse(string json)
    {
        Dictionary<string, AlejoPronoun>? payload = JsonConvert.DeserializeObject<
            Dictionary<string, AlejoPronoun>
        >(json);
        if (payload is null)
            return [];

        List<PronounRecord> records = new(payload.Count);
        foreach (KeyValuePair<string, AlejoPronoun> entry in payload)
        {
            if (
                string.IsNullOrWhiteSpace(entry.Value.Subject)
                || string.IsNullOrWhiteSpace(entry.Value.Object)
            )
                continue;

            records.Add(
                new PronounRecord(
                    entry.Value.Subject,
                    entry.Value.Object,
                    entry.Value.Singular,
                    entry.Key
                )
            );
        }

        return records;
    }

    // The alejo pronoun shape (Newtonsoft, case-insensitive). "name" is the id (echoed in the key) and
    // is unused — the seeded Name is derived from subject/object so it matches the bundled slash form.
    private sealed class AlejoPronoun
    {
        public string Subject { get; set; } = string.Empty;
        public string Object { get; set; } = string.Empty;
        public bool Singular { get; set; }
    }

    // alejo.io GET /v1/users/{login} response shape.
    // "pronoun_id" is the primary key (e.g. "theythem"); "alt_pronoun_id" is the optional secondary.
    private sealed class AlejoUserResponse
    {
        [JsonProperty("channel_id")]
        public string? ChannelId { get; set; }

        [JsonProperty("pronoun_id")]
        public string? PronounId { get; set; }

        [JsonProperty("alt_pronoun_id")]
        public string? AltPronounId { get; set; }
    }
}
