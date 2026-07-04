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
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using NomNomzBot.Api.Authorization;
using NomNomzBot.Api.Controllers;

namespace NomNomzBot.Api.Tests.Authorization;

/// <summary>
/// The durable endpoint-authorization invariant: since Gate 1 became pure entry (any authenticated caller
/// resolves any existing channel's tenant scope), an action without an explicit gate is reachable by EVERY
/// authenticated user. So every controller action must carry <c>[RequireAction]</c> (Gate 2), be explicitly
/// <c>[AllowAnonymous]</c>, carry an <c>[Authorize]</c> with Roles or a named Policy (Plane-C IAM), or appear
/// on the allowlist below — one entry per endpoint, each stating WHY it is exempt (self-scoped body check,
/// token-scoped public surface, auth flow, or a spec-cited Everyone floor with no seeded key). Adding a new
/// ungated endpoint fails this test; so does a stale allowlist entry, so the list can never rot.
/// </summary>
public sealed class EndpointAuthorizationInvariantTests
{
    /// <summary>
    /// Endpoints deliberately outside the attribute gates. Key = <c>{Controller}.{ActionMethod}</c>.
    /// The value documents WHY — every entry must hold a real, verified justification.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> Allowlist = new Dictionary<
        string,
        string
    >(StringComparer.Ordinal)
    {
        // ── Self-scoped in the action body: subject bound to the JWT caller (self-or-Gate-2 / self-only) ──
        ["AnalyticsController.GetViewer"] =
            "self-or-Gate-2 in body via CanReadViewerAsync (analytics.md §5 row: analytics:viewer:read, self-or-Gate-2)",
        ["AnalyticsController.GetViewerEngagement"] =
            "self-or-Gate-2 in body via CanReadViewerAsync (analytics.md §5)",
        ["AnalyticsController.GetViewerStreak"] =
            "self-or-Gate-2 in body via CanReadViewerAsync (analytics.md §5)",
        ["AnalyticsController.SetViewerOptOut"] =
            "self-or-Gate-2 in body via CanReadViewerAsync (analytics.md §5; viewer's own opt-out is self-service)",
        ["UsersController.GetUser"] =
            "self-or-Gate-2 in body via CanReadUserAsync (own row always; another user needs community:read)",
        ["UsersController.GetUserProfile"] =
            "self-or-Gate-2 in body via CanReadUserAsync (Me page reads own profile; managers hold community:read)",
        ["UsersController.UpdateUserProfile"] =
            "self-only write enforced in body (caller==userId or admin) — a manager never edits another user's identity",
        ["UsersController.GetUserChannels"] =
            "self-only in body (caller==userId or admin): the caller's own channel list",
        ["UsersController.GetUserStats"] =
            "self-only in body (caller==userId): GDPR data summary of the caller's own data",
        ["UsersController.ExportUserData"] =
            "self-only in body (caller==userId or admin): GDPR right of access on own data (gdpr-crypto.md §5.1 posture)",
        ["UsersController.DeleteUserData"] =
            "self-only in body (caller==userId or admin): GDPR erasure of own data (gdpr-crypto.md §5.1 posture)",
        ["ChannelsController.ListChannels"] =
            "JWT-self-scoped in body: lists channels the CALLER owns/moderates (ids from the caller's claims only)",
        ["ChannelsController.GetModeratedChannels"] =
            "JWT-self-scoped in body: the caller's own moderated-channel list from their broadcaster_id claim",
        ["ChannelsController.OnboardChannel"] =
            "self-scoped in body (caller==request.BroadcasterId or admin): a user onboards only their own channel",
        ["CommunityController.GetMyStanding"] =
            "self-scoped: the caller's own community standing on the tenant (subject = JWT sub)",
        ["RolesController.EffectiveMe"] =
            "self-scoped: the caller's own resolved access breakdown (subject = JWT sub)",
        ["PronounsController.GetMe"] =
            "self-scoped: the caller's own pronoun state (subject = JWT sub)",
        ["CurrencyController.GetMyAccount"] =
            "self-bound: only ever returns the CALLER's wallet (economy.md §5 accounts/me, community / Everyone)",
        // ── Own-session auth surface (identity-auth.md §5: '— (any authenticated user, own session)') ──
        ["AuthController.GetCurrentUser"] =
            "own session: auth/me returns the caller's own identity (identity-auth.md §5)",
        ["AuthController.Logout"] =
            "own session: revokes the caller's own session tokens (identity-auth.md §5)",
        ["AuthController.LogoutAll"] =
            "own sessions: revokes every session of the caller themselves (identity-auth.md §5)",
        // ── Community-plane Everyone floor with NO seeded action key (spec-cited) ──
        ["MusicController.GetQueue"] =
            "community / Everyone with no action key (music-sr.md §5.1 'GET queue — community / Everyone'; 'No new action keys are introduced')",
        ["MusicController.GetNowPlaying"] =
            "community / Everyone with no action key (music-sr.md §5.1 'GET now-playing — community / Everyone')",
    };

