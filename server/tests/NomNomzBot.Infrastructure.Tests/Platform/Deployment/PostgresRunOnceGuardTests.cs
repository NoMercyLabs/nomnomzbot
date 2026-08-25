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
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Infrastructure.Platform.Deployment;

namespace NomNomzBot.Infrastructure.Tests.Platform.Deployment;

/// <summary>
/// Z3: proves <see cref="PostgresRunOnceGuard"/> actually excludes ACROSS PROCESSES — the whole point of
/// registering it in place of the process-local <see cref="NoOpRunOnceGuard"/> on Postgres profiles. Each test
/// stands up TWO independent guard instances, each with its OWN dedicated Npgsql connection, exactly mirroring
/// two separate API instances in a zero-downtime-deploy overlap contending for the same named resource against
/// the same database — a single in-process dictionary (what the no-op guard uses) could never fail this test,
/// which is precisely the gap this slice closes.
/// </summary>
public sealed class PostgresRunOnceGuardTests
{
    // Matches the local dev Postgres container (docker-compose.yml `postgres` service / .env defaults).
    // Internal so PostgresFactAttribute can probe the same target these tests connect to.
    internal static readonly string ConnectionString =
        Environment.GetEnvironmentVariable("NNZ_TEST_PG_CONNECTION")
        ?? "Host=localhost;Port=5432;Database=nomnomzbot;Username=nomnomzbot;Password="
            + (Environment.GetEnvironmentVariable("POSTGRES_PASSWORD") ?? "nomnomzbot_dev");

    private static PostgresRunOnceGuard NewGuard() =>
        new(
            new ConfigurationBuilder()
                .AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:DefaultConnection"] = ConnectionString,
                    }
                )
                .Build(),
            NullLogger<PostgresRunOnceGuard>.Instance
        );

    [PostgresFact]
    public async Task Two_separate_guard_instances_contend_for_the_same_resource_and_exactly_one_wins()
    {
        string resourceName = $"z3-contend-{Guid.NewGuid():N}";
        PostgresRunOnceGuard first = NewGuard();
        PostgresRunOnceGuard second = NewGuard();

        IAsyncDisposable? firstLease = await first.TryAcquireAsync(
            resourceName,
            TimeSpan.FromMinutes(5)
        );
        IAsyncDisposable? secondLease = await second.TryAcquireAsync(
            resourceName,
            TimeSpan.FromMinutes(5)
        );

        try
        {
            firstLease.Should().NotBeNull("the first, uncontested instance must win the lease");
            secondLease
                .Should()
                .BeNull(
                    "a second, SEPARATE guard instance/connection contending for the same "
                        + "resourceName must be excluded while the first instance still holds it"
                );
        }
        finally
        {
            if (firstLease is not null)
                await firstLease.DisposeAsync();
            if (secondLease is not null)
                await secondLease.DisposeAsync();
        }
    }

    [PostgresFact]
    public async Task Disposing_the_holder_releases_the_lease_for_the_other_instance_to_acquire()
    {
        string resourceName = $"z3-release-{Guid.NewGuid():N}";
        PostgresRunOnceGuard first = NewGuard();
        PostgresRunOnceGuard second = NewGuard();

        IAsyncDisposable? firstLease = await first.TryAcquireAsync(
            resourceName,
            TimeSpan.FromMinutes(5)
        );
        firstLease.Should().NotBeNull();

        // While the first instance still holds the lease, the second is excluded.
        IAsyncDisposable? blockedAttempt = await second.TryAcquireAsync(
            resourceName,
            TimeSpan.FromMinutes(5)
        );
        blockedAttempt.Should().BeNull();

        // The holder's process/connection goes away (dispose = the crash/shutdown path releasing the session).
        await firstLease.DisposeAsync();

        IAsyncDisposable? afterRelease = await second.TryAcquireAsync(
            resourceName,
            TimeSpan.FromMinutes(5)
        );

        try
        {
            afterRelease
                .Should()
                .NotBeNull(
                    "once the holder's session ends, the advisory lock is released and the "
                        + "other instance's next attempt must succeed — the self-heal this slice documents"
                );
        }
        finally
        {
            if (afterRelease is not null)
                await afterRelease.DisposeAsync();
        }
    }
}
