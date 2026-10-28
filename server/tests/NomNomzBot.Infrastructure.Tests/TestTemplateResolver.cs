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
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Platform.Templating;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests;

/// <summary>
/// The real <see cref="TemplateResolver"/> for tests that fill a reply or an announcement from seed variables.
/// Only the outside world is faked: the database scope, the channel registry and (optionally) the clock.
/// </summary>
internal static class TestTemplateResolver
{
    public static TemplateResolver Create(TimeProvider? clock = null)
    {
        IChannelRegistry registry = Substitute.For<IChannelRegistry>();
        registry.Get(Arg.Any<Guid>()).Returns((ChannelContext?)null);

        return new TemplateResolver(
            Substitute.For<IServiceScopeFactory>(),
            registry,
            NullLogger<TemplateResolver>.Instance,
            clock ?? TimeProvider.System
        );
    }
}
