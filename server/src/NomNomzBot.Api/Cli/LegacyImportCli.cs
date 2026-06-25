// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.EventStore;
using NomNomzBot.Infrastructure;
using NomNomzBot.Infrastructure.Analytics;
using NomNomzBot.Infrastructure.EventStore.LegacyImport;
using NomNomzBot.Infrastructure.Platform.Persistence;

namespace NomNomzBot.Api.Cli;

/// <summary>
/// Standalone, headless runner for the owner-gated legacy backfill — invoked by <c>--run-legacy-import &lt;channelId&gt;</c>
/// at the very top of <c>Program.cs</c>, BEFORE the web host is built. It exists because running the import through
/// the live HTTP API contends catastrophically for the single SQLite file: EventSub, the token-refresh loop, the
/// timers, and the projection driver all write the same <c>nomnomz.db</c> under WAL while the import tries to append
/// ~37k rows, which once stalled the import for an hour with no progress. This path eliminates that: it builds a
/// minimal generic <see cref="IHost"/> with the SAME Application + Infrastructure DI (so it opens the SAME live DB),
/// then STRIPS every <see cref="IHostedService"/> registration so Kestrel never binds and no background worker ever
/// starts — the import owns the database exclusively. It pins <c>Deployment:Mode=SelfHostLite</c> so the resolver
/// cannot drift onto Postgres when a stray docker tier happens to be up, guaranteeing it touches the same SQLite file
/// the self-host API serves. Progress is written to the console at every flush/replay batch so a stall is pinpointed
/// to a phase and a count; the legacy file is opened read-only and the live DB is never reset.
/// </summary>
public static class LegacyImportCli
{
    public const string Flag = "--run-legacy-import";

    /// <summary>True when <paramref name="args"/> request the standalone legacy import.</summary>
    public static bool Matches(string[] args) =>
        args.Length > 0 && string.Equals(args[0], Flag, StringComparison.Ordinal);

