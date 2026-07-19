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
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Analytics;
using NomNomzBot.Application.Contracts.CustomCode;
using NomNomzBot.Application.Contracts.Tts;
using NomNomzBot.Application.Economy.Services;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Application.Rewards.Dtos;
using NomNomzBot.Application.Rewards.Services;
using NomNomzBot.Application.Tts.Dtos;
using NomNomzBot.Application.Tts.Services;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Identity.Entities;
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
/// SSRF-hardened egress client; <c>storage.*</c> is the channel's bounded script KV store; <c>tts.speak</c> routes
/// through the gated TTS dispatcher; <c>widget.emit</c> pushes an event to one of THIS channel's enabled widgets;
/// <c>reward.get</c>/<c>reward.update</c> read and patch a channel-point reward through the rewards service
/// (Helix-synced; only bot-manageable rewards may be updated); <c>stats.viewer</c> reads a viewer's M.1 analytics
/// profile (the SAME source the <c>{viewer.*}</c> template stats read — a never-seen viewer reads as honest zeros);
/// <c>tts.voice.get</c>/<c>tts.voice.set</c> read and assign a viewer's per-channel TTS voice (set validates
/// against the voice catalogue; an empty voice id clears back to the channel default). Every dispatch fails
/// closed (returns a safe primitive).
/// </summary>
public sealed class ScriptHostBridge(
    Guid broadcasterId,
    string triggeringUserId,
    IChatProvider chatProvider,
    ICurrencyAccountService currencyService,
    IMusicService musicService,
    IHttpClientFactory httpClientFactory,
    IScriptStorageService storageService,
    ITtsDispatchService ttsDispatch,
    IWidgetService widgetService,
    IWidgetEventNotifier widgetNotifier,
    IRewardService rewardService,
    IViewerAnalyticsService viewerAnalytics,
    ITtsConfigService ttsConfig,
    IScheduledPipelineService scheduledPipelines,
    IApplicationDbContext db
) : IScriptHostBridge
{
    private const int MaxResponseBytes = 256 * 1024;

    // How many 100-row pages a name/title lookup will walk before giving up (bounded-and-allow).
    private const int MaxLookupPages = 10;

    public HostImportDelegate Resolve(string capabilityKey) =>
        capabilityKey switch
        {
            "chat.send" or "chat.reply" => SendChat,
            "economy.read" => ReadBalance,
            "music.queue" => QueueMusic,
            "music.nowPlaying" => ReadNowPlaying,
            "user.get" => GetUser,
            "http.fetch" => Fetch,
            "storage.get" => StorageGet,
            "storage.set" => StorageSet,
            "storage.delete" => StorageDelete,
            "storage.list" => StorageList,
            "tts.speak" => Speak,
            "widget.emit" => EmitWidgetEvent,
            "reward.get" => GetReward,
            "reward.update" => UpdateReward,
            "stats.viewer" => GetViewerStats,
            "tts.voice.get" => GetTtsVoice,
            "tts.voice.set" => SetTtsVoice,
            "schedule.pipeline" => SchedulePipeline,
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

        // Resolve by login / Twitch id / internal Guid — the SAME converger stats.viewer and tts.voice.*
        // use. Scripts target @mentions (logins), so a Guid-only lookup made every targeting script
        // (!stats @u, voice swap, ratio) fail to find its target; this makes the whole SDK consistent.
        User? user = ResolveViewerUser(subject, ct);
        if (user is null)
            return null;

        return JsonConvert.SerializeObject(
            new
            {
                id = user.Id,
                username = user.Username,
                displayName = user.DisplayName,
                avatarUrl = user.ProfileImageUrl,
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
        // A refused admission (no provider, blocked track) surfaces as "false" — the guest sees the
        // boolean contract, never the host's typed error.
        Result queued = musicService
            .AddToQueueAsync(broadcasterId.ToString(), args[0], triggeringUserId, ct)
            .GetAwaiter()
            .GetResult();
        return queued.IsSuccess ? "true" : "false";
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

    private string? StorageGet(
        string capabilityKey,
        IReadOnlyList<string> args,
        CancellationToken ct
    )
    {
        if (args.Count == 0 || string.IsNullOrWhiteSpace(args[0]))
            return null;
        return storageService.GetAsync(broadcasterId, args[0], ct).GetAwaiter().GetResult();
    }

    private string? StorageSet(
        string capabilityKey,
        IReadOnlyList<string> args,
        CancellationToken ct
    )
    {
        if (args.Count < 2 || string.IsNullOrWhiteSpace(args[0]))
            return null;

        // The service enforces the bounds (key length, 64 KB value, 200-keys-per-channel); an over-cap
        // write is a typed failure host-side, surfaced to the guest as null — fail-closed, nothing written.
        Result set = storageService
            .SetAsync(broadcasterId, args[0], args[1], ct)
            .GetAwaiter()
            .GetResult();
        return set.IsSuccess ? "ok" : null;
    }

    private string? StorageDelete(
        string capabilityKey,
        IReadOnlyList<string> args,
        CancellationToken ct
    )
    {
        if (args.Count == 0 || string.IsNullOrWhiteSpace(args[0]))
            return null;

        Result deleted = storageService
            .DeleteAsync(broadcasterId, args[0], ct)
            .GetAwaiter()
            .GetResult();
        return deleted.IsSuccess ? "ok" : null;
    }

    private string? StorageList(
        string capabilityKey,
        IReadOnlyList<string> args,
        CancellationToken ct
    )
    {
        string? prefix = args.Count > 0 && !string.IsNullOrEmpty(args[0]) ? args[0] : null;
        IReadOnlyList<string> keys = storageService
            .ListAsync(broadcasterId, prefix, ct)
            .GetAwaiter()
            .GetResult();
        return JsonConvert.SerializeObject(keys);
    }

    private string? Speak(string capabilityKey, IReadOnlyList<string> args, CancellationToken ct)
    {
        if (args.Count == 0 || string.IsNullOrWhiteSpace(args[0]))
            return null;
        string? voiceOverride =
            args.Count > 1 && !string.IsNullOrWhiteSpace(args[1]) ? args[1] : null;

        // The same shape PlayTtsAction hands the dispatcher: the gate (enabled + caps + censor + voice
        // resolution) runs host-side; a refusal is a typed failure the guest only ever sees as null.
        TtsSpeakRequest request = new(
            BroadcasterId: broadcasterId,
            RequestedByUserId: Guid.Empty,
            RequestedByTwitchUserId: triggeringUserId,
            RequestedByDisplayName: string.Empty,
            Text: args[0],
            VoiceIdOverride: voiceOverride,
            BitsAmount: 0,
            CommunityStanding: "everyone",
            SourceMessageId: null,
            StreamId: null
        );
        Result<TtsDispatchOutcome> outcome = ttsDispatch
            .RequestSpeakAsync(request, ct)
            .GetAwaiter()
            .GetResult();
        if (outcome.IsFailure)
            return null;

        return JsonConvert.SerializeObject(
            new { voiceId = outcome.Value.VoiceId, characterCount = outcome.Value.CharacterCount }
        );
    }

    private string? EmitWidgetEvent(
        string capabilityKey,
        IReadOnlyList<string> args,
        CancellationToken ct
    )
    {
        if (
            args.Count < 2
            || string.IsNullOrWhiteSpace(args[0])
            || string.IsNullOrWhiteSpace(args[1])
        )
            return null;

        // Fail-closed, mirroring the widget_event pipeline action: the widget must exist AND be enabled in
        // THIS tenant (the service scopes by broadcaster, so another channel's widget resolves as not-found).
        WidgetDetail? widget = ResolveWidget(args[0], ct);
        if (widget is null || !widget.IsEnabled)
            return null;

        object? data = null;
        if (args.Count > 2 && !string.IsNullOrWhiteSpace(args[2]))
        {
            data = ParseDataJson(args[2]);
            if (data is null)
                return null; // malformed payload — refuse rather than push garbage to the overlay
        }

        widgetNotifier
            .SendWidgetEventAsync(broadcasterId, widget.Id, args[1], data, ct)
            .GetAwaiter()
            .GetResult();
        return "ok";
    }

    private string? GetReward(
        string capabilityKey,
        IReadOnlyList<string> args,
        CancellationToken ct
    )
    {
        if (args.Count == 0 || string.IsNullOrWhiteSpace(args[0]))
            return null;

        RewardDetail? reward = ResolveReward(args[0], ct);
        if (reward is null)
            return null;

        return JsonConvert.SerializeObject(
            new
            {
                id = reward.Id,
                title = reward.Title,
                cost = reward.Cost,
                prompt = reward.Prompt,
                isEnabled = reward.IsEnabled,
                isPaused = reward.IsPaused,
            }
        );
    }

    private string? UpdateReward(
        string capabilityKey,
        IReadOnlyList<string> args,
        CancellationToken ct
    )
    {
        if (args.Count < 2 || string.IsNullOrWhiteSpace(args[0]))
            return null;

        RewardDetail? reward = ResolveReward(args[0], ct);
        // Only bot-manageable rewards may be mutated from a script (Twitch only lets our client_id patch
        // rewards it created; an external reward is read-only) — fail closed before touching the service.
        if (reward is null || !reward.IsManageable)
            return null;

        UpdateRewardRequest? patch = ReadRewardPatch(args[1]);
        if (patch is null)
            return null;

        // The rewards service is the ONE update path (same as the dashboard), so the Helix push + local
        // persistence happen exactly as they do there.
        Result<RewardDetail> updated = rewardService
            .UpdateAsync(broadcasterId.ToString(), reward.Id, patch, ct)
            .GetAwaiter()
            .GetResult();
        return updated.IsSuccess ? "ok" : null;
    }

    private string? GetViewerStats(
        string capabilityKey,
        IReadOnlyList<string> args,
        CancellationToken ct
    )
    {
        // The optional arg names a viewer by internal Guid, Twitch id, or login; default to the trigger user.
        string subject =
            args.Count > 0 && !string.IsNullOrWhiteSpace(args[0]) ? args[0] : triggeringUserId;

        // Unknown viewer → honest zeros (the shape a !stats script can always render), never null.
        ViewerProfileDto? profile = null;
        User? viewer = ResolveViewerUser(subject, ct);
        if (viewer is not null)
        {
            Result<ViewerProfileDto> loaded = viewerAnalytics
                .GetProfileAsync(broadcasterId, viewer.Id, ct)
                .GetAwaiter()
                .GetResult();
            if (loaded.IsSuccess)
                profile = loaded.Value;
        }

        return JsonConvert.SerializeObject(
            new
            {
                messages = profile?.TotalMessages ?? 0,
                watchtimeSeconds = profile?.TotalWatchSeconds ?? 0,
                firstSeen = profile?.FirstSeenAt?.ToString("yyyy-MM-dd"),
                redemptions = profile?.TotalRedemptions ?? 0,
                songRequests = profile?.TotalSongRequests ?? 0,
            }
        );
    }

    private string? GetTtsVoice(
        string capabilityKey,
        IReadOnlyList<string> args,
        CancellationToken ct
    )
    {
        string? platformUserId = ResolveViewerPlatformId(
            args.Count > 0 && !string.IsNullOrWhiteSpace(args[0]) ? args[0] : triggeringUserId,
            ct
        );
        if (platformUserId is null)
            return null;

        // NOT_FOUND = the viewer uses the channel default — the guest sees null, the honest "no assignment".
        Result<UserTtsVoiceDto> assigned = ttsConfig
            .GetUserVoiceAsync(broadcasterId, platformUserId, ct)
            .GetAwaiter()
            .GetResult();
        if (assigned.IsFailure)
            return null;

        return JsonConvert.SerializeObject(
            new
            {
                voiceId = assigned.Value.VoiceId,
                displayName = ResolveVoiceDisplayName(assigned.Value.VoiceId, ct),
            }
        );
    }

    private string? SetTtsVoice(
        string capabilityKey,
        IReadOnlyList<string> args,
        CancellationToken ct
    )
    {
        if (args.Count == 0 || string.IsNullOrWhiteSpace(args[0]))
            return null;
        string? platformUserId = ResolveViewerPlatformId(args[0], ct);
        if (platformUserId is null)
            return null;

        // An empty voice id clears the assignment back to the channel default (the !voice clear semantics).
        string voiceId = args.Count > 1 ? args[1] : string.Empty;
        if (string.IsNullOrWhiteSpace(voiceId))
        {
            Result cleared = ttsConfig
                .ClearUserVoiceAsync(broadcasterId, platformUserId, ct)
                .GetAwaiter()
                .GetResult();
            return cleared.IsSuccess ? "ok" : null;
        }

        // The service is the ONE assignment path (same as the dashboard/mod override) — it validates the
        // voice against the synthesizable catalogue, so an unknown voice is a typed failure → null.
        Result<UserTtsVoiceDto> set = ttsConfig
            .SetUserVoiceAsync(
                broadcasterId,
                platformUserId,
                new SetUserVoiceDto { VoiceId = voiceId },
                ct
            )
            .GetAwaiter()
            .GetResult();
        return set.IsSuccess ? "ok" : null;
    }

    private string? SchedulePipeline(
        string capabilityKey,
        IReadOnlyList<string> args,
        CancellationToken ct
    )
    {
        // args: [pipelineName, delaySeconds, variablesJson?, dedupeKey?]. The name resolves to a pipeline of THIS
        // channel host-side (unknown → typed NOT_FOUND → the guest sees the boolean false); the delay is clamped
        // by the service. This is how a Voice-Swap script schedules its own revert.
        if (
            args.Count < 2
            || string.IsNullOrWhiteSpace(args[0])
            || !int.TryParse(args[1], out int delaySeconds)
        )
            return null;

        Dictionary<string, string>? variables = new(StringComparer.OrdinalIgnoreCase);
        if (args.Count > 2 && !string.IsNullOrWhiteSpace(args[2]))
        {
            try
            {
                variables = JsonConvert.DeserializeObject<Dictionary<string, string>>(args[2]);
            }
            catch (JsonException)
            {
                return null; // malformed variables payload — refuse rather than schedule garbage
            }
            if (variables is null)
                return null;
        }

        string? dedupeKey = args.Count > 3 && !string.IsNullOrWhiteSpace(args[3]) ? args[3] : null;

        Result<ScheduledPipelineTaskDto> scheduled = scheduledPipelines
            .ScheduleByNameAsync(
                broadcasterId,
                args[0],
                delaySeconds,
                variables,
                triggeringUserId,
                string.Empty,
                dedupeKey,
                ct
            )
            .GetAwaiter()
            .GetResult();
        return scheduled.IsSuccess ? "ok" : null;
    }

    // Resolves a viewer reference (internal Guid, Twitch id, or login) to the User row — the same
    // identity-first convergence the template resolver's {viewer.*} stats use. Null when never seen.
    private User? ResolveViewerUser(string subject, CancellationToken ct)
    {
        if (Guid.TryParse(subject, out Guid userGuid))
            return db
                .Users.AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == userGuid, ct)
                .GetAwaiter()
                .GetResult();

        string login = subject.Trim().TrimStart('@').ToLowerInvariant();
        return db.Users.AsNoTracking()
                .FirstOrDefaultAsync(u => u.TwitchUserId == subject, ct)
                .GetAwaiter()
                .GetResult()
            ?? db.Users.AsNoTracking()
                .FirstOrDefaultAsync(u => u.Username == login, ct)
                .GetAwaiter()
                .GetResult();
    }

    // The platform (Twitch) user id the TTS voice table keys on. A numeric id the bot has not persisted yet
    // still resolves to itself — matching how the dispatch voice-resolver reads assignments by platform id.
    private string? ResolveViewerPlatformId(string subject, CancellationToken ct)
    {
        User? viewer = ResolveViewerUser(subject, ct);
        if (viewer?.TwitchUserId is string twitchId && !string.IsNullOrEmpty(twitchId))
            return twitchId;
        return !Guid.TryParse(subject, out _) && subject.All(char.IsAsciiDigit) ? subject : null;
    }

    // Best-effort display name off the voice catalogue (exact id match); the id itself when un-catalogued.
    private string ResolveVoiceDisplayName(string voiceId, CancellationToken ct)
    {
        Result<PagedList<TtsVoiceDto>> matches = ttsConfig
            .SearchVoicesAsync(new TtsVoiceQuery(Q: voiceId, PageSize: 10), ct)
            .GetAwaiter()
            .GetResult();
        if (matches.IsFailure)
            return voiceId;
        TtsVoiceDto? exact = matches.Value.Items.FirstOrDefault(v =>
            string.Equals(v.Id, voiceId, StringComparison.OrdinalIgnoreCase)
        );
        return exact?.DisplayName ?? voiceId;
    }

    // Resolves a widget of THIS channel by Guid id or (case-insensitive) name; null when absent.
    private WidgetDetail? ResolveWidget(string idOrName, CancellationToken ct)
    {
        if (Guid.TryParse(idOrName, out Guid widgetId))
        {
            Result<WidgetDetail> byId = widgetService
                .GetAsync(broadcasterId.ToString(), widgetId.ToString(), ct)
                .GetAwaiter()
                .GetResult();
            return byId.IsSuccess ? byId.Value : null;
        }

        for (int page = 1; page <= MaxLookupPages; page++)
        {
            Result<PagedList<WidgetDetail>> listed = widgetService
                .ListAsync(broadcasterId.ToString(), new PaginationParams(page, 100), ct)
                .GetAwaiter()
                .GetResult();
            if (listed.IsFailure)
                return null;
            WidgetDetail? match = listed.Value.Items.FirstOrDefault(w =>
                string.Equals(w.Name, idOrName, StringComparison.OrdinalIgnoreCase)
            );
            if (match is not null)
                return match;
            if (!listed.Value.HasNextPage)
                return null;
        }
        return null;
    }

    // Resolves a reward of THIS channel by Guid id or (case-insensitive) title; null when absent.
    private RewardDetail? ResolveReward(string idOrTitle, CancellationToken ct)
    {
        if (Guid.TryParse(idOrTitle, out Guid rewardId))
        {
            Result<RewardDetail> byId = rewardService
                .GetAsync(broadcasterId.ToString(), rewardId.ToString(), ct)
                .GetAwaiter()
                .GetResult();
            return byId.IsSuccess ? byId.Value : null;
        }

        for (int page = 1; page <= MaxLookupPages; page++)
        {
            Result<PagedList<RewardDetail>> listed = rewardService
                .ListAsync(broadcasterId.ToString(), new PaginationParams(page, 100), ct)
                .GetAwaiter()
                .GetResult();
            if (listed.IsFailure)
                return null;
            RewardDetail? match = listed.Value.Items.FirstOrDefault(r =>
                string.Equals(r.Title, idOrTitle, StringComparison.OrdinalIgnoreCase)
            );
            if (match is not null)
                return match;
            if (!listed.Value.HasNextPage)
                return null;
        }
        return null;
    }

    // The guest's optional data payload, materialized to a plain CLR graph (dictionaries / lists /
    // primitives) exactly like WidgetEventAction.ReadData — a raw JsonElement does not serialize cleanly
    // over the hub's MessagePack transport. Malformed JSON → null (the caller refuses the push).
    private static object? ParseDataJson(string dataJson)
    {
        try
        {
            using System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(dataJson);
            return ToClr(doc.RootElement);
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private static object? ToClr(System.Text.Json.JsonElement e) =>
        e.ValueKind switch
        {
            System.Text.Json.JsonValueKind.Object => e.EnumerateObject()
                .ToDictionary(p => p.Name, p => ToClr(p.Value)),
            System.Text.Json.JsonValueKind.Array => e.EnumerateArray().Select(ToClr).ToList(),
            System.Text.Json.JsonValueKind.String => e.GetString(),
            System.Text.Json.JsonValueKind.Number => e.TryGetInt64(out long l) ? l : e.GetDouble(),
            System.Text.Json.JsonValueKind.True => true,
            System.Text.Json.JsonValueKind.False => false,
            _ => null,
        };

    // The guest's patch JSON → the service's UpdateRewardRequest (only the recognised keys; anything else
    // is ignored). Malformed JSON or a non-object → null (the caller refuses the update).
    private static UpdateRewardRequest? ReadRewardPatch(string patchJson)
    {
        Newtonsoft.Json.Linq.JObject patch;
        try
        {
            patch = Newtonsoft.Json.Linq.JObject.Parse(patchJson);
        }
        catch (JsonException)
        {
            return null;
        }

        return new UpdateRewardRequest
        {
            Title = patch.Value<string?>("title"),
            Cost = patch.Value<int?>("cost"),
            Prompt = patch.Value<string?>("prompt"),
            IsEnabled = patch.Value<bool?>("isEnabled"),
            IsPaused = patch.Value<bool?>("isPaused"),
        };
    }
}
