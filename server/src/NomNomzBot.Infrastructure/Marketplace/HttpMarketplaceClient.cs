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
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Marketplace;
using NomNomzBot.Application.Marketplace.Services;

namespace NomNomzBot.Infrastructure.Marketplace;

/// <summary>
/// <see cref="IMarketplaceClient"/> over the hosted marketplace's wire API (marketplace.md §4):
/// <c>GET /v1/items</c>, <c>GET /v1/items/{id}</c>, <c>GET /v1/items/{id}/download</c>,
/// <c>POST /v1/publish</c> (multipart), <c>GET /v1/submissions/{id}</c>. Browse/download are anonymous;
/// publish + submission status carry the channel's vaulted publisher token as a bearer. Every transport
/// problem (unconfigured URL, DNS, timeout, 5xx) surfaces as a typed <c>MARKETPLACE_UNAVAILABLE</c>
/// failure — this client never throws to callers. Browse calls ride the 10 s client; download/publish ride
/// the 60 s transfer client.
/// </summary>
public sealed class HttpMarketplaceClient : IMarketplaceClient
{
    /// <summary>Named client for the small catalog calls — 10 s ceiling.</summary>
    public const string BrowseClientName = "marketplace";

    /// <summary>Named client for the bundle transfers (download/publish) — 60 s ceiling.</summary>
    public const string TransferClientName = "marketplace-transfer";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMarketplacePublisherTokenService _publisherTokens;
    private readonly IOptions<MarketplaceOptions> _options;
    private readonly ILogger<HttpMarketplaceClient> _logger;

    public HttpMarketplaceClient(
        IHttpClientFactory httpClientFactory,
        IMarketplacePublisherTokenService publisherTokens,
        IOptions<MarketplaceOptions> options,
        ILogger<HttpMarketplaceClient> logger
    )
    {
        _httpClientFactory = httpClientFactory;
        _publisherTokens = publisherTokens;
        _options = options;
        _logger = logger;
    }

    // ── Browse (anonymous) ──────────────────────────────────────────────────────

    public async Task<Result<PagedList<MarketplaceItemDto>>> SearchAsync(
        MarketplaceQuery query,
        PaginationParams? pagination = null,
        CancellationToken ct = default
    )
    {
        if (BaseUrl() is not string baseUrl)
            return Unavailable<PagedList<MarketplaceItemDto>>("no marketplace URL is configured");

        PaginationParams page = pagination ?? new PaginationParams();
        string url = $"{baseUrl}/v1/items{QueryString(query, page)}";
        try
        {
            HttpClient http = _httpClientFactory.CreateClient(BrowseClientName);
            using HttpResponseMessage response = await http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
                return Unavailable<PagedList<MarketplaceItemDto>>(
                    $"the marketplace answered {(int)response.StatusCode}"
                );

            WireItemPage? wire = BundleConventions.Deserialize<WireItemPage>(
                await response.Content.ReadAsStringAsync(ct)
            );
            if (wire is null)
                return Unavailable<PagedList<MarketplaceItemDto>>(
                    "the marketplace answered with a malformed page"
                );

            List<MarketplaceItemDto> items = wire.Items.Select(ToDto).ToList();
            return Result.Success(
                new PagedList<MarketplaceItemDto>(items, wire.Page, wire.PageSize, wire.TotalCount)
            );
        }
        catch (Exception ex) when (IsTransport(ex, ct))
        {
            return LogUnavailable<PagedList<MarketplaceItemDto>>("search", ex);
        }
    }

    public async Task<Result<MarketplaceItemDto>> GetItemAsync(
        string itemId,
        CancellationToken ct = default
    )
    {
        if (BaseUrl() is not string baseUrl)
            return Unavailable<MarketplaceItemDto>("no marketplace URL is configured");

        try
        {
            HttpClient http = _httpClientFactory.CreateClient(BrowseClientName);
            using HttpResponseMessage response = await http.GetAsync(
                $"{baseUrl}/v1/items/{Uri.EscapeDataString(itemId)}",
                ct
            );
            if (response.StatusCode == HttpStatusCode.NotFound)
                return Result.Failure<MarketplaceItemDto>(
                    $"Marketplace item '{itemId}' was not found.",
                    "NOT_FOUND"
                );
            if (!response.IsSuccessStatusCode)
                return Unavailable<MarketplaceItemDto>(
                    $"the marketplace answered {(int)response.StatusCode}"
                );

            WireItem? wire = BundleConventions.Deserialize<WireItem>(
                await response.Content.ReadAsStringAsync(ct)
            );
            return wire is null
                ? Unavailable<MarketplaceItemDto>("the marketplace answered with a malformed item")
                : Result.Success(ToDto(wire));
        }
        catch (Exception ex) when (IsTransport(ex, ct))
        {
            return LogUnavailable<MarketplaceItemDto>("item", ex);
        }
    }

