// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Contracts.Security;

/// <summary>
/// Why the bot is allowed to change something on a third-party platform. Every outbound WRITE must carry one;
/// reads never do, because reading changes nothing that belongs to somebody else.
///
/// <para>
/// This exists because inspection could not answer "does the bot only act when sanctioned". On 2026-09-04 a
/// background handler granted a Twitch moderator role on channels whose broadcasters were never asked, and
/// auditing by hand missed a second writer on the first spot-check. A property that a careful read of the code
/// cannot establish has to be enforced by the code instead.
/// </para>
/// </summary>
/// <param name="Basis">How the action was sanctioned — see <see cref="OutboundSanctionBasis"/>.</param>
/// <param name="Detail">
/// What specifically authorised it: the Gate-2 action key for a user action, or the channel-scoped rule or
/// setting for a configured one. This is the string that has to be defensible when a broadcaster asks why
/// their channel changed.
/// </param>
/// <param name="ActorUserId">The operator who asked for it, when a person asked at all.</param>
public sealed record OutboundSanction(string Basis, string Detail, Guid? ActorUserId = null)
{
    /// <summary>A person asked for this, through an endpoint that already checked they may.</summary>
    public static OutboundSanction UserAction(string actionKey, Guid? actorUserId) =>
        new(OutboundSanctionBasis.UserAction, actionKey, actorUserId);

    /// <summary>
    /// The channel's own stored configuration asked for it — an automod rule, a chat filter, a timer. No
    /// person is present at the moment it runs, but the broadcaster opted in when they saved the setting, and
    /// <paramref name="detail"/> names which setting so the trail leads back to it.
    /// </summary>
    public static OutboundSanction ChannelConfiguration(string detail) =>
        new(OutboundSanctionBasis.ChannelConfiguration, detail);

    /// <summary>
    /// A deployment-level decision by the platform owner, pinned in configuration rather than decided at
    /// runtime — currently only the SaaS bot identity that may be granted moderator on onboarding.
    /// </summary>
    public static OutboundSanction PlatformConfiguration(string detail) =>
        new(OutboundSanctionBasis.PlatformConfiguration, detail);
}

public static class OutboundSanctionBasis
{
    public const string UserAction = "user_action";
    public const string ChannelConfiguration = "channel_configuration";
    public const string PlatformConfiguration = "platform_configuration";
}

/// <summary>
/// Ambient access to the sanction covering the current logical operation. Ambient rather than a parameter
/// because the alternative is threading it through 69 write methods and every service between them — and a
/// parameter that can be defaulted is a parameter that gets defaulted.
/// </summary>
public interface IOutboundSanctionAccessor
{
    /// <summary>The sanction in force, or <c>null</c> when nothing has claimed one.</summary>
    OutboundSanction? Current { get; }

    /// <summary>Puts <paramref name="sanction"/> in force until the returned scope is disposed.</summary>
    IDisposable Begin(OutboundSanction sanction);
}
