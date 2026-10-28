// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
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
