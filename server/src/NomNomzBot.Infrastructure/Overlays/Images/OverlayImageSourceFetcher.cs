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

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Overlays.Services;

namespace NomNomzBot.Infrastructure.Overlays.Images;

/// <summary>
/// Downloads one allow-listed image. Redirects are followed by hand (the client's own redirect handling is off) so
/// every hop passes <see cref="OverlayImageSourcePolicy"/> before a request is sent, and the body is counted while
/// it streams so an oversize source is dropped at the cap, not after it has all arrived.
/// </summary>
internal sealed class OverlayImageSourceFetcher(IHttpClientFactory httpClients)
{
    public const string ClientName = "overlay-image-source";
    public const int MaxBytes = 10 * 1024 * 1024;
    public const int MaxRedirects = 3;

    public async Task<Result<byte[]>> FetchAsync(
        Uri source,
        string? ownOrigin,
        CancellationToken ct
    )
    {
        HttpClient client = httpClients.CreateClient(ClientName);
        Uri current = source;
        try
        {
            for (int hop = 0; ; hop++)
            {
                using HttpRequestMessage request = new(HttpMethod.Get, current);
                using HttpResponseMessage response = await client.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    ct
                );

                if (IsRedirect(response, out Uri? target))
                {
                    Uri? next =
                        hop < MaxRedirects
                            ? OverlayImageSourcePolicy.Allow(
                                new Uri(current, target).AbsoluteUri,
                                ownOrigin
                            )
                            : null;
                    if (next is null)
                        return Refused("The image redirects somewhere that is not allowed.");

                    current = next;
                    continue;
                }

                return response.IsSuccessStatusCode
                    ? await ReadCappedAsync(response, ct)
                    : FetchFailed($"The image host answered {(int)response.StatusCode}.");
            }
        }
        catch (HttpRequestException ex)
        {
            return FetchFailed(ex.Message);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return FetchFailed("The image host took too long.");
        }
    }

    private static bool IsRedirect(HttpResponseMessage response, out Uri target)
    {
        target = response.Headers.Location!;
        return (int)response.StatusCode is >= 300 and < 400
            && response.Headers.Location is not null;
    }

    private static async Task<Result<byte[]>> ReadCappedAsync(
        HttpResponseMessage response,
        CancellationToken ct
    )
    {
        if (response.Content.Headers.ContentLength > MaxBytes)
            return TooLarge();

        await using System.IO.Stream body = await response.Content.ReadAsStreamAsync(ct);
        using MemoryStream bytes = new();
        byte[] buffer = new byte[81920];
        int read;
        while ((read = await body.ReadAsync(buffer, ct)) > 0)
        {
            if (bytes.Length + read > MaxBytes)
                return TooLarge();
            bytes.Write(buffer, 0, read);
        }

        return Result.Success(bytes.ToArray());
    }

    private static Result<byte[]> TooLarge() =>
        Result.Failure<byte[]>(
            "The image is larger than 10 MB.",
            IOverlayImageOptimizer.ErrorCodes.TooLarge
        );

    private static Result<byte[]> Refused(string message) =>
        Result.Failure<byte[]>(message, IOverlayImageOptimizer.ErrorCodes.SourceNotAllowed);

    private static Result<byte[]> FetchFailed(string message) =>
        Result.Failure<byte[]>(message, IOverlayImageOptimizer.ErrorCodes.FetchFailed);
}
