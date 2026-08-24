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
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Infrastructure.Platform.Deployment;

namespace NomNomzBot.Infrastructure.Tests.Platform.Deployment;

/// <summary>
/// Z3: the run-once guard must be selected by DATABASE PROVIDER, not deployment mode. A process-local guard on a
/// Postgres-backed profile would silently let two zero-downtime-deploy instances both run singleton work (migrate
/// / seed / conduit-provision) against the same database. Proves the DI graph binds
/// <see cref="PostgresRunOnceGuard"/> for EVERY Postgres profile (self_host_full AND saas — not saas alone, the
/// bug this slice fixes) and <see cref="NoOpRunOnceGuard"/> only for the SQLite profile (self_host_lite).
/// </summary>
public sealed class RunOnceGuardRegistrationTests
{
    private static ServiceProvider BuildProvider(string deploymentMode)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Deployment:Mode"] = deploymentMode,
                    ["Encryption:Key"] = Convert.ToBase64String(new byte[32]),
                    ["Jwt:Secret"] = "test-secret-key-at-least-32-characters-long!!",
                    ["ConnectionStrings:DefaultConnection"] =
                        "Host=localhost;Database=runonce_guard_test;Username=test;Password=test",
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

    [Theory]
    [InlineData("SelfHostFull")]
    [InlineData("Saas")]
    public void Postgres_backed_profiles_get_the_cross_process_advisory_lock_guard(string mode)
    {
        using ServiceProvider provider = BuildProvider(mode);

        IRunOnceGuard guard = provider.GetRequiredService<IRunOnceGuard>();

        guard
            .Should()
            .BeOfType<PostgresRunOnceGuard>(
                $"{mode} runs on Postgres, so two overlapping instances during a zero-downtime deploy "
                    + "must contend for the SAME lease across processes, not just within one"
            );
    }

    [Fact]
    public void Sqlite_backed_self_host_lite_keeps_the_in_process_no_op_guard()
    {
        using ServiceProvider provider = BuildProvider("SelfHostLite");

        IRunOnceGuard guard = provider.GetRequiredService<IRunOnceGuard>();

        guard
            .Should()
            .BeOfType<NoOpRunOnceGuard>(
                "a second process against one SQLite file is not a supported topology, so no "
                    + "cross-process guard is needed"
            );
    }
}
