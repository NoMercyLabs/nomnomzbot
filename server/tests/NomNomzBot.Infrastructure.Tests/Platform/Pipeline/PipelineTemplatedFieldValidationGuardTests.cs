// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NomNomzBot.Application;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Infrastructure.Tests.Platform.Pipeline;

/// <summary>
/// S042b regression guard: enumerates every REAL registered <see cref="ICommandAction"/> from the live DI
/// graph (not a hand-written list — the same discovery <see cref="PipelineActionFieldSchemaGuardTests"/>
/// uses) and, for each field the action itself marks <see cref="PipelineActionFieldDescriptor.Templated"/>,
/// proves the real <c>ICommandConfigValidator</c> wired through DI rejects an unknown helper key in that
/// field. A NEW pipeline action that adds a templated field is automatically covered by this test the
/// moment it registers via the assembly scan — nothing here needs updating by hand, and a wiring
/// regression (e.g. a field name mismatch between the descriptor and <c>CommandConfigValidator</c>'s
/// lookup) fails loud, by action type and field name, instead of silently shipping unvalidated.
/// </summary>
public sealed class PipelineTemplatedFieldValidationGuardTests
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
                        "Host=localhost;Database=templated_field_guard;Username=test;Password=test",
                }
            )
            .Build();

        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton(configuration);
        services.AddApplication();
        services.AddInfrastructure(configuration);

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Every_templated_field_on_every_registered_action_rejects_an_unknown_helper()
    {
        using ServiceProvider provider = BuildProvider();
        ICommandConfigValidator validator = provider.GetRequiredService<ICommandConfigValidator>();
        List<ICommandAction> actions = [.. provider.GetServices<ICommandAction>()];
        actions.Should().NotBeEmpty("the assembly registers pipeline actions to check");

        List<(string ActionType, string FieldName)> templatedFields =
        [
            .. actions.SelectMany(a =>
                a.Fields.Where(f => f.Templated).Select(f => (a.ActionType, f.Name))
            ),
        ];
        templatedFields
            .Should()
            .NotBeEmpty("at least send_message/send_reply declare templated fields");

        List<string> unvalidated = [];
        foreach ((string actionType, string fieldName) in templatedFields)
        {
            PipelineGraphInput graph = new([
                new PipelineStepInput(
                    actionType,
                    new Dictionary<string, object?>
                    {
                        [fieldName] = JsonSerializer.SerializeToElement(
                            "{totally.not.a.real.helper}"
                        ),
                    }
                ),
            ]);

            Result<PipelineValidationResult> result = await validator.ValidatePipelineAsync(graph);
            if (result.IsFailure || result.Value.IsValid)
                unvalidated.Add(
                    $"{actionType}.{fieldName} accepted an unknown template helper without rejecting it"
                );
        }

        unvalidated
            .Should()
            .BeEmpty(
                "every Templated pipeline action field must be checked against the helper registry at save time"
            );
    }
}
