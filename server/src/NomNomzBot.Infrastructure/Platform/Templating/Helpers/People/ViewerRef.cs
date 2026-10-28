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