    public async Task<Result<System.IO.Stream>> DownloadAsync(
        string itemId,
        CancellationToken ct = default
    )
    {
        if (BaseUrl() is not string baseUrl)
            return Unavailable<System.IO.Stream>("no marketplace URL is configured");

        try
        {
            HttpClient http = _httpClientFactory.CreateClient(TransferClientName);
            using HttpResponseMessage response = await http.GetAsync(
                $"{baseUrl}/v1/items/{Uri.EscapeDataString(itemId)}/download",
                HttpCompletionOption.ResponseHeadersRead,
                ct
            );
            if (response.StatusCode == HttpStatusCode.NotFound)
                return Result.Failure<System.IO.Stream>(
                    $"Marketplace item '{itemId}' was not found.",
                    "NOT_FOUND"
                );
            if (!response.IsSuccessStatusCode)
                return Unavailable<System.IO.Stream>(
                    $"the marketplace answered {(int)response.StatusCode}"
                );

            // Buffer under the bundle cap so a hostile/miscapped download can never balloon memory —
            // the import re-checks the same cap, this just stops the bytes at the door.
            MemoryStream buffer = new();
            await using System.IO.Stream body = await response.Content.ReadAsStreamAsync(ct);
            byte[] chunk = new byte[81920];
            int read;
            while ((read = await body.ReadAsync(chunk, ct)) > 0)
            {
                if (buffer.Length + read > BundleFormat.MaxBundleBytes)
                    return Result.Failure<System.IO.Stream>(
                        $"The bundle is larger than {BundleFormat.MaxBundleBytes / (1024 * 1024)} MB.",
                        "BUNDLE_TOO_LARGE"
                    );
                buffer.Write(chunk, 0, read);
            }
            buffer.Position = 0;
            return Result.Success<System.IO.Stream>(buffer);
        }
        catch (Exception ex) when (IsTransport(ex, ct))
        {
            return LogUnavailable<System.IO.Stream>("download", ex);
        }
    }

    // ── Publish (publisher token) ───────────────────────────────────────────────

    public async Task<Result<PublishSubmissionDto>> PublishAsync(
        Guid broadcasterId,
        System.IO.Stream zip,
        PublishMetadata metadata,
        CancellationToken ct = default
    )
    {
        if (BaseUrl() is not string baseUrl)
            return Unavailable<PublishSubmissionDto>("no marketplace URL is configured");

        string? token = await _publisherTokens.GetPublisherTokenAsync(broadcasterId, ct);
        if (token is null)
            return Result.Failure<PublishSubmissionDto>(
                "No marketplace publisher token is stored for this channel — add one under marketplace settings first.",
                "MARKETPLACE_NO_PUBLISHER_TOKEN"
            );

        try
        {
            using MultipartFormDataContent form = new();
            StringContent metadataPart = new(BundleConventions.Serialize(metadata));
            metadataPart.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            form.Add(metadataPart, "metadata");
            StreamContent zipPart = new(zip);
            zipPart.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
            form.Add(zipPart, "bundle", "bundle.zip");

            using HttpRequestMessage request = new(HttpMethod.Post, $"{baseUrl}/v1/publish");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Content = form;

            HttpClient http = _httpClientFactory.CreateClient(TransferClientName);
            using HttpResponseMessage response = await http.SendAsync(request, ct);
            return await ReadSubmissionAsync(response, ct);
        }
        catch (Exception ex) when (IsTransport(ex, ct))
        {
            return LogUnavailable<PublishSubmissionDto>("publish", ex);
        }
    }

    public async Task<Result<PublishSubmissionDto>> GetSubmissionAsync(
        Guid broadcasterId,
        string submissionId,
        CancellationToken ct = default
    )
    {
        if (BaseUrl() is not string baseUrl)
            return Unavailable<PublishSubmissionDto>("no marketplace URL is configured");

        string? token = await _publisherTokens.GetPublisherTokenAsync(broadcasterId, ct);
        if (token is null)
            return Result.Failure<PublishSubmissionDto>(
                "No marketplace publisher token is stored for this channel — add one under marketplace settings first.",
                "MARKETPLACE_NO_PUBLISHER_TOKEN"
            );

        try
        {
            using HttpRequestMessage request = new(
                HttpMethod.Get,
                $"{baseUrl}/v1/submissions/{Uri.EscapeDataString(submissionId)}"
            );
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            HttpClient http = _httpClientFactory.CreateClient(BrowseClientName);
            using HttpResponseMessage response = await http.SendAsync(request, ct);
            return await ReadSubmissionAsync(response, ct);
        }
        catch (Exception ex) when (IsTransport(ex, ct))
        {
            return LogUnavailable<PublishSubmissionDto>("submission", ex);
        }
    }

