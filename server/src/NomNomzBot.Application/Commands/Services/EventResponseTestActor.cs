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

namespace NomNomzBot.Application.Commands.Services;

/// <summary>The user a test fire acts as: the person who pressed the test button, never an invented viewer.</summary>
/// <param name="UserId">The platform user id the live event would carry.</param>
/// <param name="Login">The lowercase login, for <c>{user.name}</c>.</param>
/// <param name="DisplayName">The display name, for <c>{user}</c>.</param>
public sealed record EventResponseTestActor(string UserId, string Login, string DisplayName);
