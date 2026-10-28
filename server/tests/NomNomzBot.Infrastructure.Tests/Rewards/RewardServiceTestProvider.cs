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
