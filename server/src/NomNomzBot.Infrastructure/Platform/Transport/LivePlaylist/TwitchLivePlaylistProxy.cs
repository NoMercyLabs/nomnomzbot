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

using System.Net;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;

namespace NomNomzBot.Infrastructure.Platform.Transport.LivePlaylist;

/// <summary>
/// Fetches one allowlisted Twitch playlist with a fresh request (no header, cookie or token of ours) and a short
/// timeout, and hands it back rewritten for the proxy. A redirect is a failure, never followed: its target
/// could be off the allowlist. A body larger than any real playlist is a failure too.
/// </summary>
public sealed class TwitchLivePlaylistProxy(IHttpClientFactory httpClientFactory)
    : ITwitchLivePlaylistProxy
{
    public const string ClientName = "twitch-live-playlist";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private const int MaxPlaylistBytes = 512 * 1024;

    public async Task<Result<string>> FetchAsync(
        string encodedAddress,
        CancellationToken cancellationToken
    )
    {
        Uri? address = TwitchLivePlaylistAddress.Decode(encodedAddress);
        if (address is null)
            return Result.Failure<string>(
                "The address is not a Twitch playlist.",
                ITwitchLivePlaylistProxy.InvalidAddress
            );

        try
        {
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken
            );
            timeout.CancelAfter(Timeout);

            HttpClient client = httpClientFactory.CreateClient(ClientName);
            using HttpRequestMessage request = new(HttpMethod.Get, address);
            using HttpResponseMessage response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token
            );

            if (response.StatusCode == HttpStatusCode.NotFound)
                return Result.Failure<string>(
                    "Twitch has no stream at this address.",
                    TwitchErrorCodes.NotFound
                );

            if (
                response.StatusCode != HttpStatusCode.OK
                || response.Content.Headers.ContentLength > MaxPlaylistBytes
            )
                return Upstream($"Twitch answered {(int)response.StatusCode}.");

            string body = await response.Content.ReadAsStringAsync(timeout.Token);
            if (body.Length > MaxPlaylistBytes)
                return Upstream("The playlist is larger than any real playlist.");

            return Result.Success(HlsPlaylistRewriter.Rewrite(body, address));
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested)
                throw;

            return Upstream("Twitch did not answer in time.");
        }
    }

    private static Result<string> Upstream(string message) =>
        Result.Failure<string>(message, TwitchErrorCodes.Transport);
}
