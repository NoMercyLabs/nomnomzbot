// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.DependencyInjection;
using NomNomzBot.Application.Commands.Services;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Rewards;

/// <summary>
/// The service provider tests hand to <c>RewardService</c>, which resolves <see cref="IEventResponseExecutor"/>
/// from it per call (injecting the executor directly is a DI cycle).
/// </summary>
internal static class RewardServiceTestProvider
{
    public static IServiceProvider Create() =>
        new ServiceCollection()
            .AddSingleton(Substitute.For<IEventResponseExecutor>())
            .BuildServiceProvider();
}
