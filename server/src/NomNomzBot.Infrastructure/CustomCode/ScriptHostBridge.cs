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
using Newtonsoft.Json;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.CustomCode;
using NomNomzBot.Application.Economy.Services;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Infrastructure.Sandbox;

namespace NomNomzBot.Infrastructure.CustomCode;

/// <summary>
/// The per-execution host-dispatch bridge (custom-code.md §3.1/§6.2) — the only path from a granted <c>bot.*</c>
/// import to host code. Bound to exactly one <c>BroadcasterId</c> (host-side; never readable by the guest); each
/// resolved delegate is primitive-in / primitive-out and tenant-scoped to that channel. <c>chat.send</c>/
/// <c>chat.reply</c> dispatch to the channel's Helix chat provider (bot token host-side, never in the guest; the
/// provider resolves the tenant Guid → Twitch id internally); <c>economy.read</c> reads this channel's ledger;
/// <c>music.queue</c> enqueues a request and <c>music.nowPlaying</c> reads the current track; <c>user.get</c>
/// returns a viewer's public profile (never their email/PII); <c>http.fetch</c> does a capped GET through the
/// SSRF-hardened egress client. Every dispatch fails closed (returns a safe primitive).
/// </summary>
public sealed class ScriptHostBridge(
    Guid broadcasterId,
    string triggeringUserId,
    IChatProvider chatProvider,
    ICurrencyAccountService currencyService,
    IMusicService musicService,
    IUserService userService,
    IHttpClientFactory httpClientFactory
) : IScriptHostBridge
{
    private const int MaxResponseBytes = 256 * 1024;

    public HostImportDelegate Resolve(string capabilityKey) =>
        capabilityKey switch
        {
            "chat.send" or "chat.reply" => SendChat,
            "economy.read" => ReadBalance,
            "music.queue" => QueueMusic,
            "music.nowPlaying" => ReadNowPlaying,
            "user.get" => GetUser,
            "http.fetch" => Fetch,
            _ => static (_, _, _) => null, // granted-but-unwired caps no-op; the grant already gated access
        };

    private string? ReadNowPlaying(
        string capabilityKey,
        IReadOnlyList<string> args,
        CancellationToken ct
    )
    {
        // Read-only current-track snapshot for THIS channel; null when nothing is playing (guest gets a JSON
        // string it can JSON.parse). Provider token stays host-side — the guest only ever sees the values.
        NowPlaying? nowPlaying = musicService
            .GetNowPlayingAsync(broadcasterId.ToString(), ct)
            .GetAwaiter()
            .GetResult();
        if (nowPlaying is null)
            return null;

        return JsonConvert.SerializeObject(
            new
            {
                track = nowPlaying.TrackName,
                artist = nowPlaying.Artist,
                album = nowPlaying.Album,
                durationMs = nowPlaying.DurationMs,
                progressMs = nowPlaying.ProgressMs,
                isPlaying = nowPlaying.IsPlaying,
                requestedBy = nowPlaying.RequestedBy,
                provider = nowPlaying.Provider,
            }
        );
    }

    private string? GetUser(string capabilityKey, IReadOnlyList<string> args, CancellationToken ct)
    {
        // The optional id arg names a user; default to the trigger user (host-supplied, never guest-forged).
        // Public profile only — id/username/displayName/avatar. Email and other PII are deliberately withheld.
        string subject =
            args.Count > 0 && !string.IsNullOrWhiteSpace(args[0]) ? args[0] : triggeringUserId;
        if (string.IsNullOrWhiteSpace(subject))
            return null;

        Result<UserDto> user = userService.GetAsync(subject, ct).GetAwaiter().GetResult();
        if (user.IsFailure)
            return null;

        return JsonConvert.SerializeObject(
            new
            {
                id = user.Value.Id,
                username = user.Value.Username,
                displayName = user.Value.DisplayName,
                avatarUrl = user.Value.ProfileImageUrl,
            }
        );
    }

    private string? Fetch(string capabilityKey, IReadOnlyList<string> args, CancellationToken ct)
    {
        if (
            args.Count == 0
            || !Uri.TryCreate(args[0], UriKind.Absolute, out Uri? uri)
            || uri.Scheme != Uri.UriSchemeHttps
        )
            return null;

        try
        {
            // The egress client resolves-then-pins + blocks non-public IPs + is https-only (SSRF-hardened);
            // bounded by the script's cancellation budget. Read is capped so a huge body can't flood the guest.
            HttpClient client = httpClientFactory.CreateClient(EgressHttpClient.Name);
            using HttpResponseMessage response = client
                .GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct)
                .GetAwaiter()
                .GetResult();
            if (!response.IsSuccessStatusCode)
                return null;

            using System.IO.Stream body = response.Content.ReadAsStream(ct);
            byte[] buffer = new byte[MaxResponseBytes];
            int total = 0;
            int read;
            while (
                total < MaxResponseBytes
                && (read = body.Read(buffer, total, MaxResponseBytes - total)) > 0
            )
                total += read;
            return Encoding.UTF8.GetString(buffer, 0, total);
        }
        catch
        {
            return null; // blocked egress / timeout / transport fault — fail closed
        }
    }

    private string? QueueMusic(
        string capabilityKey,
        IReadOnlyList<string> args,
        CancellationToken ct
    )
    {
        if (args.Count == 0 || string.IsNullOrWhiteSpace(args[0]))
            return "false";

        // The music service takes the raw query (title/artist/link) and resolves + enqueues it host-side,
        // attributing the request to the trigger user; the bot's provider token never reaches the guest.
        bool queued = musicService
            .AddToQueueAsync(broadcasterId.ToString(), args[0], triggeringUserId, ct)
            .GetAwaiter()
            .GetResult();
        return queued ? "true" : "false";
    }

    private string? ReadBalance(
        string capabilityKey,
        IReadOnlyList<string> args,
        CancellationToken ct
    )
    {
        // The optional userId arg names a viewer of THIS channel; default to the trigger user (host-validated).
        string subject =
            args.Count > 0 && !string.IsNullOrWhiteSpace(args[0]) ? args[0] : triggeringUserId;
        if (!Guid.TryParse(subject, out Guid viewerUserId))
            return "0";

        Result<long> balance = currencyService
            .GetBalanceAsync(broadcasterId, viewerUserId, ct)
            .GetAwaiter()
            .GetResult();
        return balance.IsSuccess ? balance.Value.ToString() : "0";
    }

    private string? SendChat(string capabilityKey, IReadOnlyList<string> args, CancellationToken ct)
    {
        if (args.Count == 0 || string.IsNullOrWhiteSpace(args[0]))
            return null;

        // The guest holds only the Guid; the Helix provider resolves the Twitch channel id + bot token host-side.
        chatProvider.SendMessageAsync(broadcasterId, args[0], ct).GetAwaiter().GetResult();
        return null;
    }
}
