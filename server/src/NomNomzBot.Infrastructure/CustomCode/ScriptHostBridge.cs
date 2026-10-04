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
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Application.Commands.Builtin;
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
using NomNomzBot.Domain.Chat.ValueObjects;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Infrastructure.Sandbox;

namespace NomNomzBot.Infrastructure.CustomCode;

/// <summary>
/// The per-execution host-dispatch bridge (custom-code.md §3.1/§6.2) — the only path from a granted <c>bot.*</c>
/// import to host code. Bound to exactly one <c>BroadcasterId</c> (host-side; never readable by the guest); each
/// resolved delegate is primitive-in / primitive-out and tenant-scoped to that channel. <c>chat.send</c>/
/// <c>chat.reply</c> dispatch to the channel's Helix chat provider (bot token host-side, never in the guest; the
/// provider resolves the tenant Guid → Twitch id internally); <c>chat.reply</c> threads under the chat message
/// that started the run, falls back to an @mention when the platform refuses the thread, and is a plain line
/// when no chat message started the run; <c>economy.read</c> reads this channel's ledger;
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
    ScriptReplyTarget? replyTo,
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
    IApplicationDbContext db,
    ISevenTvUserPaintResolver paintResolver,
    IOwnerActionService ownerActions,
    long? maxEgressBytes = null
) : IScriptHostBridge, IScriptWriteValidator
{
    private const int MaxResponseBytes = 256 * 1024;

    // Request plus response bytes of every http.fetch in this run, against the budget's MaxEgressBytes.
    private readonly long _egressCap =
        maxEgressBytes ?? ScriptResourceBudget.Baseline.MaxEgressBytes;
    private long _egressUsed;

    // user.get's paint field is OMITTED (not null, not {}) for a viewer wearing no cosmetic — a script must be
    // able to key off "is this field present" without inspecting every sub-field. The default JsonConvert
    // settings elsewhere in this file serialize a null property AS null, so this one response needs its own
    // NullValueHandling.Ignore settings rather than the ambient default.
    private static readonly JsonSerializerSettings OmitNullSettings = new()
    {
        NullValueHandling = NullValueHandling.Ignore,
    };

    // How many 100-row pages a name/title lookup will walk before giving up (bounded-and-allow).
    private const int MaxLookupPages = 10;

    // The last failed host call's reason (null after a success). Every call clears it first, so it only ever
    // describes the LAST call; reading it through "last.error" is itself not a call and leaves it untouched.
    private ScriptHostError? _lastError;

    public HostImportDelegate Resolve(string capabilityKey)
    {
        if (capabilityKey == ScriptHostErrorCodes.LastErrorKey)
            return (_, _, _) => _lastError?.ToJson();

        HostImportDelegate handler = Dispatch(capabilityKey);
        return (key, args, ct) =>
        {
            _lastError = null;
            return handler(key, args, ct);
        };
    }

    // What the failed call handed back to the guest; read by the live methods and by ValidateWrite.
    private string? _failureReturn;

    private sealed record WidgetEmitPlan(WidgetDetail Widget, object? Data);

    private sealed record RewardUpdatePlan(RewardDetail Reward, UpdateRewardRequest Patch);

    private sealed record SchedulePlan(int DelaySeconds, Dictionary<string, string> Variables);

    public bool ValidateWrite(
        string capabilityKey,
        IReadOnlyList<string> args,
        CancellationToken ct,
        out string? failureReturn
    )
    {
        _lastError = null;
        _failureReturn = null;
        bool valid = capabilityKey switch
        {
            "chat.send" or "chat.reply" => ValidateMessage(capabilityKey, args),
            "music.queue" => ValidateMusicQuery(args),
            "storage.set" => ValidateStorageSet(args),
            "storage.delete" => ValidateStorageDelete(args),
            "tts.speak" => ValidateSpeak(args),
            "tts.voice.set" => PlanVoiceSet(args, ct) is not null,
            "widget.emit" => PlanWidgetEmit(args, ct) is not null,
            "reward.update" => PlanRewardUpdate(args, ct) is not null,
            "schedule.pipeline" => PlanSchedule(args) is not null,
            _ => true,
        };
        failureReturn = _failureReturn;
        return valid;
    }

    private string? Fail(string code, string message, string? returned = null)
    {
        _lastError = new(code, message);
        _failureReturn = returned;
        return returned;
    }

    private string? Fail(Result failure, string? returned = null)
    {
        _lastError = ScriptHostError.FromResult(failure);
        _failureReturn = returned;
        return returned;
    }

    private HostImportDelegate Dispatch(string capabilityKey)
    {
        // One gate key per action type ("actions.invoke:<type>"): the type is part of the key, not an argument.
        if (ScriptActionInvoker.ActionTypeOf(capabilityKey) is { } actionType)
            return (_, args, ct) => InvokeAction(actionType, args, ct);
        return DispatchFixed(capabilityKey);
    }

    private string? InvokeAction(
        string actionType,
        IReadOnlyList<string> args,
        CancellationToken ct
    )
    {
        (string json, ScriptHostError? error) = new ScriptActionInvoker(
            broadcasterId,
            ownerActions
        ).Invoke(actionType, args, ct);
        _lastError = error;
        return json;
    }

    private HostImportDelegate DispatchFixed(string capabilityKey) =>
        capabilityKey switch
        {
            "chat.send" => SendChat,
            "chat.reply" => ReplyChat,
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
        // Public profile only — id/username/displayName/avatar(/paint). Email and other PII are deliberately
        // withheld.
        string subject =
            args.Count > 0 && !string.IsNullOrWhiteSpace(args[0]) ? args[0] : triggeringUserId;
        if (string.IsNullOrWhiteSpace(subject))
            return Fail(
                ScriptHostErrorCodes.InvalidArgument,
                "user.get needs a user and the run has no triggering user."
            );

        // Resolve by login / Twitch id / internal Guid — the SAME converger stats.viewer and tts.voice.*
        // use. Scripts target @mentions (logins), so a Guid-only lookup made every targeting script
        // (!stats @u, voice swap, ratio) fail to find its target; this makes the whole SDK consistent.
        User? user = ResolveViewerUser(subject, ct);
        if (user is null)
            return Fail(ScriptHostErrorCodes.NotFound, $"No user matches '{subject}'.");

        // The 7TV "paint" this chatter wears, folded onto the SAME profile response rather than a second
        // capability key — a script that wants "who is this and how do they render" makes one round trip, not
        // two. Mirrors DashboardBroadcastHandler.MapPaint's null-in/null-out shape exactly: a viewer wearing no
        // paint (or on a platform 7TV does not cover) produces no `paint` field at all, never an empty object.
        ChatPaint? paint = string.IsNullOrEmpty(user.TwitchUserId)
            ? null
            : paintResolver.ResolveAsync(user.TwitchUserId, ct).GetAwaiter().GetResult();

        return JsonConvert.SerializeObject(
            new
            {
                id = user.Id,
                username = user.Username,
                displayName = user.DisplayName,
                avatarUrl = user.ProfileImageUrl,
                paint = paint is null
                    ? null
                    : new
                    {
                        backgroundImage = paint.BackgroundImage,
                        color = paint.Color,
                        textShadow = paint.TextShadow,
                        isImageOnly = paint.IsImageOnly,
                    },
            },
            OmitNullSettings
        );
    }

    private string? Fetch(string capabilityKey, IReadOnlyList<string> args, CancellationToken ct)
    {
        if (
            args.Count == 0
            || !Uri.TryCreate(args[0], UriKind.Absolute, out Uri? uri)
            || uri.Scheme != Uri.UriSchemeHttps
        )
            return Fail(
                ScriptHostErrorCodes.InvalidArgument,
                "http.fetch needs an absolute https URL."
            );

        long requestBytes = Encoding.UTF8.GetByteCount(uri.AbsoluteUri);
        if (_egressUsed + requestBytes > _egressCap)
            return EgressCapReached();
        _egressUsed += requestBytes;
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
                return Fail(
                    ScriptHostErrorCodes.UpstreamFailed,
                    $"The server answered {(int)response.StatusCode}."
                );

            // Read one byte past what is left of the run's cap, so a body that would cross it is seen as crossing.
            int allowed = (int)Math.Min(MaxResponseBytes, _egressCap - _egressUsed);
            using System.IO.Stream body = response.Content.ReadAsStream(ct);
            byte[] buffer = new byte[allowed];
            int total = 0;
            int read;
            while (total < allowed && (read = body.Read(buffer, total, allowed - total)) > 0)
                total += read;
            _egressUsed += total;
            if (total == allowed && allowed < MaxResponseBytes && body.ReadByte() >= 0)
                return EgressCapReached();
            return Encoding.UTF8.GetString(buffer, 0, total);
        }
        catch
        {
            // blocked egress / timeout / transport fault — fail closed
            return Fail(
                ScriptHostErrorCodes.UpstreamFailed,
                "The request was blocked or failed before a response arrived."
            );
        }
    }

    private string? EgressCapReached() =>
        Fail(
            ScriptHostErrorCodes.LimitExceeded,
            $"http.fetch would pass the {_egressCap} byte limit on data sent and received in one run."
        );

    private string? QueueMusic(
        string capabilityKey,
        IReadOnlyList<string> args,
        CancellationToken ct
    )
    {
        if (!ValidateMusicQuery(args))
            return _failureReturn;

        // The music service takes the raw query (title/artist/link) and resolves + enqueues it host-side,
        // attributing the request to the trigger user; the bot's provider token never reaches the guest.
        // A refused admission (no provider, not found, blocked track) surfaces as "false" — the guest sees
        // the boolean contract, never the host's typed error.
        Result<MusicTrack> queued = musicService
            .RequestTrackAsync(
                broadcasterId.ToString(),
                args[0],
                triggeringUserId,
                cancellationToken: ct
            )
            .GetAwaiter()
            .GetResult();
        return queued.IsSuccess ? "true" : Fail(queued, "false");
    }

    private string? ReadBalance(
        string capabilityKey,
        IReadOnlyList<string> args,
        CancellationToken ct
    )
    {
        // The optional arg names a viewer of THIS channel by internal Guid, Twitch id, or login; default to the
        // trigger user (host-validated). A chat trigger carries the Twitch id, so a Guid-only read was always 0.
        string subject =
            args.Count > 0 && !string.IsNullOrWhiteSpace(args[0]) ? args[0] : triggeringUserId;
        Guid? viewerUserId = Guid.TryParse(subject, out Guid userGuid)
            ? userGuid
            : ResolveViewerUser(subject, ct)?.Id;
        if (viewerUserId is not { } viewerId)
            return Fail(ScriptHostErrorCodes.NotFound, $"No viewer matches '{subject}'.", "0");

        Result<long> balance = currencyService
            .GetBalanceAsync(broadcasterId, viewerId, ct)
            .GetAwaiter()
            .GetResult();
        return balance.IsSuccess ? balance.Value.ToString() : Fail(balance, "0");
    }

    private bool ValidateMusicQuery(IReadOnlyList<string> args)
    {
        if (args.Count != 0 && !string.IsNullOrWhiteSpace(args[0]))
            return true;
        Fail(
            ScriptHostErrorCodes.InvalidArgument,
            "music.queue needs a song title, artist or link.",
            "false"
        );
        return false;
    }

    private bool ValidateMessage(string capabilityKey, IReadOnlyList<string> args)
    {
        if (args.Count != 0 && !string.IsNullOrWhiteSpace(args[0]))
            return true;
        Fail(ScriptHostErrorCodes.InvalidArgument, $"{capabilityKey} needs a non-empty message.");
        return false;
    }

    private string? SendChat(string capabilityKey, IReadOnlyList<string> args, CancellationToken ct)
    {
        if (!ValidateMessage(capabilityKey, args))
            return _failureReturn;

        // The guest holds only the Guid; the Helix provider resolves the Twitch channel id + bot token host-side.
        bool sent = chatProvider
            .SendMessageAsync(broadcasterId, args[0], ct)
            .GetAwaiter()
            .GetResult();
        return sent ? null : FailNotSent();
    }

    // chat.send / chat.reply are declared void, so the guest learns of a message that never reached chat
    // only through last.error.
    private string? FailNotSent() =>
        Fail(ScriptHostErrorCodes.UpstreamFailed, "The message was not sent to chat.");

    private string? ReplyChat(
        string capabilityKey,
        IReadOnlyList<string> args,
        CancellationToken ct
    )
    {
        if (replyTo is null)
            return SendChat(capabilityKey, args, ct);
        if (!ValidateMessage(capabilityKey, args))
            return _failureReturn;

        bool threaded = chatProvider
            .SendReplyAsync(broadcasterId, replyTo.MessageId, args[0], ct)
            .GetAwaiter()
            .GetResult();
        if (threaded)
            return null;

        ReplyOrMentionPlan mention = ReplyOrMentionComposer.Compose(
            null,
            replyTo.DisplayName,
            args[0]
        );
        bool mentioned = chatProvider
            .SendMessageAsync(broadcasterId, mention.Message, ct)
            .GetAwaiter()
            .GetResult();
        return mentioned ? null : FailNotSent();
    }

    private string? StorageGet(
        string capabilityKey,
        IReadOnlyList<string> args,
        CancellationToken ct
    )
    {
        if (args.Count == 0 || string.IsNullOrWhiteSpace(args[0]))
            return Fail(ScriptHostErrorCodes.InvalidArgument, "storage.get needs a key.");
        return storageService.GetAsync(broadcasterId, args[0], ct).GetAwaiter().GetResult();
    }

    private string? StorageSet(
        string capabilityKey,
        IReadOnlyList<string> args,
        CancellationToken ct
    )
    {
        if (!ValidateStorageSet(args))
            return _failureReturn;

        // The service enforces the bounds (key length, 64 KB value, 200-keys-per-channel); an over-cap
        // write is a typed failure host-side, surfaced to the guest as null — fail-closed, nothing written.
        Result set = storageService
            .SetAsync(broadcasterId, args[0], args[1], ct)
            .GetAwaiter()
            .GetResult();
        return set.IsSuccess ? "ok" : Fail(set);
    }

    private bool ValidateStorageSet(IReadOnlyList<string> args)
    {
        if (args.Count < 2 || string.IsNullOrWhiteSpace(args[0]))
        {
            Fail(ScriptHostErrorCodes.InvalidArgument, "storage.set needs a key and a value.");
            return false;
        }
        Result valid = ScriptStorageService.ValidateWrite(args[0], args[1]);
        if (valid.IsSuccess)
            return true;
        Fail(valid);
        return false;
    }

    private bool ValidateStorageDelete(IReadOnlyList<string> args)
    {
        if (args.Count == 0 || string.IsNullOrWhiteSpace(args[0]))
        {
            Fail(ScriptHostErrorCodes.InvalidArgument, "storage.delete needs a key.");
            return false;
        }
        Result valid = ScriptStorageService.ValidateKey(args[0]);
        if (valid.IsSuccess)
            return true;
        Fail(valid);
        return false;
    }

    private string? StorageDelete(
        string capabilityKey,
        IReadOnlyList<string> args,
        CancellationToken ct
    )
    {
        if (!ValidateStorageDelete(args))
            return _failureReturn;

        Result deleted = storageService
            .DeleteAsync(broadcasterId, args[0], ct)
            .GetAwaiter()
            .GetResult();
        return deleted.IsSuccess ? "ok" : Fail(deleted);
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

    private bool ValidateSpeak(IReadOnlyList<string> args)
    {
        if (args.Count != 0 && !string.IsNullOrWhiteSpace(args[0]))
            return true;
        Fail(ScriptHostErrorCodes.InvalidArgument, "tts.speak needs the text to speak.");
        return false;
    }

    private string? Speak(string capabilityKey, IReadOnlyList<string> args, CancellationToken ct)
    {
        if (!ValidateSpeak(args))
            return _failureReturn;
        string? voiceOverride =
            args.Count > 1 && !string.IsNullOrWhiteSpace(args[1]) ? args[1] : null;
        // Per-utterance SSML prosody overrides (e.g. a script's "evil wizard" voice) — a one-off flourish for
        // THIS call only, never written to the channel's persisted TTS config. Absent/blank/unparseable is
        // null, i.e. the provider's default rate/pitch.
        double? ratePercent = ParseOptionalDouble(args, 2);
        double? pitchPercent = ParseOptionalDouble(args, 3);

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
            StreamId: null,
            RatePercent: ratePercent,
            PitchPercent: pitchPercent
        );
        Result<TtsDispatchOutcome> outcome = ttsDispatch
            .RequestSpeakAsync(request, ct)
            .GetAwaiter()
            .GetResult();
        if (outcome.IsFailure)
            return Fail(outcome);

        return JsonConvert.SerializeObject(
            new
            {
                voiceId = outcome.Value.VoiceId,
                characterCount = outcome.Value.CharacterCount,
                // How long the utterance actually takes to play — a script pairing tts.speak with chat.send
                // needs this to hold the message back until the line is spoken, not fire both in the same tick
                // (owner report 2026-09-09: "it takes about 1.5 seconds for the tts to be ready and makes the
                // typed action feel disconnected"). See nnz.time.sleep.
                durationMs = outcome.Value.DurationMs,
            }
        );
    }

    private WidgetEmitPlan? PlanWidgetEmit(IReadOnlyList<string> args, CancellationToken ct)
    {
        if (
            args.Count < 2
            || string.IsNullOrWhiteSpace(args[0])
            || string.IsNullOrWhiteSpace(args[1])
        )
        {
            Fail(
                ScriptHostErrorCodes.InvalidArgument,
                "widget.emit needs a widget and an event name."
            );
            return null;
        }

        // Fail-closed, mirroring the widget_event pipeline action: the widget must exist AND be enabled in
        // THIS tenant (the service scopes by broadcaster, so another channel's widget resolves as not-found).
        WidgetDetail? widget = ResolveWidget(args[0], ct);
        if (widget is null)
        {
            Fail(ScriptHostErrorCodes.NotFound, $"No widget matches '{args[0]}'.");
            return null;
        }
        if (!widget.IsEnabled)
        {
            Fail(ScriptHostErrorCodes.Refused, $"The widget '{widget.Name}' is turned off.");
            return null;
        }
        if (!widget.IsAttached)
        {
            Fail(
                ScriptHostErrorCodes.Refused,
                $"The widget '{widget.Name}' is open in no browser source, so nobody would see the event."
            );
            return null;
        }

        object? data = null;
        if (args.Count > 2 && !string.IsNullOrWhiteSpace(args[2]))
        {
            data = ParseDataJson(args[2]);
            if (data is null)
            {
                // malformed payload — refuse rather than push garbage to the overlay
                Fail(ScriptHostErrorCodes.InvalidArgument, "widget.emit data is not valid JSON.");
                return null;
            }
        }
        return new(widget, data);
    }

    private string? EmitWidgetEvent(
        string capabilityKey,
        IReadOnlyList<string> args,
        CancellationToken ct
    )
    {
        WidgetEmitPlan? plan = PlanWidgetEmit(args, ct);
        if (plan is null)
            return _failureReturn;

        try
        {
            widgetNotifier
                .SendWidgetEventAsync(broadcasterId, plan.Widget.Id, args[1], plan.Data, ct)
                .GetAwaiter()
                .GetResult();
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            // The SDK promises false for a failed send; only the run's own cancellation may still stop the script.
            return Fail(
                ScriptHostErrorCodes.UpstreamFailed,
                "The widget event could not be sent to the overlay."
            );
        }
        return "ok";
    }

    private string? GetReward(
        string capabilityKey,
        IReadOnlyList<string> args,
        CancellationToken ct
    )
    {
        if (args.Count == 0 || string.IsNullOrWhiteSpace(args[0]))
            return Fail(
                ScriptHostErrorCodes.InvalidArgument,
                "reward.get needs a reward id or title."
            );

        RewardDetail? reward = ResolveReward(args[0], ct);
        if (reward is null)
            return Fail(ScriptHostErrorCodes.NotFound, $"No reward matches '{args[0]}'.");

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

    private RewardUpdatePlan? PlanRewardUpdate(IReadOnlyList<string> args, CancellationToken ct)
    {
        if (args.Count < 2 || string.IsNullOrWhiteSpace(args[0]))
        {
            Fail(ScriptHostErrorCodes.InvalidArgument, "reward.update needs a reward and a patch.");
            return null;
        }

        RewardDetail? reward = ResolveReward(args[0], ct);
        if (reward is null)
        {
            Fail(ScriptHostErrorCodes.NotFound, $"No reward matches '{args[0]}'.");
            return null;
        }
        // Only bot-manageable rewards may be mutated from a script (Twitch only lets our client_id patch
        // rewards it created; an external reward is read-only) — fail closed before touching the service.
        if (!reward.IsManageable)
        {
            Fail(
                ScriptHostErrorCodes.Refused,
                $"The reward '{reward.Title}' was not created by the bot and is read-only."
            );
            return null;
        }

        UpdateRewardRequest? patch = ReadRewardPatch(args[1]);
        if (patch is null)
        {
            Fail(
                ScriptHostErrorCodes.InvalidArgument,
                "reward.update patch is not a valid JSON object."
            );
            return null;
        }
        return new(reward, patch);
    }

    private string? UpdateReward(
        string capabilityKey,
        IReadOnlyList<string> args,
        CancellationToken ct
    )
    {
        RewardUpdatePlan? plan = PlanRewardUpdate(args, ct);
        if (plan is null)
            return _failureReturn;

        // The rewards service is the ONE update path (same as the dashboard), so the Helix push + local
        // persistence happen exactly as they do there.
        Result<RewardDetail> updated = rewardService
            .UpdateAsync(broadcasterId.ToString(), plan.Reward.Id, plan.Patch, ct)
            .GetAwaiter()
            .GetResult();
        return updated.IsSuccess ? "ok" : Fail(updated);
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
            return Fail(ScriptHostErrorCodes.NotFound, "No viewer matches that user.");

        // NOT_FOUND = the viewer uses the channel default — the guest sees null, the honest "no assignment".
        Result<UserTtsVoiceDto> assigned = ttsConfig
            .GetUserVoiceAsync(broadcasterId, platformUserId, ct)
            .GetAwaiter()
            .GetResult();
        if (assigned.IsFailure)
            return Fail(assigned);

        return JsonConvert.SerializeObject(
            new
            {
                voiceId = assigned.Value.VoiceId,
                displayName = ResolveVoiceDisplayName(assigned.Value.VoiceId, ct),
            }
        );
    }

    private string? PlanVoiceSet(IReadOnlyList<string> args, CancellationToken ct)
    {
        if (args.Count == 0 || string.IsNullOrWhiteSpace(args[0]))
        {
            Fail(ScriptHostErrorCodes.InvalidArgument, "tts.voice.set needs a viewer.");
            return null;
        }
        string? platformUserId = ResolveViewerPlatformId(args[0], ct);
        if (platformUserId is null)
            Fail(ScriptHostErrorCodes.NotFound, $"No viewer matches '{args[0]}'.");
        return platformUserId;
    }

    private string? SetTtsVoice(
        string capabilityKey,
        IReadOnlyList<string> args,
        CancellationToken ct
    )
    {
        string? platformUserId = PlanVoiceSet(args, ct);
        if (platformUserId is null)
            return _failureReturn;

        // An empty voice id clears the assignment back to the channel default (the !voice clear semantics).
        string voiceId = args.Count > 1 ? args[1] : string.Empty;
        if (string.IsNullOrWhiteSpace(voiceId))
        {
            Result cleared = ttsConfig
                .ClearUserVoiceAsync(broadcasterId, platformUserId, ct)
                .GetAwaiter()
                .GetResult();
            return cleared.IsSuccess ? "ok" : Fail(cleared);
        }

        // The service is the ONE assignment path (same as the dashboard/mod override) — it validates the
        // voice against the synthesizable catalogue, so an unknown voice is a typed failure → null.
        Result<UserTtsVoiceDto> set = ttsConfig
            .SetUserVoiceAsync(broadcasterId, platformUserId, new() { VoiceId = voiceId }, ct)
            .GetAwaiter()
            .GetResult();
        return set.IsSuccess ? "ok" : Fail(set);
    }

    private SchedulePlan? PlanSchedule(IReadOnlyList<string> args)
    {
        if (
            args.Count < 2
            || string.IsNullOrWhiteSpace(args[0])
            || !int.TryParse(args[1], out int delaySeconds)
        )
        {
            Fail(
                ScriptHostErrorCodes.InvalidArgument,
                "schedule.pipeline needs a pipeline name and a whole number of seconds."
            );
            return null;
        }

        Dictionary<string, string>? variables = new(StringComparer.OrdinalIgnoreCase);
        if (args.Count > 2 && !string.IsNullOrWhiteSpace(args[2]))
        {
            try
            {
                variables = JsonConvert.DeserializeObject<Dictionary<string, string>>(args[2]);
            }
            catch (JsonException)
            {
                // malformed variables payload — refuse rather than schedule garbage
                variables = null;
            }
            if (variables is null)
            {
                Fail(
                    ScriptHostErrorCodes.InvalidArgument,
                    "schedule.pipeline variables are not a valid JSON object."
                );
                return null;
            }
        }
        return new(delaySeconds, variables);
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
        SchedulePlan? plan = PlanSchedule(args);
        if (plan is null)
            return _failureReturn;

        string? dedupeKey = args.Count > 3 && !string.IsNullOrWhiteSpace(args[3]) ? args[3] : null;

        Result<ScheduledPipelineTaskDto> scheduled = scheduledPipelines
            .ScheduleByNameAsync(
                broadcasterId,
                args[0],
                plan.DelaySeconds,
                plan.Variables,
                triggeringUserId,
                string.Empty,
                dedupeKey,
                ct
            )
            .GetAwaiter()
            .GetResult();
        return scheduled.IsSuccess ? "ok" : Fail(scheduled);
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

        string login = MentionParser.ParseUserMention(subject).ToLowerInvariant();
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
        if (viewer?.TwitchUserId is { } twitchId && !string.IsNullOrEmpty(twitchId))
            return twitchId;
        return !Guid.TryParse(subject, out _) && subject.All(char.IsAsciiDigit) ? subject : null;
    }

    // Best-effort display name off the voice catalogue (exact id match); the id itself when un-catalogued.
    private string ResolveVoiceDisplayName(string voiceId, CancellationToken ct)
    {
        Result<PagedList<TtsVoiceDto>> matches = ttsConfig
            .SearchVoicesAsync(new(Q: voiceId, PageSize: 10), ct)
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
                .ListAsync(broadcasterId.ToString(), new(page, 100), ct)
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
                .ListAsync(broadcasterId.ToString(), new(page, 100), ct)
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

    // An optional trailing numeric arg (e.g. tts.speak's rate/pitch overrides): absent, blank,
    // unparseable or not finite (NaN, Infinity) is null rather than a thrown/guest-visible error — a script
    // that omits or fat-fingers the override just gets the provider's default rate/pitch, never a failed call.
    private static double? ParseOptionalDouble(IReadOnlyList<string> args, int index)
    {
        if (args.Count <= index || string.IsNullOrWhiteSpace(args[index]))
            return null;
        return
            double.TryParse(
                args[index],
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out double parsed
            ) && double.IsFinite(parsed)
            ? parsed
            : null;
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

        return new()
        {
            Title = patch.Value<string?>("title"),
            Cost = patch.Value<int?>("cost"),
            Prompt = patch.Value<string?>("prompt"),
            IsEnabled = patch.Value<bool?>("isEnabled"),
            IsPaused = patch.Value<bool?>("isPaused"),
        };
    }
}
