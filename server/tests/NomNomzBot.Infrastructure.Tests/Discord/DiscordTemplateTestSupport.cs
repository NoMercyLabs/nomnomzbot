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
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Platform.Templating;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Discord;

/// <summary>
/// A real <see cref="TemplateResolver"/> (not a stand-in) shared by the Discord notification tests
/// (S-TWO-TEMPLATE-ENGINES) — Discord renders through the SAME resolver every other template surface
/// uses, so these tests exercise it for real; only its unused DB/channel-registry dependency is faked.
/// </summary>
internal static class DiscordTemplateTestSupport
{
    public static ITemplateResolver CreateResolver()
    {
        IServiceScopeFactory scopeFactory = Substitute.For<IServiceScopeFactory>();
        IChannelRegistry registry = Substitute.For<IChannelRegistry>();
        registry.Get(Arg.Any<Guid>()).Returns((ChannelContext?)null);

        return new TemplateResolver(
            scopeFactory,
            registry,
            NullLogger<TemplateResolver>.Instance,
            TimeProvider.System
        );
    }

    public static ITemplateHelperValidator CreateValidator() => new TemplateHelperValidator();
}