    // ── Wire mechanics ──────────────────────────────────────────────────────────

    private static async Task<Result<PublishSubmissionDto>> ReadSubmissionAsync(
        HttpResponseMessage response,
        CancellationToken ct
    )
    {
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            return Result.Failure<PublishSubmissionDto>(
                "The marketplace rejected the publisher token — re-enter it under marketplace settings.",
                "MARKETPLACE_AUTH_FAILED"
            );
        if (response.StatusCode == HttpStatusCode.NotFound)
            return Result.Failure<PublishSubmissionDto>(
                "The submission was not found.",
                "NOT_FOUND"
            );
        if (!response.IsSuccessStatusCode)
            return Unavailable<PublishSubmissionDto>(
                $"the marketplace answered {(int)response.StatusCode}"
            );

        WireSubmission? wire = BundleConventions.Deserialize<WireSubmission>(
            await response.Content.ReadAsStringAsync(ct)
        );
        return wire is null
            ? Unavailable<PublishSubmissionDto>(
                "the marketplace answered with a malformed submission"
            )
            : Result.Success(
                new PublishSubmissionDto(wire.SubmissionId, wire.Status, wire.ReviewNote)
            );
    }

    private string? BaseUrl()
    {
        string url = _options.Value.Url;
        return string.IsNullOrWhiteSpace(url) ? null : url.TrimEnd('/');
    }

    private static string QueryString(MarketplaceQuery query, PaginationParams page)
    {
        List<string> parts = [$"page={page.Page}", $"pageSize={page.PageSize}"];
        if (!string.IsNullOrWhiteSpace(query.Search))
            parts.Add($"q={Uri.EscapeDataString(query.Search)}");
        if (!string.IsNullOrWhiteSpace(query.Type))
            parts.Add($"type={Uri.EscapeDataString(query.Type)}");
        if (query.Tags is { Count: > 0 } tags)
            parts.Add($"tags={Uri.EscapeDataString(string.Join(',', tags))}");
        return $"?{string.Join('&', parts)}";
    }

    /// <summary>
    /// The transport faults that mean "the marketplace is unreachable" — connection failures, broken
    /// streams, and the client-side timeout (a <see cref="TaskCanceledException"/> the CALLER did not ask
    /// for; genuine caller cancellation propagates).
    /// </summary>
    private static bool IsTransport(Exception ex, CancellationToken ct) =>
        ex is HttpRequestException or IOException
        || (ex is TaskCanceledException && !ct.IsCancellationRequested);

    private static Result<T> Unavailable<T>(string why) =>
        Result.Failure<T>($"The marketplace is unavailable: {why}.", "MARKETPLACE_UNAVAILABLE");

    private Result<T> LogUnavailable<T>(string operation, Exception ex)
    {
        _logger.LogWarning(
            ex,
            "Marketplace {Operation} call failed: {Message}",
            operation,
            ex.Message
        );
        return Unavailable<T>(ex.Message);
    }

    private static MarketplaceItemDto ToDto(WireItem wire) =>
        new(
            wire.ItemId,
            wire.Name,
            wire.Author,
            wire.Version,
            wire.Summary ?? string.Empty,
            wire.Type ?? string.Empty,
            wire.Tags ?? [],
            wire.Capabilities ?? [],
            wire.Rating,
            wire.Installs
        );

    // The versioned /v1 wire shapes (camelCase JSON, marketplace.md §4). Nullable where a lean catalog
    // may omit the field; the DTO mapping normalises to empty.
    private sealed record WireItem(
        string ItemId,
        string Name,
        string Author,
        string Version,
        string? Summary,
        string? Type,
        IReadOnlyList<string>? Tags,
        IReadOnlyList<string>? Capabilities,
        double Rating,
        long Installs
    );

    private sealed record WireItemPage(
        List<WireItem> Items,
        int Page,
        int PageSize,
        int TotalCount
    );

    private sealed record WireSubmission(string SubmissionId, string Status, string? ReviewNote);
}
