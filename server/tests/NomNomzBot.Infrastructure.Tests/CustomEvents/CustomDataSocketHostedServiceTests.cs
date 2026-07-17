// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Runtime.CompilerServices;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Caching;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.CustomEvents.Services;
using NomNomzBot.Domain.CustomEvents.Entities;
using NomNomzBot.Domain.CustomEvents.Events;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.CustomEvents;
using NomNomzBot.Infrastructure.CustomEvents.Sockets;
using NomNomzBot.Infrastructure.Platform.Deployment;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.CustomEvents;

/// <summary>
/// Proves the custom-data socket ingress runner (custom-events.md D2) by its consequences against the REAL
/// ingest path: a reconcile opens one connection for an enabled socket source, applies the sealed secret as the
/// preset's query-param token, and each inbound text frame flows through <see cref="CustomDataIngestService"/> —
/// extracting the mapped <c>bpm</c> field, publishing exactly one <see cref="CustomDataReceivedEvent"/>, and
/// stamping <c>LastReceivedAt</c>. A second test proves the reconcile connects ONLY enabled socket sources —
/// a disabled socket and an enabled <c>poll</c> source open nothing.
/// </summary>
public sealed class CustomDataSocketHostedServiceTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-000000000f01");

    // A Pulsoid-shaped heart-rate frame: the field-map lifts $.data.heart_rate → bpm.
    private const string HeartRateFrame = """
        { "measured_at": 1710000000000, "data": { "heart_rate": 128 } }
        """;

    private static async Task<(
        CustomDataSocketHostedService Service,
        AuthDbContext Db,
        RecordingEventBus Bus,
        QueueFrameSource Frames
    )> BuildAsync()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();

        RecordingEventBus bus = new();
        CustomDataIngestService ingest = new(db, bus, Substitute.For<ICacheService>());

        ServiceCollection services = new();
        services.AddSingleton<IRunOnceGuard>(new NoOpRunOnceGuard());
        services.AddSingleton<IApplicationDbContext>(db);
        services.AddSingleton<ITokenProtector>(new PrefixProtector());
        services.AddSingleton<ICustomDataIngestService>(ingest);
        ServiceProvider provider = services.BuildServiceProvider();

        QueueFrameSource frames = new();
        CustomDataSocketHostedService service = new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            frames,
            TimeProvider.System,
            NullLogger<CustomDataSocketHostedService>.Instance
        );
        return (service, db, bus, frames);
    }

    private static CustomDataSource Source(
        string name = "heartrate",
        string sourceKind = "socket",
        bool enabled = true,
        string endpointUrl = "wss://dev.pulsoid.net/api/v1/data/real_time?access_token=",
        string? secret = "tok",
        string fieldMap = "{\"bpm\":\"$.data.heart_rate\"}"
    ) =>
        new()
        {
            BroadcasterId = Channel,
            Name = name,
            DisplayName = name,
            SourceKind = sourceKind,
            EndpointUrl = endpointUrl,
            AuthSecretCipher = secret is null ? null : $"sealed:{secret}",
            FieldMapJson = fieldMap,
            IsEnabled = enabled,
        };

    [Fact]
    public async Task Reconcile_StartsASocketRunner_WhoseFrameExtractsBpm_PublishesTheEvent_AndStampsLastReceived()
    {
        (
            CustomDataSocketHostedService service,
            AuthDbContext db,
            RecordingEventBus bus,
            QueueFrameSource frames
        ) = await BuildAsync();
        db.CustomDataSources.Add(Source());
        await db.SaveChangesAsync();
        frames.Enqueue(HeartRateFrame);

        await service.ReconcileOnceAsync(CancellationToken.None);
        // Every queued frame consumed (and its ingest awaited) by the runner.
        await frames.Drained.WaitAsync(TimeSpan.FromSeconds(10));

        // The runner handed the transport the endpoint with the UNSEALED token appended as the query param.
        frames.ConnectedEndpoints.Should().HaveCount(1);
        frames
            .ConnectedEndpoints[0]
            .Should()
            .Be(new Uri("wss://dev.pulsoid.net/api/v1/data/real_time?access_token=tok"));

        // Consequence 1: exactly one CustomDataReceivedEvent, carrying the mapped field.
        CustomDataReceivedEvent published = bus
            .Published.OfType<CustomDataReceivedEvent>()
            .Should()
            .ContainSingle()
            .Subject;
        published.SourceName.Should().Be("heartrate");
        published.Fields.Should().ContainKey("bpm").WhoseValue.Should().Be("128");
        published.RawPayload.Should().Contain("heart_rate");

        // Consequence 2: the source's LastReceivedAt is stamped by the ingest.
        CustomDataSource persisted = await db.CustomDataSources.SingleAsync();
        persisted.LastReceivedAt.Should().NotBeNull();

        await service.DisposeAsync();
    }

    [Fact]
    public async Task Reconcile_ConnectsOnlyEnabledSocketSources_IgnoringDisabledAndPollSources()
    {
        (CustomDataSocketHostedService service, AuthDbContext db, _, QueueFrameSource frames) =
            await BuildAsync();
        db.CustomDataSources.Add(Source(name: "heartrate")); // enabled socket → connects
        db.CustomDataSources.Add(Source(name: "disabled", enabled: false)); // disabled socket → ignored
        db.CustomDataSources.Add(
            Source(
                name: "ticker",
                sourceKind: "poll",
                endpointUrl: "https://api.example.com/ticker"
            )
        ); // enabled poll → ignored (owned by the poll worker)
        await db.SaveChangesAsync();

        await service.ReconcileOnceAsync(CancellationToken.None);
        await frames.FirstConnected.WaitAsync(TimeSpan.FromSeconds(10)); // runner startup is async

        frames.ConnectedEndpoints.Should().HaveCount(1);
        frames.ConnectedEndpoints[0].Host.Should().Be("dev.pulsoid.net");

        await service.DisposeAsync();
    }

    // ─── scaffolding ───────────────────────────────────────────────────────────

    /// <summary>
    /// A scripted transport: yields the queued frames, then waits (an idle open socket) until cancelled.
    /// <see cref="Drained"/> completes when every queued frame has been consumed, giving tests a race-free
    /// point to assert the ingested consequences.
    /// </summary>
    private sealed class QueueFrameSource : ICustomDataSocketFrameSource
    {
        private readonly Queue<string> _frames = new();
        private readonly TaskCompletionSource _drained = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private readonly TaskCompletionSource _firstConnected = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        public List<Uri> ConnectedEndpoints { get; } = [];
        public Task Drained => _drained.Task;
        public Task FirstConnected => _firstConnected.Task;

        public void Enqueue(string frame) => _frames.Enqueue(frame);

        public async IAsyncEnumerable<string> ConnectAndReceiveAsync(
            Uri endpoint,
            [EnumeratorCancellation] CancellationToken ct
        )
        {
            ConnectedEndpoints.Add(endpoint);
            _firstConnected.TrySetResult();
            while (_frames.Count > 0)
            {
                string frame = _frames.Dequeue();
                yield return frame;
            }
            _drained.TrySetResult();
            // An open, idle socket: stay connected until the runner is cancelled.
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
        }
    }

    /// <summary>Transparent AEAD stand-in (<c>sealed:&lt;plaintext&gt;</c>) — the crypto is proven elsewhere.</summary>
    private sealed class PrefixProtector : ITokenProtector
    {
        public Task<string> ProtectAsync(
            string plaintext,
            TokenProtectionContext context,
            CancellationToken cancellationToken = default
        ) => Task.FromResult($"sealed:{plaintext}");

        public Task<string?> TryUnprotectAsync(
            string? sealedEnvelope,
            TokenProtectionContext context,
            CancellationToken cancellationToken = default
        ) =>
            Task.FromResult(
                sealedEnvelope is not null && sealedEnvelope.StartsWith("sealed:")
                    ? sealedEnvelope["sealed:".Length..]
                    : null
            );
    }
}
