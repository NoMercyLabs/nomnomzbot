// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.People;

/// <summary>Which person of a render a <c>user.*</c> or <c>target.*</c> row reads.</summary>
internal enum ViewerSide
{
    User,
    Target,
}

/// <summary>Which sides a row exists for.</summary>
[Flags]
internal enum ViewerSides
{
    User = 1,
    Target = 2,
    Both = User | Target,
}

/// <summary>
/// One person of a render. <see cref="UserId"/> is the platform user's key in this database (null when the
/// person has no record); <see cref="PlatformUserId"/> is the id on the provider (the Twitch id), null when unknown.
/// </summary>
internal sealed record ViewerRef(
    Guid? UserId,
    string? PlatformUserId,
    string Login,
    string Display,
    string Provider
);
