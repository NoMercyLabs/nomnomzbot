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
using NomNomzBot.Application.Abstractions.Pipeline;

namespace NomNomzBot.Infrastructure.Tests.Localization;

/// <summary>
/// S-SCHEMA-I18N-c's authoring-completeness guard: every registered <see cref="ICommandAction"/>'s every
/// <see cref="PipelineActionFieldDescriptor"/> must carry a non-null <see cref="PipelineActionFieldDescriptor.Description"/>
/// — the field's help text physically cannot reach the dashboard step form otherwise (a bare
/// <c>Description: null</c> is exactly how 38 of the 46 discovered actions shipped before this slice, an
/// optional field nothing enforced). Walks the REAL production DI graph — the same assembly scan the app boots
/// with — so a new action or a new field on an existing action fails this test loud, by action type and field
/// name, the moment it ships without help text; there is no hand-written action list to fall out of sync.
/// </summary>
public sealed class PipelineActionFieldCoverageTests
{
    [Fact]
    public void Every_field_on_every_registered_action_has_description_help_text()
    {
        using ServiceProvider provider = BuildActionProvider();

        List<string> undescribedFields =
        [
            .. provider
                .GetServices<ICommandAction>()
                .SelectMany(action =>
                    action
                        .Fields.Where(field => field.Description is null)
                        .Select(field => $"{action.ActionType}.{field.Name}")
                ),
        ];

        undescribedFields
            .Should()
            .BeEmpty(
                "every pipeline action field must carry operator-facing help text "
                    + "(PipelineActionFieldDescriptor.Description) so it can render in the step form's help "
                    + $"text — missing on: {string.Join(", ", undescribedFields)}"
            );
    }

    private static ServiceProvider BuildActionProvider()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Encryption:Key"] = Convert.ToBase64String(new byte[32]),
                    ["Jwt:Secret"] = "test-secret-key-at-least-32-characters-long!!",
                    ["ConnectionStrings:DefaultConnection"] =
                        "Host=localhost;Database=pipeline_field_coverage_test;Username=test;Password=test",
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
}
