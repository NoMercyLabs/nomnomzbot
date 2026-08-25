// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Sound.Services;
using NomNomzBot.Infrastructure.Platform;

namespace NomNomzBot.Infrastructure.Sound;

/// <summary>
/// Self-host implementation of <see cref="ISoundClipStore"/>. Clips are persisted under
/// <c>NOMNOMZ_DATA_DIR/sound-clips/{broadcasterId}/</c>. The storage key is the relative path
/// <c>{broadcasterId}/{uniqueFileName}</c>, which doubles as the path fragment for the playback URL.
/// </summary>
internal sealed class DiskSoundClipStore : ISoundClipStore
{
    private readonly string _root = SelfHostDataPaths.SoundClipsDirectory;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IConfiguration _configuration;

    public DiskSoundClipStore(
        IHttpContextAccessor httpContextAccessor,
        IConfiguration configuration
    )
    {
        _httpContextAccessor = httpContextAccessor;
        _configuration = configuration;
    }

    public async Task<Result<string>> PutAsync(
        Guid broadcasterId,
        string fileName,
        System.IO.Stream content,
        string mimeType,
        CancellationToken ct = default
    )
    {
        string channelDir = Path.Combine(_root, broadcasterId.ToString("N"));
        Directory.CreateDirectory(channelDir);

        string ext = Path.GetExtension(fileName);
        string uniqueName = $"{Guid.NewGuid():N}{ext}";
        string fullPath = Path.Combine(channelDir, uniqueName);
        string storageKey = $"{broadcasterId:N}/{uniqueName}";

        await using FileStream fs = File.Create(fullPath);
        await content.CopyToAsync(fs, ct);

        return Result<string>.Success(storageKey);
    }

    public Task<Result<System.IO.Stream>> OpenAsync(
        string storageKey,
        CancellationToken ct = default
    )
    {
        string fullPath = Path.Combine(_root, storageKey.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(fullPath))
            return Task.FromResult(Result<System.IO.Stream>.Failure("Clip file not found."));

        System.IO.Stream stream = File.OpenRead(fullPath);
        return Task.FromResult(Result<System.IO.Stream>.Success(stream));
    }

    public Task<Result> DeleteAsync(string storageKey, CancellationToken ct = default)
    {
        string fullPath = Path.Combine(_root, storageKey.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(fullPath))
            File.Delete(fullPath);

        return Task.FromResult(Result.Success());
    }

    public Task<Result<string>> GetPlaybackUrlAsync(
        string storageKey,
        CancellationToken ct = default
    )
    {
        // Build an absolute URL to the sound-clips serve endpoint. Prefer the configured public base URL
        // (App:BaseUrl — the same setting the Twitch OAuth redirect URIs are computed from) over the current
        // request's scheme/host: behind the Cloudflare tunnel/reverse proxy this request always arrives at
        // Kestrel as plain http, so trusting ctx.Request.Scheme silently produced http:// playback URLs even
        // though the site is served over https — the overlay page's CSP (media-src 'self' https: ...) then
        // silently blocked every TTS/sound-clip playback in the browser. Only overlay/OBS pushes are affected
        // (channels/{id}/sound-clips endpoints return relative previewUrl, unaffected); this widened fallback
        // to the request scheme covers local dev where App:BaseUrl may be unset.
        string? configuredBaseUrl = _configuration["App:BaseUrl"]?.TrimEnd('/');
        HttpContext? ctx = _httpContextAccessor.HttpContext;
        string baseUrl =
            !string.IsNullOrWhiteSpace(configuredBaseUrl) ? configuredBaseUrl
            : ctx is not null ? ForwardedOrigin(ctx.Request)
            : "http://localhost:5080";

        string url = $"{baseUrl}/api/v1/sound-clips/stream/{Uri.EscapeDataString(storageKey)}";
        return Task.FromResult(Result<string>.Success(url));
    }

    /// <summary>
    /// The origin the BROWSER used, honouring the reverse proxy's <c>X-Forwarded-Proto</c>/<c>-Host</c>.
    /// Falling back to the raw request scheme yields <c>http://</c> behind a TLS-terminating proxy, and the
    /// overlay's CSP (<c>media-src 'self' https:</c>) then blocks every clip.
    /// </summary>
    private static string ForwardedOrigin(HttpRequest request)
    {
        string forwardedHost = request.Headers["X-Forwarded-Host"].ToString();
        string forwardedProto = request.Headers["X-Forwarded-Proto"].ToString();
        string host = !string.IsNullOrWhiteSpace(forwardedHost)
            ? forwardedHost.Split(',')[0].Trim()
            : request.Host.ToString();
        string scheme = !string.IsNullOrWhiteSpace(forwardedProto)
            ? forwardedProto.Split(',')[0].Trim()
            : request.Scheme;
        return $"{scheme}://{host}";
    }
}
