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
using NomNomzBot.Application.Abstractions.Caching;
using NomNomzBot.Application.Chat.Decoration;
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Platform.Dtos;
using NomNomzBot.Application.Platform.Services;
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Domain.Chat.ValueObjects;
using NomNomzBot.Infrastructure.Chat.Adapters;

namespace NomNomzBot.Infrastructure.Chat;

/// <summary>
/// The thin decoration orchestrator (chat-decoration spec §0/§3.1). It owns no enrichment logic: it seeds a mutable
/// <see cref="ChatDecorationContext"/> from the event — copies of the fragments (so decoration never mutates the event's
/// own fragments that other handlers read) plus the channel's resolved rules — then runs the discovered
/// <see cref="IChatDecorationAdapter"/> chain in <c>Order</c>, each gated by its own <c>AppliesTo</c> and best-effort
/// (a throwing adapter is logged and skipped, the message still emits). No provider HTTP happens here — adapters read
/// only cache (the refresh worker warms it, §3.6).
/// </summary>
public sealed class ChatMessageDecorator : IChatMessageDecorator
{
    // The decoration features and their default state. Third-party emote rendering is ON by default — the near-universal
    // want, matching every emote extension — and a channel opts OUT with an explicit toggle. Link preview is absent here
    // (opt-in, gated on its own toggle + viewer standing in its adapter).
    private static readonly (string Key, bool DefaultOn)[] DecorationFeatures =
    [
        ("use_7tv", true),
        ("use_bttv", true),
        ("use_ffz", true),
        ("use_link_preview", false), // opt-in: link previews make an outbound fetch, so off by default
        ("use_chat_html", false), // opt-in: rendering a viewer's inline HTML is powerful, so off by default
    ];

    // The channel's resolved decoration rules are cached briefly so the chat hot path does not hit the feature store
    // per message; a toggle change takes effect within this window.
    private static readonly TimeSpan RulesCacheTtl = TimeSpan.FromSeconds(60);

    private readonly IReadOnlyList<IChatDecorationAdapter> _adapters;
    private readonly IFeatureService _features;
    private readonly ICacheService _cache;
    private readonly IUserService _users;
    private readonly IRoleResolver _roles;
    private readonly ILogger<ChatMessageDecorator> _logger;

    public ChatMessageDecorator(
        IEnumerable<IChatDecorationAdapter> adapters,
        IFeatureService features,
        ICacheService cache,
        IUserService users,
        IRoleResolver roles,
        ILogger<ChatMessageDecorator> logger
    )
    {
        _adapters = [.. adapters.OrderBy(adapter => adapter.Order)];
        _features = features;
        _cache = cache;
        _users = users;
        _roles = roles;
        _logger = logger;
    }

    public async Task<DecoratedChatMessage> DecorateAsync(
        ChatMessageReceivedEvent message,
        CancellationToken ct = default
    )
    {
        IReadOnlySet<string> enabledFeatures = await ResolveEnabledFeaturesAsync(
            message.BroadcasterId,
            ct
        );
        SenderStanding standing = await ResolveSenderStandingAsync(message, enabledFeatures, ct);

        ChatDecorationContext context = new()
        {
            BroadcasterId = message.BroadcasterId,
            TwitchBroadcasterId = message.TwitchBroadcasterId,
            EnabledFeatures = enabledFeatures,
            Fragments = [.. message.Fragments.Select(Clone)],
            Badges = message.Badges,
            SenderMayRenderHtml = standing.RenderHtml,
            SenderMayPreviewLinks = standing.PreviewLinks,
        };

        foreach (IChatDecorationAdapter adapter in _adapters)
        {
            if (!adapter.AppliesTo(context))
                continue;

            try
            {
                await adapter.DecorateAsync(context, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Decoration adapter {Adapter} threw; skipping it for this message.",
                    adapter.GetType().Name
                );
            }
        }

        return new() { Fragments = context.Fragments, Badges = context.ResolvedBadges };
    }

    private readonly record struct SenderStanding(bool RenderHtml, bool PreviewLinks);

