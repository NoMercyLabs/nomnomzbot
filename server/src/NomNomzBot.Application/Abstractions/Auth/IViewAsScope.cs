// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
