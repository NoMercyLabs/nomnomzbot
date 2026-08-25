// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NomNomzBot.Application;
using NomNomzBot.Application.Abstractions.Localization;
using NomNomzBot.Application.Abstractions.Pipeline;

namespace NomNomzBot.Infrastructure.Tests.Platform;

/// <summary>
/// S-SCHEMA-I18N guard, pipeline side: every <see cref="ICommandAction"/> that opts a field into help text via
/// <see cref="PipelineActionFieldDescriptor.Description"/> must carry BOTH an English and a Dutch translation.
/// This resolves the REAL <see cref="ICommandAction"/> set from the production DI graph (the same
/// <c>AddImplementationsOf&lt;ICommandAction&gt;</c> assembly scan the app boots with — see
/// <see cref="Platform.AssemblyScanDiscoveryTests"/>) and reads each action's real <c>Fields</c>, so a new action
/// or a new field is swept automatically; there is no hand-maintained list to drift.
/// </summary>
public sealed class PipelineActionFieldI18nTests
{
    private static ServiceProvider BuildProvider()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Encryption:Key"] = Convert.ToBase64String(new byte[32]),
                    ["Jwt:Secret"] = "test-secret-key-at-least-32-characters-long!!",
                    ["ConnectionStrings:DefaultConnection"] =
                        "Host=localhost;Database=i18n_test;Username=test;Password=test",
                }
            )
            .Build();

        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton(configuration);
        services.AddApplication();
        services.AddInfrastructure(configuration);

        return services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = false, ValidateScopes = false }
        );
    }

    [Fact]
    public void Every_action_field_description_that_exists_carries_both_english_and_dutch_translations()
    {
        using ServiceProvider provider = BuildProvider();
        List<ICommandAction> actions = [.. provider.GetServices<ICommandAction>()];

        actions.Should().NotBeEmpty("the assembly defines pipeline actions to sweep");

        foreach (ICommandAction action in actions)
        foreach (PipelineActionFieldDescriptor field in action.Fields)
        {
            if (field.Description is null)
                continue;

            LocalizedText description = field.Description;
            string what =
                $"'{action.ActionType}.{field.Name}' description (key '{description.Key}')";

            description.Key.Should().NotBeNullOrWhiteSpace($"{what} needs a translation key");
            description.En.Should().NotBeNullOrWhiteSpace($"{what} needs an English translation");
            description.Nl.Should().NotBeNullOrWhiteSpace($"{what} needs a Dutch translation");
        }
    }
}
