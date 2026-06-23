// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Linq;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Enums.Deployment;
using NomNomzBot.Domain.Platform;
using NomNomzBot.Domain.Platform.Entities;
using NomNomzBot.Domain.Platform.Events;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Platform.Deployment;
using NomNomzBot.Infrastructure.Platform.Persistence;
using NomNomzBot.Infrastructure.Tests.Identity;
using Xunit;

namespace NomNomzBot.Infrastructure.Tests.Deployment;

/// <summary>
/// Proves the deployment-profile detector (platform-conventions §3.3, deployment-distribution §2): the resolved
/// mode follows the probe inputs, an explicit override beats detection, and the persisted DeploymentProfile row
/// carries the exact adapter kinds for the mode — and the resolved event fires with the same shape. Detection runs
/// against a REAL in-memory SQLite AppDbContext (which also proves the production model materializes on SQLite).
/// </summary>
public sealed class DeploymentProfileServiceTests
{
    [Fact]
    public async Task Detects_full_when_both_postgres_and_redis_are_reachable()
    {
        await using Harness harness = Harness.Create(new StubProbe(postgres: true, redis: true));

        Result<DeploymentProfileSnapshot> result = await harness.Service.DetectAndPersistAsync();

        result.IsSuccess.Should().BeTrue();
        DeploymentProfileSnapshot snapshot = result.Value;
        snapshot.Mode.Should().Be(DeploymentMode.SelfHostFull);
        snapshot.WasAutoDetected.Should().BeTrue();
        snapshot.DbProvider.Should().Be(DbProviderKind.Postgres);
        snapshot.CacheProvider.Should().Be(CacheProviderKind.Redis);
        snapshot.EventSubTransport.Should().Be(EventSubTransportMode.WebSocket);
        snapshot.CodeExecutor.Should().Be(CodeExecutorKind.Jint);
        snapshot.RlsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task Detects_lite_when_postgres_is_unreachable()
    {
        await using Harness harness = Harness.Create(new StubProbe(postgres: false, redis: true));

        DeploymentProfileSnapshot snapshot = (await harness.Service.DetectAndPersistAsync()).Value;

        snapshot.Mode.Should().Be(DeploymentMode.SelfHostLite);
        snapshot.DbProvider.Should().Be(DbProviderKind.Sqlite);
        snapshot.CacheProvider.Should().Be(CacheProviderKind.InMemory);
        snapshot.RlsEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task Detects_lite_when_redis_is_unreachable()
    {
        await using Harness harness = Harness.Create(new StubProbe(postgres: true, redis: false));

        DeploymentProfileSnapshot snapshot = (await harness.Service.DetectAndPersistAsync()).Value;

        snapshot.Mode.Should().Be(DeploymentMode.SelfHostLite);
    }

    [Fact]
    public async Task Honors_explicit_override_over_a_full_capable_environment()
    {
        // Both services are reachable (would auto-detect full), but the operator forced lite.
        await using Harness harness = Harness.Create(
            new StubProbe(postgres: true, redis: true),
            ("Deployment:Mode", "self_host_lite")
        );

        DeploymentProfileSnapshot snapshot = (await harness.Service.DetectAndPersistAsync()).Value;

        snapshot.Mode.Should().Be(DeploymentMode.SelfHostLite);
        snapshot.WasAutoDetected.Should().BeFalse();
        snapshot.DbProvider.Should().Be(DbProviderKind.Sqlite);
    }

    [Fact]
    public async Task Persists_the_single_row_and_emits_the_resolved_event()
    {
        RecordingEventBus eventBus = new();
        await using Harness harness = Harness.Create(
            new StubProbe(postgres: false, redis: false),
            eventBus: eventBus
        );

        DeploymentProfileSnapshot snapshot = (await harness.Service.DetectAndPersistAsync()).Value;

        // The persisted row exists, is singular, and carries the resolved adapter kinds.
        await using AppDbContext verifyDb = harness.NewDbContext();
        List<DeploymentProfile> rows = await verifyDb.DeploymentProfiles.ToListAsync();
        rows.Should().HaveCount(1);
        DeploymentProfile row = rows[0];
        row.InstanceId.Should().Be(snapshot.InstanceId);
        row.Mode.Should().Be(DeploymentMode.SelfHostLite);
        row.DbProvider.Should().Be(DbProviderKind.Sqlite);
        row.CacheProvider.Should().Be(CacheProviderKind.InMemory);
        row.TokenVault.Should().Be(TokenVaultKind.LocalAes);
        row.DefaultGuidanceLevel.Should().Be(GuidanceLevel.Novice);

        // The resolved event fired with the matching shape.
        DeploymentProfileResolvedEvent published = eventBus
            .Published.OfType<DeploymentProfileResolvedEvent>()
            .Single();
        published.Mode.Should().Be("SelfHostLite");
        published.DbProvider.Should().Be("Sqlite");
        published.CacheProvider.Should().Be("InMemory");
        published.InstanceId.Should().Be(snapshot.InstanceId);
        published.RlsEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task Re_resolving_keeps_the_single_row_and_a_stable_instance_id()
    {
        await using Harness harness = Harness.Create(new StubProbe(postgres: false, redis: false));

        Guid firstInstance = (await harness.Service.DetectAndPersistAsync()).Value.InstanceId;
        Guid secondInstance = (await harness.Service.DetectAndPersistAsync()).Value.InstanceId;

        secondInstance.Should().Be(firstInstance);

        await using AppDbContext verifyDb = harness.NewDbContext();
        (await verifyDb.DeploymentProfiles.CountAsync()).Should().Be(1);
    }

    [Fact]
    public void Current_throws_before_detection_completes()
    {
        using Harness harness = Harness.Create(new StubProbe(postgres: false, redis: false));

        Action read = () => _ = harness.Service.Current;

        read.Should().Throw<InvalidOperationException>();
    }

    // ─── Harness ────────────────────────────────────────────────────────────────

    private sealed class Harness : IDisposable, IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly ServiceProvider _provider;

        public DeploymentProfileService Service { get; }

        private Harness(
            SqliteConnection connection,
            ServiceProvider provider,
            DeploymentProfileService service
        )
        {
            _connection = connection;
            _provider = provider;
            Service = service;
        }

        public static Harness Create(
            IInfraReachabilityProbe probe,
            params (string Key, string Value)[] config
        ) => Create(probe, new RecordingEventBus(), config);

        public static Harness Create(
            IInfraReachabilityProbe probe,
            RecordingEventBus eventBus,
            params (string Key, string Value)[] config
        )
        {
            // One shared in-memory SQLite connection kept open for the harness lifetime, so every scoped
            // AppDbContext sees the same database (and the production model materializes on the real provider).
            SqliteConnection connection = new("DataSource=:memory:");
            connection.Open();

            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(
                    config.Select(c => new KeyValuePair<string, string?>(c.Key, c.Value))
                )
                .Build();

            ServiceCollection services = new();
            services.AddDbContext<AppDbContext>(options => options.UseSqlite(connection));
            ServiceProvider provider = services.BuildServiceProvider();

            // Create the schema once for the shared connection.
            using (IServiceScope scope = provider.CreateScope())
            {
                AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.Database.EnsureCreated();
            }

            DeploymentProfileService service = new(
                provider,
                configuration,
                probe,
                eventBus,
                NullLogger<DeploymentProfileService>.Instance
            );

            return new Harness(connection, provider, service);
        }

        public AppDbContext NewDbContext() =>
            new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);

        public void Dispose()
        {
            _provider.Dispose();
            _connection.Dispose();
        }

        public async ValueTask DisposeAsync()
        {
            await _provider.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class StubProbe : IInfraReachabilityProbe
    {
        private readonly bool _postgres;
        private readonly bool _redis;

        public StubProbe(bool postgres, bool redis)
        {
            _postgres = postgres;
            _redis = redis;
        }

        public Task<bool> IsPostgresReachableAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_postgres);

        public Task<bool> IsRedisReachableAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_redis);
    }
}
