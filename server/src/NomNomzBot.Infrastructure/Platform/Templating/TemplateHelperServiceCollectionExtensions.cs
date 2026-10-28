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
