// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Reflection;
using NomNomzBot.Application.Contracts.Twitch;

namespace NomNomzBot.Infrastructure.Identity;

/// <summary>
/// Reflects, once, over every Helix sub-client implementation in
/// <c>NomNomzBot.Infrastructure.Platform.Transport.Helix.SubClients</c> and collects every
/// <see cref="RequiresTwitchScopeAttribute"/> it finds into <see cref="AllDeclaredScopes"/> — the
/// single authoritative set of Twitch scopes the Helix layer actually enforces at runtime.
/// <see cref="AuthService"/> unions this with a small residual set of non-Helix-gated scopes
/// (EventSub-only topics, <c>user:read:email</c>, bot-identity IRC-legacy scopes, …) to build the
/// login scope request, so a new <c>[RequiresTwitchScope]</c> method can never silently go
/// unrequested — the drift that let <c>moderator:manage:shoutouts</c> go missing for weeks.
/// Scanning the whole assembly (rather than a hand-maintained list of sub-client class names) means
/// a 27th sub-client added later is picked up automatically, with no second place to remember.
/// </summary>
public sealed class TwitchScopeRegistry
{
    private const string SubClientsNamespace =
        "NomNomzBot.Infrastructure.Platform.Transport.Helix.SubClients";

    /// <summary>
    /// Every scope string declared via <see cref="RequiresTwitchScopeAttribute"/> across the Helix
    /// sub-client assembly. Computed once at construction — this type is registered as a singleton.
    /// </summary>
    public IReadOnlySet<string> AllDeclaredScopes { get; }

    /// <summary>
    /// Scopes that gate something other than a <see cref="RequiresTwitchScopeAttribute"/>-decorated Helix
    /// sub-client method — an EventSub subscription topic condition with no corresponding Helix call, or a
    /// non-Helix identity claim — so they can never be discovered by the reflection sweep above. Hand-
    /// maintained: a topic gated ONLY by an EventSub condition (not a Helix call) belongs here.
    /// </summary>
    public static readonly IReadOnlySet<string> ResidualEventSubScopes = new HashSet<string>(
        StringComparer.Ordinal
    )
    {
        "user:read:email", // account identity claim on login, not a Helix call
        "user:read:chat", // EventSub channel.chat.message (bot's own read topic; also rides here for the
        // single-account self-host fallback where the streamer's account IS the bot)
        "user:write:chat", // HelixChatProvider send (Helix, but no [RequiresTwitchScope] sub-client method)
        // The chatbot badge: the broadcaster grants `channel:bot` to let the bot appear WITH THE BOT BADGE in
        // THEIR channel — the broadcaster-side half of the app-token send (also reachable via the
        // FeatureScopeMap "bot_badge" feature).
        "channel:bot",
        "channel:moderate", // EventSub channel.moderate v2 topic (read-only stream of mod actions)
        "moderator:read:chat_settings", // channel.moderate v2
        "moderator:read:moderators", // channel.moderate v2
        "moderator:read:shoutouts", // channel.shoutout.create / channel.shoutout.receive (EventSub only —
        // the Send Shoutout Helix call itself requires moderator:manage:shoutouts, which IS reflected)
        "moderator:read:suspicious_users", // channel.suspicious_user.message / channel.suspicious_user.update
        "moderator:read:vips", // channel.moderate v2 (distinct from channel:read:vips, which IS reflected)
        "moderator:read:warnings", // channel.warning.acknowledge / channel.warning.send, channel.moderate v2
        // user.whisper.message rides the BOT identity — this streamer-side grant is the single-account
        // self-host leg, where the streamer's own account IS the bot. Distinct from user:manage:whispers
        // (Send Whisper), which IS reflected via TwitchWhispersApi.
        "user:read:whispers",
        // Guest Star ingest: channel:read:guest_star is reflected (gates GetGuestStarSessionAsync);
        // moderator:read:guest_star gates the EventSub condition for channels the bot moderates and is not
        // itself a Helix method pre-check.
        "moderator:read:guest_star",
    };

    /// <summary>
    /// The full scope catalogue — <see cref="AllDeclaredScopes"/> ∪ <see cref="ResidualEventSubScopes"/> —
    /// everything a feature may ever need to request, on demand. This is what the proactive missing-scope
    /// sweep (<c>ScopeNotificationService.GetMissingScopesAsync</c>) and the additive re-grant
    /// (<c>BuildRegrantScopeSetAsync</c>) check against; it is intentionally NOT what a fresh login requests
    /// (that's the small progressive-scope base in <c>AuthService</c>).
    /// </summary>
    public IReadOnlySet<string> FullCatalogue { get; }

    public TwitchScopeRegistry()
        : this(typeof(TwitchScopeRegistry).Assembly) { }

    /// <summary>Internal seam for tests that want to reflect over a different assembly.</summary>
    internal TwitchScopeRegistry(Assembly subClientAssembly)
    {
        AllDeclaredScopes = CollectDeclaredScopes(subClientAssembly);
        HashSet<string> fullCatalogue = new(AllDeclaredScopes, StringComparer.Ordinal);
        fullCatalogue.UnionWith(ResidualEventSubScopes);
        FullCatalogue = fullCatalogue;
    }

    private static IReadOnlySet<string> CollectDeclaredScopes(Assembly subClientAssembly)
    {
        HashSet<string> scopes = new(StringComparer.Ordinal);

        IEnumerable<Type> subClientTypes = subClientAssembly
            .GetTypes()
            .Where(type =>
                type.IsClass
                && string.Equals(type.Namespace, SubClientsNamespace, StringComparison.Ordinal)
            );

        foreach (Type subClientType in subClientTypes)
        {
            const BindingFlags memberFlags =
                BindingFlags.Public
                | BindingFlags.NonPublic
                | BindingFlags.Instance
                | BindingFlags.Static
                | BindingFlags.DeclaredOnly;

            foreach (MethodInfo method in subClientType.GetMethods(memberFlags))
            foreach (
                RequiresTwitchScopeAttribute attribute in method.GetCustomAttributes<RequiresTwitchScopeAttribute>(
                    inherit: false
                )
            )
                scopes.Add(attribute.Scope);
        }

        return scopes;
    }
}
