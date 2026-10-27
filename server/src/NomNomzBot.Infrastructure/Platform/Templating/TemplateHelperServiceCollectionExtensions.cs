// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using NomNomzBot.Application.Abstractions.Templating;

namespace NomNomzBot.Infrastructure.Platform.Templating;

/// <summary>
/// Registers the template helper registry and every <see cref="ITemplateHelperProvider"/> class in one assembly
/// scan (the <c>IStreamMetric</c> pattern): a new namespace is a new class, and no shared file changes.
/// </summary>
public static class TemplateHelperServiceCollectionExtensions
{
    public static IServiceCollection AddTemplateHelpers(
        this IServiceCollection services,
        Assembly providerAssembly
    )
    {
        services.AddImplementationsOf<ITemplateHelperProvider>(
            providerAssembly,
            ServiceLifetime.Singleton
        );
        services.AddSingleton<ITemplateHelperRegistry, TemplateHelperRegistryAggregate>();
        return services;
    }
}
