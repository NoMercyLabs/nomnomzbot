// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Common.Exceptions;

/// <summary>
/// Thrown when the current user does not have permission to perform the requested action.
/// </summary>
public sealed class ForbiddenAccessException : Exception
{
    public ForbiddenAccessException()
        : base("You do not have permission to perform this action.") { }

    public ForbiddenAccessException(string message)
        : base(message) { }

    public ForbiddenAccessException(string userId, string resource)
        : base($"User '{userId}' does not have access to '{resource}'.") { }
}