    // The sender's standing for the two viewer-gated steps, the same ladder the command gate walks: a live badge
    // (subscriber and above) wins outright and costs nothing; only when it is missing does the resolver answer
    // per capability (a !permit grant, or a resolved level that meets the seeded default) — and only for a step
    // the channel has switched on AND this message can trigger, so a plain message never reaches the resolver.
    private async Task<SenderStanding> ResolveSenderStandingAsync(
        ChatMessageReceivedEvent message,
        IReadOnlySet<string> enabledFeatures,
        CancellationToken ct
    )
    {
        if (HasBadgeStanding(message))
            return new(true, true);

        bool htmlCandidate =
            enabledFeatures.Contains("use_chat_html")
            && message.Fragments.Any(HtmlFragmentAdapter.LooksLikeHtml);
        bool linkCandidate =
            enabledFeatures.Contains("use_link_preview")
            && message.Fragments.Any(LinkPreviewAdapter.ContainsHttpUrl);
        if (!htmlCandidate && !linkCandidate)
            return new(false, false);

        Guid? viewerUserId = await TryResolveViewerUserIdAsync(message, ct);
        if (viewerUserId is null)
            return new(false, false);

        bool renderHtml =
            htmlCandidate
            && await HoldsCapabilityAsync(
                viewerUserId.Value,
                message,
                ChatDecorationCapabilities.RenderHtml,
                ct
            );
        bool previewLinks =
            linkCandidate
            && await HoldsCapabilityAsync(
                viewerUserId.Value,
                message,
                ChatDecorationCapabilities.PreviewLinks,
                ct
            );
        return new(renderHtml, previewLinks);
    }

    // The live-badge leg of the standing ladder: a subscriber, VIP, moderator or broadcaster badge on the message.
    private static bool HasBadgeStanding(ChatMessageReceivedEvent message) =>
        message.IsSubscriber || message.IsVip || message.IsModerator || message.IsBroadcaster;

    // The event carries the platform user id; the resolver needs the internal User id. A chatter IS a (possibly
    // not-set-up) User row — the same get-or-create seam every chat-ingest handler uses. Null when it cannot
    // resolve, so the caller fails CLOSED to the badge leg (a lookup error must never elevate).
    private async Task<Guid?> TryResolveViewerUserIdAsync(
        ChatMessageReceivedEvent message,
        CancellationToken ct
    )
    {
        try
        {
            Result<UserDto> user = await _users.GetOrCreateAsync(
                message.UserId,
                message.UserLogin,
                message.UserDisplayName,
                message.Provider,
                ct
            );
            if (user.IsFailure || !Guid.TryParse(user.Value.Id, out Guid viewerUserId))
                return null;
            return viewerUserId;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Viewer lookup failed for {User} in {Channel}; decoration falls back to badge standing",
                message.UserLogin,
                message.BroadcasterId
            );
            return null;
        }
    }

    private async Task<bool> HoldsCapabilityAsync(
        Guid viewerUserId,
        ChatMessageReceivedEvent message,
        string actionKey,
        CancellationToken ct
    )
    {
        try
        {
            Result<bool> held = await _roles.HasCapabilityAsync(
                viewerUserId,
                message.BroadcasterId,
                actionKey,
                ct
            );
            return held is { IsSuccess: true, Value: true };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Capability {Action} resolution failed for {User} in {Channel}; decoration falls back to badge standing",
                actionKey,
                message.UserLogin,
                message.BroadcasterId
            );
            return false;
        }
    }

    // The set of enabled decoration feature keys for the channel: each feature ON unless an explicit toggle disables it
    // (emote features default ON). Cached per channel for a short window so the hot path does not query the feature store
    // per message.
    private async Task<IReadOnlySet<string>> ResolveEnabledFeaturesAsync(
        Guid broadcasterId,
        CancellationToken ct
    )
    {
        string cacheKey = ChatDecorationRulesCacheKeys.Channel(broadcasterId);
        HashSet<string>? cached = await _cache.GetAsync<HashSet<string>>(cacheKey, ct);
        if (cached is not null)
            return cached;

        Result<List<FeatureStatusDto>> features = await _features.GetFeaturesAsync(
            broadcasterId.ToString(),
            ct
        );
        Dictionary<string, bool> toggles = features.IsSuccess
            ? features
                .Value.GroupBy(feature => feature.FeatureKey)
                .ToDictionary(group => group.Key, group => group.Last().IsEnabled)
            : [];

        HashSet<string> enabled = new(StringComparer.Ordinal);
        foreach ((string key, bool defaultOn) in DecorationFeatures)
            if (toggles.TryGetValue(key, out bool on) ? on : defaultOn)
                enabled.Add(key);

        await _cache.SetAsync(cacheKey, enabled, RulesCacheTtl, ct);
        return enabled;
    }

    // A copy so in-place enrichment (e.g. setting Emote) never touches the event's own fragments, which sibling
    // handlers still read. `with { }` copies every member the record declares, including any added later — the
    // field-by-field version this replaces went stale the moment GIF fields landed and dropped them on every
    // message, so the id and url never reached a renderer.
    private static ChatMessageFragment Clone(ChatMessageFragment fragment) => fragment with { };
}
