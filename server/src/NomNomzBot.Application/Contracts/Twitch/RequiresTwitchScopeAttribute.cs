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
/// Declares that a Helix sub-client method requires a specific Twitch OAuth scope to succeed.
/// Applied directly above the concrete implementation method that performs the runtime
/// <c>RequireScopeAsync</c> check, so the required scope is declared exactly once, next to the
/// code that enforces it — <c>TwitchScopeRegistry</c> (in NomNomzBot.Infrastructure)
/// reflects over these attributes to build the authoritative set of scopes the bot must request
/// at login, eliminating the drift between the runtime check and the hand-maintained login scope
/// list that used to happen (e.g. <c>moderator:manage:shoutouts</c> silently missing for weeks).
/// A method can carry multiple instances when it accepts more than one acceptable scope.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class RequiresTwitchScopeAttribute(string scope) : Attribute
{
    public string Scope { get; } = scope;
}
