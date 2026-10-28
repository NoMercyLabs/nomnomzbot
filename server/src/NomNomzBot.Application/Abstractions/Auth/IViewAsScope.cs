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

namespace NomNomzBot.Application.Abstractions.Auth;

/// <summary>
/// The "View as" choice of the current request (role-login-scopes.md, View as): the highest ladder level the
/// caller asked to act at. It can only lower the caller's real level, never raise it. Null when the request
/// carries no choice.
/// </summary>
public interface IViewAsScope
{
    int? CeilingLevel { get; }
}
