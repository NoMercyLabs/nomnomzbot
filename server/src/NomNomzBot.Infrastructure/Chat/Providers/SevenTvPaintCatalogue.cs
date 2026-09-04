// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Domain.Chat.ValueObjects;

namespace NomNomzBot.Infrastructure.Chat.Providers;

/// <inheritdoc />
/// <remarks>
/// <para>
/// Fetches the WHOLE paint catalogue in one request and answers from memory afterwards. There are about a
/// thousand paints and they change rarely, while a busy chat asks for the same handful thousands of times a
/// stream — a lookup per chatter would be slower and a reliable way to get rate-limited off 7TV.
/// </para>
/// <para>
/// The v3 <c>cosmetics</c> route this would once have used is gone (it answers "route not found"), so this
/// talks to the v4 GraphQL endpoint. Verified live 2026-09-04.
/// </para>
/// </remarks>
public sealed class SevenTvPaintCatalogue : ISevenTvPaintCatalogue
{
    private const string GraphQlUrl = "https://7tv.io/v4/gql";

    /// <summary>
    /// Asks only for what the mapper consumes. A broader query costs 7TV more to serve and gives this more
    /// wire shape to break on.
    /// </summary>
    private const string Query = """
        { paints { paints { id name data {
          layers { ty { __typename
            ... on PaintLayerTypeSingleColor { color { hex } }
            ... on PaintLayerTypeLinearGradient { angle repeating stops { at color { hex } } }
            ... on PaintLayerTypeRadialGradient { repeating shape stops { at color { hex } } }
            ... on PaintLayerTypeImage { images { url mime scale frameCount } } } }
          shadows { color { hex } offsetX offsetY blur } } } } }
        """;

    /// <summary>
    /// How long a loaded catalogue is trusted. Long, because paints are near-static and a stale one costs a
    /// chatter nothing worse than last week's cosmetic; short enough that a newly created paint appears the
    /// same day without a restart.
    /// </summary>
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(6);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SevenTvPaintCatalogue> _logger;

    // One in-flight load at a time. Without this, the first busy second of a stream fires a request per
    // chat message before any of them has populated the cache.
    private readonly SemaphoreSlim _loadGate = new(1, 1);
    private IReadOnlyDictionary<string, ChatPaint> _paints = new Dictionary<string, ChatPaint>();
    private DateTimeOffset _loadedAt = DateTimeOffset.MinValue;

    public SevenTvPaintCatalogue(
        IHttpClientFactory httpClientFactory,
        TimeProvider timeProvider,
        ILogger<SevenTvPaintCatalogue> logger
    )
    {
        _httpClientFactory = httpClientFactory;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ChatPaint?> GetAsync(
        string paintId,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrWhiteSpace(paintId))
            return null;

        await EnsureLoadedAsync(cancellationToken);
        return _paints.GetValueOrDefault(paintId);
    }

    private async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (_timeProvider.GetUtcNow() - _loadedAt < CacheLifetime)
            return;

