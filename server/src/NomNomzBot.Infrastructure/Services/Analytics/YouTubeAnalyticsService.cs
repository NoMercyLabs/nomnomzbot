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
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Analytics;
using NomNomzBot.Application.Contracts.YouTube;

namespace NomNomzBot.Infrastructure.Services.Analytics;

/// <summary>
/// Reads the channel's daily YouTube numbers from <c>reports.query</c>
/// (<c>youtubeanalytics.googleapis.com/v2/reports</c>, read against the live docs 2026-11-10). The answer is
/// a table: the column headers name each value of a row. The revenue metric needs the YouTube Partner
/// Program; a channel outside it gets a 403, so the read is repeated without the revenue.
/// </summary>
public sealed class YouTubeAnalyticsService(
    IHttpClientFactory httpClientFactory,
    IYouTubeAccessTokenProvider tokens,
    ILogger<YouTubeAnalyticsService> logger
) : IYouTubeAnalyticsService
{
    private const string ReportsUrl = "https://youtubeanalytics.googleapis.com/v2/reports";
    private const string ActivityMetrics =
        "views,estimatedMinutesWatched,subscribersGained,subscribersLost";
    private const string RevenueMetric = "estimatedRevenue";

    private readonly HttpClient _http = httpClientFactory.CreateClient("youtube");

    public async Task<Result<YouTubeAnalyticsDto>> GetDailyAsync(
        Guid broadcasterId,
        DateOnly from,
        DateOnly to,
        CancellationToken ct = default
    )
    {
        string? accessToken = await tokens.GetAccessTokenAsync(broadcasterId, ct);
        if (accessToken is null)
            return Result.Success(
                new YouTubeAnalyticsDto(YouTubeAnalyticsAccess.NotConnected, [], false)
            );

        (HttpStatusCode? status, ResultTable? table) = await ReadAsync(
            accessToken,
            from,
            to,
            $"{ActivityMetrics},{RevenueMetric}",
            ct
        );
        bool revenueAvailable = status is HttpStatusCode.OK;
        if (status is HttpStatusCode.Forbidden)
            (status, table) = await ReadAsync(accessToken, from, to, ActivityMetrics, ct);

        return status switch
        {
            HttpStatusCode.OK => Result.Success(
                new YouTubeAnalyticsDto(
                    YouTubeAnalyticsAccess.Allowed,
                    Days(table),
                    revenueAvailable
                )
            ),
            HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized => Result.Success(
                new YouTubeAnalyticsDto(YouTubeAnalyticsAccess.NotAllowed, [], false)
            ),
            _ => Result.Failure<YouTubeAnalyticsDto>(
                "YouTube is temporarily unavailable.",
                "SERVICE_UNAVAILABLE"
            ),
        };
    }

    private async Task<(HttpStatusCode? Status, ResultTable? Table)> ReadAsync(
        string accessToken,
        DateOnly from,
        DateOnly to,
        string metrics,
        CancellationToken ct
    )
    {
        string url =
            $"{ReportsUrl}?ids={Uri.EscapeDataString("channel==MINE")}"
            + $"&startDate={from:yyyy-MM-dd}&endDate={to:yyyy-MM-dd}"
            + $"&metrics={Uri.EscapeDataString(metrics)}&dimensions=day&sort=day";
        HttpRequestMessage request = new(HttpMethod.Get, url);
        request.Headers.Authorization = new("Bearer", accessToken);

        try
        {
            HttpResponseMessage response = await _http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogDebug("YouTube analytics read failed: {Status}", response.StatusCode);
                return (response.StatusCode, null);
            }

            return (
                response.StatusCode,
                await response.Content.ReadFromJsonAsync<ResultTable>(cancellationToken: ct)
            );
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogError(ex, "YouTube analytics read threw.");
            return (null, null);
        }
    }

    private static IReadOnlyList<YouTubeAnalyticsDayDto> Days(ResultTable? table)
    {
        Dictionary<string, int> column = (table?.ColumnHeaders ?? [])
            .Select((header, index) => (header.Name, index))
            .Where(c => c.Name is not null)
            .ToDictionary(c => c.Name!, c => c.index, StringComparer.Ordinal);
        if (!column.TryGetValue("day", out int day))
            return [];

        return
        [
            .. (table?.Rows ?? []).Select(row => new YouTubeAnalyticsDayDto(
                DateOnly.ParseExact(
                    row[day].GetString() ?? string.Empty,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture
                ),
                Whole(row, column, "views"),
                Whole(row, column, "estimatedMinutesWatched"),
                Whole(row, column, "subscribersGained"),
                Whole(row, column, "subscribersLost"),
                column.TryGetValue(RevenueMetric, out int revenue)
                    ? row[revenue].GetDecimal()
                    : null
            )),
        ];
    }

    private static long Whole(
        IReadOnlyList<JsonElement> row,
        Dictionary<string, int> column,
        string name
    ) => column.TryGetValue(name, out int index) ? (long)row[index].GetDouble() : 0;

    private sealed class ResultTable
    {
        [JsonPropertyName("columnHeaders")]
        public List<ColumnHeader>? ColumnHeaders { get; set; }

        [JsonPropertyName("rows")]
        public List<List<JsonElement>>? Rows { get; set; }
    }

    private sealed class ColumnHeader
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }
}
