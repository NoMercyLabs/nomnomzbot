// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Moderation.Dtos;

/// <summary>
/// Whether protection is running for one channel: one check per thing a streamer relies on. A check that
/// could not be read is reported as <see cref="ProtectionCheckStates.Unknown"/>, never as healthy.
/// </summary>
/// <param name="Checks">Every check, in a stable order.</param>
public sealed record ProtectionStatusDto(IReadOnlyList<ProtectionCheckDto> Checks);

/// <summary>One protection check and its current state.</summary>
/// <param name="Key">Which check: <c>bot_moderator</c>, <c>spam_defense_mode</c>, <c>automatic_action</c> or <c>eventsub_moderation</c>.</param>
/// <param name="State">One of <see cref="ProtectionCheckStates"/>.</param>
/// <param name="Platform">The platform the check is about, or null when it covers the whole channel.</param>
/// <param name="Reason">Plain English: why the check is in this state, and what is missing when it is not ok.</param>
public sealed record ProtectionCheckDto(string Key, string State, string? Platform, string Reason);

/// <summary>The wire values of <see cref="ProtectionCheckDto.State"/>.</summary>
public static class ProtectionCheckStates
{
    public const string Ok = "ok";
    public const string Warning = "warning";
    public const string Failing = "failing";
    public const string Unknown = "unknown";
}

/// <summary>The wire values of <see cref="ProtectionCheckDto.Key"/>.</summary>
public static class ProtectionCheckKeys
{
    public const string BotModerator = "bot_moderator";
    public const string SpamDefenseMode = "spam_defense_mode";
    public const string AutomaticAction = "automatic_action";
    public const string EventSubModeration = "eventsub_moderation";
}