        await _loadGate.WaitAsync(cancellationToken);
        try
        {
            // Re-check inside the gate: everyone queued behind the winner would otherwise re-fetch in turn.
            if (_timeProvider.GetUtcNow() - _loadedAt < CacheLifetime)
                return;

            IReadOnlyDictionary<string, ChatPaint>? loaded = await FetchAsync(cancellationToken);
            if (loaded is null)
            {
                // Keep whatever is already cached, even if it is stale or empty, and back off for a full
                // lifetime. Retrying per message while 7TV is down turns their outage into our own.
                _loadedAt = _timeProvider.GetUtcNow();
                return;
            }

            _paints = loaded;
            _loadedAt = _timeProvider.GetUtcNow();
            _logger.LogInformation("Loaded {Count} 7TV paints.", loaded.Count);
        }
        finally
        {
            _loadGate.Release();
        }
    }

    private async Task<IReadOnlyDictionary<string, ChatPaint>?> FetchAsync(
        CancellationToken cancellationToken
    )
    {
        try
        {
            HttpClient client = _httpClientFactory.CreateClient(ChatEmoteHttpClient.Name);
            using HttpResponseMessage response = await client.PostAsJsonAsync(
                GraphQlUrl,
                new { query = Query },
                cancellationToken
            );
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug("7TV paints: {Status}", (int)response.StatusCode);
                return null;
            }

            string json = await response.Content.ReadAsStringAsync(cancellationToken);
            return Parse(json);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A cosmetic is decoration. It must never cost a chatter their message, so every failure here is
            // swallowed to a debug line and the caller simply gets no paint.
            _logger.LogDebug(ex, "7TV paints could not be fetched");
            return null;
        }
    }

    /// <summary>Internal so the wire shape is tested against real captured payloads.</summary>
    internal static IReadOnlyDictionary<string, ChatPaint> Parse(string json)
    {
        Dictionary<string, ChatPaint> result = new(StringComparer.Ordinal);

        using JsonDocument document = JsonDocument.Parse(json);

        // Every hop goes through the null-tolerant reader: 7TV answers an error with {"data": null}, and
        // TryGetProperty on a JSON null THROWS rather than returning false.
        JsonElement? paints = document
            .RootElement.GetPropertyOrNull("data")
            .GetPropertyOrNull("paints")
            .GetPropertyOrNull("paints");
        if (paints is not { ValueKind: JsonValueKind.Array })
            return result;

        foreach (JsonElement paint in paints.Value.EnumerateArray())
        {
            string id = paint.GetPropertyOrNull("id")?.GetString() ?? string.Empty;
            string name = paint.GetPropertyOrNull("name")?.GetString() ?? string.Empty;
            JsonElement? paintData = paint.GetPropertyOrNull("data");

            ChatPaint? mapped = SevenTvPaintMapper.Map(
                id,
                name,
                ReadLayers(paintData),
                ReadShadows(paintData)
            );
            if (mapped is not null)
                result[id] = mapped;
        }

        return result;
    }

    private static IReadOnlyList<SevenTvPaintMapper.Layer> ReadLayers(JsonElement? data)
    {
        JsonElement? layers = data?.GetPropertyOrNull("layers");
        if (layers is not { ValueKind: JsonValueKind.Array })
            return [];

        List<SevenTvPaintMapper.Layer> result = [];
        foreach (JsonElement layer in layers.Value.EnumerateArray())
        {
            JsonElement? ty = layer.GetPropertyOrNull("ty");
            string type = ty?.GetPropertyOrNull("__typename")?.GetString() ?? string.Empty;
            if (type.Length == 0)
                continue;

            result.Add(
                new(
                    type,
                    Hex: ty?.GetPropertyOrNull("color")?.GetPropertyOrNull("hex")?.GetString(),
                    Angle: ty?.GetPropertyOrNull("angle")?.GetDoubleOrZero() ?? 0,
                    Repeating: ty?.GetPropertyOrNull("repeating")?.ValueKind == JsonValueKind.True,
                    Shape: ty?.GetPropertyOrNull("shape")?.GetString()?.ToLowerInvariant(),
                    Stops: ReadStops(ty),
                    Images: ReadImages(ty)
                )
            );
        }

        return result;
    }

    private static IReadOnlyList<SevenTvPaintMapper.ImageVariant> ReadImages(JsonElement? ty)
    {
        JsonElement? images = ty?.GetPropertyOrNull("images");
        if (images is not { ValueKind: JsonValueKind.Array })
            return [];

        List<SevenTvPaintMapper.ImageVariant> result = [];
        foreach (JsonElement image in images.Value.EnumerateArray())
        {
            string? url = image.GetPropertyOrNull("url")?.GetString();
            string? mime = image.GetPropertyOrNull("mime")?.GetString();
            if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(mime))
                continue;

            result.Add(
                new(
                    url,
                    mime,
                    (int)(image.GetPropertyOrNull("scale")?.GetDoubleOrZero() ?? 0),
                    (int)(image.GetPropertyOrNull("frameCount")?.GetDoubleOrZero() ?? 0)
                )
            );
        }

        return result;
    }

    private static IReadOnlyList<SevenTvPaintMapper.Stop> ReadStops(JsonElement? ty)
    {
        JsonElement? stops = ty?.GetPropertyOrNull("stops");
        if (stops is not { ValueKind: JsonValueKind.Array })
            return [];

        List<SevenTvPaintMapper.Stop> result = [];
        foreach (JsonElement stop in stops.Value.EnumerateArray())
        {
            string? hex = stop.GetPropertyOrNull("color")?.GetPropertyOrNull("hex")?.GetString();
            if (string.IsNullOrEmpty(hex))
                continue;

            result.Add(new(stop.GetPropertyOrNull("at")?.GetDoubleOrZero() ?? 0, hex));
        }

        return result;
    }

    private static IReadOnlyList<SevenTvPaintMapper.Shadow> ReadShadows(JsonElement? data)
    {
        JsonElement? shadows = data?.GetPropertyOrNull("shadows");
        if (shadows is not { ValueKind: JsonValueKind.Array })
            return [];

        List<SevenTvPaintMapper.Shadow> result = [];
        foreach (JsonElement shadow in shadows.Value.EnumerateArray())
        {
            string? hex = shadow.GetPropertyOrNull("color")?.GetPropertyOrNull("hex")?.GetString();
            if (string.IsNullOrEmpty(hex))
                continue;

            result.Add(
                new(
                    hex,
                    shadow.GetPropertyOrNull("offsetX")?.GetDoubleOrZero() ?? 0,
                    shadow.GetPropertyOrNull("offsetY")?.GetDoubleOrZero() ?? 0,
                    shadow.GetPropertyOrNull("blur")?.GetDoubleOrZero() ?? 0
                )
            );
        }

        return result;
    }
}

/// <summary>
/// Null-tolerant <see cref="JsonElement"/> reads. 7TV's GraphQL omits fields that do not apply to a layer
/// kind rather than nulling them, so every read here has to cope with the property simply not being there.
/// </summary>
internal static class SevenTvJsonExtensions
{
    public static JsonElement? GetPropertyOrNull(this JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out JsonElement value)
        && value.ValueKind != JsonValueKind.Null
            ? value
            : null;

    public static JsonElement? GetPropertyOrNull(this JsonElement? element, string name) =>
        element?.GetPropertyOrNull(name);

    /// <summary>A non-number reads as zero rather than throwing — a malformed paint must not kill the load.</summary>
    public static double GetDoubleOrZero(this JsonElement element) =>
        element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out double value)
            ? value
            : 0;
}
