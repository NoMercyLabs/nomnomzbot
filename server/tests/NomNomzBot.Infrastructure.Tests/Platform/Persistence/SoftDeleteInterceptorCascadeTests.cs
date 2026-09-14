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
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Infrastructure.Commands.Persistence;
using NomNomzBot.Infrastructure.Identity.Persistence;
using NomNomzBot.Infrastructure.Platform.Persistence.Interceptors;
using DomainPipeline = NomNomzBot.Domain.Commands.Entities.Pipeline;
using Timer = NomNomzBot.Domain.Commands.Entities.Timer;

namespace NomNomzBot.Infrastructure.Tests.Platform.Persistence;

/// <summary>
/// Proves <see cref="SoftDeleteInterceptor"/>'s generic FK cascade: soft-deleting a <c>Pipeline</c>
/// must null <see cref="Command.PipelineId"/>/<see cref="EventResponse.PipelineId"/>/
/// <see cref="Timer.PipelineId"/> on every row that referenced it — mirroring the real
/// <c>.OnDelete(DeleteBehavior.SetNull)</c> each configuration declares, which the database itself never
/// runs because a soft delete never becomes an actual DELETE statement. Regression coverage for the live
/// incident (2026-09-14): qtkitte's <c>!spank</c> command kept pointing at a soft-deleted pipeline and
/// silently no-opped on every trigger, because nothing cleared the dangling reference.
/// </summary>
public sealed class SoftDeleteInterceptorCascadeTests : IDisposable
{
    private static readonly Guid OwnerUserId = Guid.Parse("0192a000-0000-7000-8000-0000000000a1");
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public SoftDeleteInterceptorCascadeTests()
    {
        _connection.Open();
        using CascadeTestDbContext schema = new(
            new DbContextOptionsBuilder<CascadeTestDbContext>().UseSqlite(_connection).Options
        );
        schema.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    /// <summary>Minimal plain <see cref="DbContext"/> — the interceptor only needs
    /// <see cref="DbContext.ChangeTracker"/>/<see cref="DbContext.Model"/>/<see cref="DbContext.Set{TEntity}"/>,
    /// not the full <c>IApplicationDbContext</c> surface — mapping the real configurations for exactly the
    /// entities this cascade touches, on a real relational SQLite connection.</summary>
    private sealed class CascadeTestDbContext(DbContextOptions<CascadeTestDbContext> options)
        : DbContext(options)
    {
        public DbSet<Channel> Channels => Set<Channel>();
        public DbSet<DomainPipeline> Pipelines => Set<DomainPipeline>();
        public DbSet<Command> Commands => Set<Command>();
        public DbSet<EventResponse> EventResponses => Set<EventResponse>();
        public DbSet<Timer> Timers => Set<Timer>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new ChannelConfiguration());
            modelBuilder.ApplyConfiguration(new PipelineConfiguration());
            modelBuilder.ApplyConfiguration(new CommandConfiguration());
            modelBuilder.ApplyConfiguration(new EventResponseConfiguration());
            modelBuilder.ApplyConfiguration(new TimerConfiguration());

            // MetadataJson/Messages are Dictionary<string,string>/List<string> mapped to a real jsonb
            // column in production — SQLite (this harness's provider) cannot materialize a jsonb-of-
            // complex-type column, same limitation noted by SeedTestDbContext. Neither column is
            // exercised by this cascade test, so drop them rather than add a value converter.
            modelBuilder.Entity<EventResponse>().Ignore(e => e.MetadataJson);
            modelBuilder.Entity<Timer>().Ignore(e => e.Messages);

            // Channel carries navs into entities this minimal harness never maps (Moderators, Streams,
            // Events, PlatformConnections, User) — drop them so EF doesn't auto-discover those types too.
            modelBuilder.Entity<Channel>(b =>
            {
                b.Ignore(c => c.User);
                b.Ignore(c => c.Moderators);
                b.Ignore(c => c.Streams);
                b.Ignore(c => c.Events);
                b.Ignore(c => c.PlatformConnections);
            });
            // Pipeline.Steps/Triggers are unconfigured by PipelineConfiguration (owned by their own
            // configs, not needed by this cascade test) — drop them for the same reason.
            modelBuilder.Entity<DomainPipeline>(b =>
            {
                b.Ignore(p => p.Steps);
                b.Ignore(p => p.Triggers);
            });
            // Belt-and-suspenders: ChannelModerator keeps surfacing via convention discovery even with
            // the navs above ignored — drop the type outright rather than chase the exact reachability
            // path further; it plays no part in the FK cascade under test.
            modelBuilder.Ignore<ChannelModerator>();

            // Postgres-only default-value SQL (the `::jsonb` cast) is not a no-op on SQLite like
            // HasColumnType — it lands verbatim in the CREATE TABLE statement and SQLite's parser
            // rejects the cast syntax. Clear it; the columns themselves (List<string>) still map fine.
            modelBuilder.Entity<Command>().Property(c => c.Aliases).HasDefaultValueSql(null);
            modelBuilder
                .Entity<Command>()
                .Property(c => c.TemplateResponses)
                .HasDefaultValueSql(null);
            modelBuilder.Entity<Channel>().Property(c => c.Tags).HasDefaultValueSql(null);
            modelBuilder.Entity<Channel>().Property(c => c.ContentLabels).HasDefaultValueSql(null);
        }
    }

    private CascadeTestDbContext NewContext(SoftDeleteInterceptor interceptor)
    {
        DbContextOptions<CascadeTestDbContext> options =
            new DbContextOptionsBuilder<CascadeTestDbContext>()
                .UseSqlite(_connection)
                .AddInterceptors(interceptor)
                .Options;
        return new CascadeTestDbContext(options);
    }

    private static Channel NewChannel() =>
        new()
        {
            Id = Guid.CreateVersion7(),
            OwnerUserId = OwnerUserId,
            Provider = "twitch",
            ExternalChannelId = Guid.NewGuid().ToString("N"),
            Name = "qtkitte",
            NameNormalized = "qtkitte",
            Status = "active",
            DeploymentMode = "self_host_full",
            BillingTierKey = "base",
            OverlayToken = Guid.NewGuid().ToString("N"),
            IsOnboarded = true,
            IsLive = false,
            StreamDelay = 0,
            IsBrandedContent = false,
        };

    [Fact]
    public async Task Soft_deleting_a_pipeline_clears_PipelineId_on_every_command_event_response_and_timer_that_referenced_it()
    {
        FakeTimeProvider time = new(DateTimeOffset.Parse("2026-09-14T20:00:00Z"));
        SoftDeleteInterceptor interceptor = new(time, StubCurrentUserService.For(Guid.NewGuid()));

        Guid channelId;
        Guid pipelineId;
        Guid commandId;
        Guid eventResponseId;
        Guid timerId;

        await using (CascadeTestDbContext db = NewContext(interceptor))
        {
            Channel channel = NewChannel();
            db.Channels.Add(channel);
            channelId = channel.Id;

            DomainPipeline pipeline = new()
            {
                Id = Guid.CreateVersion7(),
                BroadcasterId = channelId,
                Name = "Spank",
            };
            db.Pipelines.Add(pipeline);
            pipelineId = pipeline.Id;
            await db.SaveChangesAsync();

            Command command = new()
            {
                Id = Guid.CreateVersion7(),
                BroadcasterId = channelId,
                Name = "!spank",
                NameNormalized = "spank",
                PipelineId = pipelineId,
            };
            db.Commands.Add(command);
            commandId = command.Id;

            EventResponse eventResponse = new()
            {
                Id = Guid.CreateVersion7(),
                BroadcasterId = channelId,
                EventType = "channel.raid",
                ResponseType = "pipeline",
                PipelineId = pipelineId,
            };
            db.EventResponses.Add(eventResponse);
            eventResponseId = eventResponse.Id;

            Timer timer = new()
            {
                Id = Guid.CreateVersion7(),
                BroadcasterId = channelId,
                Name = "spank-reminder",
                PipelineId = pipelineId,
            };
            db.Timers.Add(timer);
            timerId = timer.Id;

            await db.SaveChangesAsync();
        }

        await using (CascadeTestDbContext db = NewContext(interceptor))
        {
            DomainPipeline pipeline = await db.Pipelines.SingleAsync(p => p.Id == pipelineId);
            db.Pipelines.Remove(pipeline); // SoftDeleteInterceptor converts this to a soft delete
            await db.SaveChangesAsync();
        }

        await using (CascadeTestDbContext verify = NewContext(interceptor))
        {
            DomainPipeline deletedPipeline = await verify.Pipelines.SingleAsync(p =>
                p.Id == pipelineId
            );
            deletedPipeline.DeletedAt.Should().NotBeNull();

            Command command = await verify.Commands.SingleAsync(c => c.Id == commandId);
            command.PipelineId.Should().BeNull();

            EventResponse eventResponse = await verify.EventResponses.SingleAsync(r =>
                r.Id == eventResponseId
            );
            eventResponse.PipelineId.Should().BeNull();

            Timer timer = await verify.Timers.SingleAsync(t => t.Id == timerId);
            timer.PipelineId.Should().BeNull();
        }
    }

    [Fact]
    public async Task Soft_deleting_a_pipeline_never_touches_references_to_a_DIFFERENT_pipeline()
    {
        FakeTimeProvider time = new(DateTimeOffset.Parse("2026-09-14T20:00:00Z"));
        SoftDeleteInterceptor interceptor = new(time, StubCurrentUserService.For(Guid.NewGuid()));

        Guid channelId;
        Guid deletedPipelineId;
        Guid survivingPipelineId;
        Guid survivingCommandId;

        await using (CascadeTestDbContext db = NewContext(interceptor))
        {
            Channel channel = NewChannel();
            db.Channels.Add(channel);
            channelId = channel.Id;

            DomainPipeline deleted = new()
            {
                Id = Guid.CreateVersion7(),
                BroadcasterId = channelId,
                Name = "to-delete",
            };
            DomainPipeline surviving = new()
            {
                Id = Guid.CreateVersion7(),
                BroadcasterId = channelId,
                Name = "keep-me",
            };
            db.Pipelines.AddRange(deleted, surviving);
            deletedPipelineId = deleted.Id;
            survivingPipelineId = surviving.Id;
            await db.SaveChangesAsync();

            Command command = new()
            {
                Id = Guid.CreateVersion7(),
                BroadcasterId = channelId,
                Name = "!keep",
                NameNormalized = "keep",
                PipelineId = survivingPipelineId,
            };
            db.Commands.Add(command);
            survivingCommandId = command.Id;
            await db.SaveChangesAsync();
        }

        await using (CascadeTestDbContext db = NewContext(interceptor))
        {
            DomainPipeline pipeline = await db.Pipelines.SingleAsync(p =>
                p.Id == deletedPipelineId
            );
            db.Pipelines.Remove(pipeline);
            await db.SaveChangesAsync();
        }

        await using (CascadeTestDbContext verify = NewContext(interceptor))
        {
            Command survivor = await verify.Commands.SingleAsync(c => c.Id == survivingCommandId);
            survivor.PipelineId.Should().Be(survivingPipelineId);
        }
    }
}