    private static bool IsExplicitlyGated(MemberInfo member)
    {
        if (member.GetCustomAttributes<RequireActionAttribute>(inherit: true).Any())
            return true;
        if (member.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true).Any())
            return true;
        return member
            .GetCustomAttributes<AuthorizeAttribute>(inherit: true)
            .Any(a => !string.IsNullOrEmpty(a.Roles) || !string.IsNullOrEmpty(a.Policy));
    }

    private static IReadOnlyList<Type> ControllerTypes() =>
        [
            .. typeof(BaseController)
                .Assembly.GetTypes()
                .Where(t =>
                    !t.IsAbstract && t.IsPublic && typeof(ControllerBase).IsAssignableFrom(t)
                ),
        ];

    private static IReadOnlyList<MethodInfo> ActionMethods(Type controller) =>
        [
            .. controller
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m =>
                    !m.IsSpecialName
                    && m.GetCustomAttribute<NonActionAttribute>() is null
                    && m.GetCustomAttributes<HttpMethodAttribute>().Any()
                ),
        ];

    [Fact]
    public void Every_controller_action_is_explicitly_gated_or_allowlisted_with_a_reason()
    {
        List<string> violations = [];

        foreach (Type controller in ControllerTypes())
        {
            bool classGated = IsExplicitlyGated(controller);
            foreach (MethodInfo action in ActionMethods(controller))
            {
                string key = $"{controller.Name}.{action.Name}";
                bool gated = classGated || IsExplicitlyGated(action);
                if (!gated && !Allowlist.ContainsKey(key))
                    violations.Add(key);
            }
        }

        // Joined into one string so a failure prints EVERY offending endpoint, not "at least one item".
        string.Join(Environment.NewLine, violations)
            .Should()
            .BeEmpty(
                "every endpoint must carry [RequireAction] / [AllowAnonymous] / [Authorize(Roles|Policy)] "
                    + "or be allowlisted here with a documented reason — an ungated endpoint is reachable by "
                    + "ANY authenticated user now that Gate 1 is pure entry"
            );
    }

    [Fact]
    public void Allowlist_carries_no_stale_entries()
    {
        HashSet<string> knownUngated = [];
        foreach (Type controller in ControllerTypes())
        {
            bool classGated = IsExplicitlyGated(controller);
            foreach (MethodInfo action in ActionMethods(controller))
            {
                if (!classGated && !IsExplicitlyGated(action))
                    knownUngated.Add($"{controller.Name}.{action.Name}");
            }
        }

        IReadOnlyList<string> stale = [.. Allowlist.Keys.Where(k => !knownUngated.Contains(k))];
        string.Join(Environment.NewLine, stale)
            .Should()
            .BeEmpty(
                "an allowlist entry whose endpoint no longer exists (or is now gated) must be removed, "
                    + "so the list always reflects reality"
            );
    }
}
