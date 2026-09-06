// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Identity.Dtos;

/// <summary>
/// The real, three-way state of the shared platform bot (S-BOT-PLATFORM-UI) — distinct from
/// <see cref="Services.BotStatusDto"/>'s boolean <c>Connected</c>, which collapses "never connected" and
/// "connected but the stored token no longer decrypts" (e.g. after an <c>ENCRYPTION_KEY</c> rotation) into
/// the same <c>false</c>. The admin surface must tell those apart: one means "run first-run setup", the
/// other means "re-authorize the existing bot" — very different operator actions. A plain string constants
/// class, not a C# enum — no global <c>JsonStringEnumConverter</c> is registered, so an enum would wire as
/// an integer; this matches the project's own convention for a wire-facing state (e.g. <c>NetworkBlockStatus</c>).
/// </summary>
public static class PlatformBotConnectionState
{
    public const string NeverConnected = "NeverConnected";
    public const string ConnectedAndWorking = "ConnectedAndWorking";
    public const string ConnectedTokenUnusable = "ConnectedTokenUnusable";
}

/// <summary>The platform bot admin screen's read model — real state, read from the stored credential.</summary>
public sealed record PlatformBotAdminStatusDto(
    string State,
    string? BotUsername,
    string? BotDisplayName
);

/// <summary>
/// The counted blast radius a platform-bot reconnect/replace would touch — every channel that has no
/// active custom bot of its own and therefore speaks through the shared platform bot. Must be rendered
/// before a reconnect is armed, and re-verified fresh (not trusted from a stale preview) when the swap
/// actually applies.
/// </summary>
public sealed record PlatformBotReconnectPreviewDto(int AffectedChannelCount);
