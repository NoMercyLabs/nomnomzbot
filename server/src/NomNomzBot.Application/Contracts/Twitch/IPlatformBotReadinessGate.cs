// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Contracts.Twitch;

/// <summary>
/// The single source of truth for "can we talk to Twitch as the bot yet". Twitch-dependent background work
/// (the EventSub transport, the IRC connection, the Helix cache warmers) must stay dormant on a fresh,
/// un-onboarded self-host until a platform bot account has been authorized — otherwise it reconnect-loops and
/// spams "No bot token is configured." This gate reads the same fact the token resolver reads to satisfy a
/// bot-scoped Helix/EventSub call: a usable, decryptable platform bot token exists. The full/SaaS path has the
/// bot authorized, so it returns <c>true</c> immediately there. Background loops re-evaluate it on a periodic
/// tick, so an onboarding completed at runtime activates them without a process restart.
/// </summary>
public interface IPlatformBotReadinessGate
{
    /// <summary>
    /// True once the shared platform bot account is authorized and its token decrypts — i.e. a bot-scoped
    /// Twitch call would succeed. False on a fresh, un-onboarded install (no bot connection) or when the
    /// stored token can no longer be read (e.g. after a KEK rotation, pending re-auth).
    /// </summary>
    Task<bool> IsPlatformBotConfiguredAsync(CancellationToken ct = default);
}
