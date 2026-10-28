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
