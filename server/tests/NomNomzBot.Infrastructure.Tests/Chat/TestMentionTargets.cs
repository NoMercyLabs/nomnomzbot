// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Infrastructure.Chat;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Chat;

/// <summary>
/// The real <see cref="MentionTargetResolver"/> for tests that only type login-shaped targets: such a target
/// resolves without touching the database, so an empty substitute context is enough.
/// </summary>
internal static class TestMentionTargets
{
    public static IMentionTargetResolver LoginsOnly() =>
        new MentionTargetResolver(Substitute.For<IApplicationDbContext>());
}
