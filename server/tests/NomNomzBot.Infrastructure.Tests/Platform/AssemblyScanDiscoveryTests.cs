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
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NomNomzBot.Application;
using NomNomzBot.Application.Abstractions.Content;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Tests.Platform;

/// <summary>
/// Proves the §4 (D5) assembly scan actually DISCOVERS every pluggable artifact — not just
/// that the code compiles. Each expected count is computed from the assembly by reflection,
/// so if a new artifact is added (or the scan stops finding one), the relevant test fails.
/// The final test builds the real DI graph with ValidateOnBuild + ValidateScopes to prove the
/// container resolves everything the scan registered.
/// </summary>
public class AssemblyScanDiscoveryTests
{
    private static readonly Assembly InfrastructureAssembly = typeof(DependencyInjection).Assembly;

    private static ServiceProvider BuildProvider(bool validate)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    // No Redis → in-memory cache branch; no Twitch creds needed to build the graph.
                    ["Encryption:Key"] = Convert.ToBase64String(new byte[32]),
                    ["Jwt:Secret"] = "test-secret-key-at-least-32-characters-long!!",
                    ["ConnectionStrings:DefaultConnection"] =
                        "Host=localhost;Database=scan_test;Username=test;Password=test",
                }
            )
            .Build();

        ServiceCollection services = new();
        services.AddLogging();
        // IConfiguration is an ambient framework service the real host registers automatically
        // (WebApplication.CreateBuilder); register it so the standalone graph mirrors production.
        services.AddSingleton(configuration);
        services.AddApplication();
        services.AddInfrastructure(configuration);

        return services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = validate, ValidateScopes = validate }
        );
    }

    private static int CountConcreteAssignableTo(Type marker) =>
        InfrastructureAssembly
            .GetTypes()
            .Count(t =>
                t is { IsAbstract: false, IsInterface: false, IsGenericTypeDefinition: false }
                && marker.IsAssignableFrom(t)
            );

    [Fact]
    public void Scan_discovers_every_ICommandAction_in_the_assembly()
    {
        int expected = CountConcreteAssignableTo(typeof(ICommandAction));
        expected.Should().BeGreaterThan(0, "the assembly defines pipeline actions to discover");

        using ServiceProvider provider = BuildProvider(validate: false);
        List<ICommandAction> actions = provider.GetServices<ICommandAction>().ToList();

        actions.Should().HaveCount(expected);
        // Distinct concrete types — no action registered twice or dropped.
        actions.Select(a => a.GetType()).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Scan_discovers_every_ICommandCondition_in_the_assembly()
    {
        int expected = CountConcreteAssignableTo(typeof(ICommandCondition));
        expected.Should().BeGreaterThan(0);

        using ServiceProvider provider = BuildProvider(validate: false);
        List<ICommandCondition> conditions = provider.GetServices<ICommandCondition>().ToList();

        conditions.Should().HaveCount(expected);
        conditions.Select(c => c.GetType()).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Scan_discovers_every_IMusicProvider_in_the_assembly()
    {
        int expected = CountConcreteAssignableTo(typeof(IMusicProvider));
        expected.Should().BeGreaterThan(0, "music routing depends on IEnumerable<IMusicProvider>");

        using ServiceProvider provider = BuildProvider(validate: false);
        using IServiceScope scope = provider.CreateScope();
        List<IMusicProvider> providers = scope
            .ServiceProvider.GetServices<IMusicProvider>()
            .ToList();

        providers.Should().HaveCount(expected);
        providers.Select(p => p.GetType()).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Scan_discovers_every_ISeeder_in_the_assembly()
    {
        // The §5 content seeders self-register through the same §4 marker scan. The expected
        // count is reflection-computed, so adding a seeder keeps this honest; if the scan stops
        // finding one (e.g. the AddImplementationsOf<ISeeder> line is dropped), this fails.
        int expected = CountConcreteAssignableTo(typeof(ISeeder));
        expected.Should().BeGreaterThanOrEqualTo(4, "the four content seeders must be discovered");

        using ServiceProvider provider = BuildProvider(validate: false);
        using IServiceScope scope = provider.CreateScope();
        List<ISeeder> seeders = scope.ServiceProvider.GetServices<ISeeder>().ToList();

        seeders.Should().HaveCount(expected);
        seeders.Select(s => s.GetType()).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Scan_registers_a_handler_for_each_IEventHandler_closed_interface()
    {
        // Every concrete IEventHandler<TEvent> closed interface implemented in the assembly
        // must resolve to at least one handler — proves no handler type is missed.
        // IsGenericTypeDefinition: false matches the scanner's own rule — the open-generic automation
        // bridge handler yields an OPEN IEventHandler<TEvent> interface that can't (and shouldn't)
        // resolve; its closed forms are registered per described event by the descriptor DI loop.
        List<Type> closedHandlerInterfaces = InfrastructureAssembly
            .GetTypes()
            .Where(t =>
                t is { IsAbstract: false, IsInterface: false, IsGenericTypeDefinition: false }
            )
            .SelectMany(t => t.GetInterfaces())
            .Where(i =>
                i.IsGenericType
                && i.GetGenericTypeDefinition() == typeof(IEventHandler<>)
                && !i.ContainsGenericParameters
            )
            .Distinct()
            .ToList();

        closedHandlerInterfaces.Should().NotBeEmpty();

        using ServiceProvider provider = BuildProvider(validate: false);
        using IServiceScope scope = provider.CreateScope();

        foreach (Type handlerInterface in closedHandlerInterfaces)
        {
            object? handlers = scope.ServiceProvider.GetService(
                typeof(IEnumerable<>).MakeGenericType(handlerInterface)
            );
            ((System.Collections.IEnumerable)handlers!)
                .Cast<object>()
                .Should()
                .NotBeEmpty($"a handler must be discovered for {handlerInterface.Name}");
        }
    }

    [Fact]
    public void Scan_registers_every_BackgroundService_worker_as_a_hosted_service()
    {
        // Includes the singleton+hosted trio (Irc/EventSub/ChannelRegistry) wired explicitly
        // AND the plain workers wired by the scan — TokenRefreshService must now be among them.
        int expectedWorkers = CountConcreteAssignableTo(typeof(BackgroundService)) + 1; // ChannelRegistry implements IHostedService directly, not BackgroundService.

        using ServiceProvider provider = BuildProvider(validate: false);
        List<IHostedService> hosted = provider.GetServices<IHostedService>().ToList();

        hosted
            .Select(h => h.GetType().Name)
            .Should()
            .Contain(
                "TokenRefreshService",
                "the previously-unregistered token refresh worker must now run"
            );
        hosted.Should().HaveCountGreaterThanOrEqualTo(expectedWorkers);
    }

    [Fact]
    public void Provider_builds_with_ValidateOnBuild_and_ValidateScopes()
    {
        // The reliability proof: if the scan left any registered service with an unresolvable
        // dependency, or a singleton capturing a scoped service, this throws.
        Action build = () =>
        {
            using ServiceProvider provider = BuildProvider(validate: true);
        };

        build.Should().NotThrow();
    }
}