    /// <summary>
    /// Runs the import for the channel id in <paramref name="args"/> and returns the process exit code (0 = success).
    /// Phase 1 imports the legacy events onto the tenant journal; phase 2 rebuilds ONLY the channel-event-log
    /// projection and links each row to its internal User. The other analytics projections are a separate follow-up.
    /// </summary>
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length < 2 || !Guid.TryParse(args[1], out Guid broadcasterId))
        {
            Console.Error.WriteLine(
                $"usage: {Flag} <channelId-guid>   (e.g. {Flag} 019EF8E4-EAA1-736F-B992-7778DC68F241)"
            );
            return 2;
        }

        Log("legacy-import: standalone runner starting (no Kestrel, no hosted services).");
        Log($"legacy-import: target tenant {broadcasterId}.");

        HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

        // Pin SQLite (lite) so the resolver cannot pick Postgres if a stray docker tier is up — the self-host API
        // serves this exact nomnomz.db, and the import must write the SAME file, not an unrelated Postgres database.
        // Silence EF Core's per-statement command logging: at Information it writes the full INSERT for EVERY one of
        // ~37k appended rows, and that console/file I/O — not the SQLite write — dominates and progressively slows the
        // run. Capping EF at Warning keeps the import's own progress lines as the signal and lets the append run fast.
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Deployment:Mode"] = "SelfHostLite",
                ["Logging:LogLevel:Microsoft.EntityFrameworkCore"] = "Warning",
                ["Logging:LogLevel:Microsoft.EntityFrameworkCore.Database.Command"] = "Warning",
            }
        );

        builder.Services.AddApplication();
        builder.Services.AddInfrastructure(builder.Configuration);

        // Neutralize ALL background workers: remove every IHostedService descriptor so building/running the host
        // starts NOTHING (no EventSub, no token-refresh loop, no timers, no projection driver, no mDNS, no webhook
        // worker, no onboarding seed backfill). The concrete singletons stay registered so the DI graph that the
        // import services depend on still validates and resolves — they are simply never started.
        int removedWorkers = 0;
        for (int i = builder.Services.Count - 1; i >= 0; i--)
        {
            if (builder.Services[i].ServiceType == typeof(IHostedService))
            {
                builder.Services.RemoveAt(i);
                removedWorkers++;
            }
        }
        Log($"legacy-import: stripped {removedWorkers} hosted services — exclusive DB access.");

        using IHost host = builder.Build();

        await using AsyncServiceScope scope = host.Services.CreateAsyncScope();
        IServiceProvider sp = scope.ServiceProvider;

        // Confirm the runner is bound to the live self-host database before it writes a single row. The concrete
        // AppDbContext exposes the connection facade (IApplicationDbContext does not) — both are the same scoped
        // instance, so the source string is the file the import will actually write.
        IApplicationDbContext db = sp.GetRequiredService<IApplicationDbContext>();
        AppDbContext appDb = sp.GetRequiredService<AppDbContext>();
        Log($"legacy-import: live database = {appDb.Database.GetDbConnection().DataSource}");

        long journalBefore = await db.EventJournals.LongCountAsync();
        long channelEventsBefore = await db.ChannelEvents.LongCountAsync();
        Log(
            $"legacy-import: baseline — EventJournals={journalBefore}, ChannelEvents={channelEventsBefore}."
        );

        // ── Phase 1: import the legacy events onto the journal ───────────────────────────────────
        IEventJournal journal = sp.GetRequiredService<IEventJournal>();
        ILegacyDatabaseLocator legacyDb = sp.GetRequiredService<ILegacyDatabaseLocator>();

        Result<string> legacyPath = legacyDb.Resolve();
        if (legacyPath.IsFailure)
        {
            Console.Error.WriteLine(
                $"legacy-import: FAILED to locate legacy db — {legacyPath.ErrorMessage}"
            );
            return 1;
        }
        Log($"legacy-import: legacy source (read-only) = {legacyPath.Value}");

        Progress<long> readProgress = new(read => Log($"import-read: {read} rows scanned"));
        Progress<long> appendProgress = new(done => Log($"import: {done} appended"));

        LegacyChannelEventImporter importer = new(
            journal,
            new LegacyChannelEventMapper(),
            NullLogger.Instance
        );
        LegacySqliteChannelEventSource source = new(legacyPath.Value);

        Log("legacy-import: phase 1/2 — importing legacy events onto the journal…");
        Result<LegacyImportSummary> imported = await importer.ImportAsync(
            broadcasterId,
            source,
            CancellationToken.None,
            readProgress,
            appendProgress
        );
        if (imported.IsFailure)
        {
            Console.Error.WriteLine(
                $"legacy-import: phase 1 FAILED — {imported.ErrorMessage} ({imported.ErrorCode})"
            );
            return 1;
        }

        LegacyImportSummary summary = imported.Value;
        Log(
            $"legacy-import: phase 1 done — read={summary.TotalRead}, imported={summary.Imported}, "
                + $"duplicate={summary.SkippedDuplicate}, unmapped={summary.SkippedUnmapped}."
        );
        if (summary.SkippedByLegacyType.Count > 0)
            Log(
                "legacy-import: unmapped by type — "
                    + string.Join(
                        ", ",
                        summary
                            .SkippedByLegacyType.OrderByDescending(kv => kv.Value)
                            .Select(kv => $"{kv.Key}={kv.Value}")
                    )
            );

        // ── Phase 2: rebuild ONLY the channel-event-log projection, then link each row to its User ─
        IProjectionRunner runner = sp.GetRequiredService<IProjectionRunner>();
        Progress<long> projectProgress = new(applied => Log($"project: {applied} folded"));

        Log("legacy-import: phase 2/2 — rebuilding the channel-event-log projection…");
        Result<long> rebuilt = await runner.RebuildAsync(
            TwitchChannelEventLogProjectionName,
            broadcasterId,
            CancellationToken.None,
            projectProgress
        );
        if (rebuilt.IsFailure)
        {
            Console.Error.WriteLine(
                $"legacy-import: phase 2 (rebuild) FAILED — {rebuilt.ErrorMessage} ({rebuilt.ErrorCode})"
            );
            return 1;
        }
        Log($"legacy-import: projection rebuilt — {rebuilt.Value} events folded.");

        Log("legacy-import: phase 2/2 — linking channel events to viewer Users…");
        ChannelEventActorBackfill backfill = sp.GetRequiredService<ChannelEventActorBackfill>();
        Result<long> linked = await backfill.BackfillAsync(broadcasterId, CancellationToken.None);
        if (linked.IsFailure)
        {
            Console.Error.WriteLine(
                $"legacy-import: phase 2 (actor backfill) FAILED — {linked.ErrorMessage} ({linked.ErrorCode})"
            );
            return 1;
        }
        Log($"legacy-import: actor backfill linked {linked.Value} channel-event rows to Users.");

        long journalAfter = await db.EventJournals.LongCountAsync();
        long channelEventsAfter = await db.ChannelEvents.LongCountAsync();
        long withUser = await db.ChannelEvents.LongCountAsync(e => e.UserId != null);
        int distinctViewers = await db
            .ChannelEvents.Where(e => e.UserId != null)
            .Select(e => e.UserId)
            .Distinct()
            .CountAsync();

        Log("legacy-import: COMPLETE.");
        Log(
            $"legacy-import: final — EventJournals {journalBefore} -> {journalAfter}, "
                + $"ChannelEvents {channelEventsBefore} -> {channelEventsAfter}, "
                + $"with-User={withUser}, distinct-viewers={distinctViewers}."
        );
        return 0;
    }

    // The channel-event-log projection's stable name (TwitchChannelEventLogProjection.Name). For THIS pass only this
    // projection is rebuilt — the analytics dailies, viewer profiles, and watch sessions are a separate follow-up.
    private const string TwitchChannelEventLogProjectionName = "twitch.channel-event-log";

    private static void Log(string message) =>
        Console.WriteLine($"[{DateTimeOffset.Now:HH:mm:ss}] {message}");
}
