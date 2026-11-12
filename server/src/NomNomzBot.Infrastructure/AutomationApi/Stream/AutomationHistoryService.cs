// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.AutomationApi.Dtos;
using NomNomzBot.Application.AutomationApi.Services;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Domain.EventStore.Entities;
using NomNomzBot.Domain.Platform;
using NomNomzBot.Infrastructure.EventStore;

namespace NomNomzBot.Infrastructure.AutomationApi.Stream;

public sealed class AutomationHistoryService(
    IApplicationDbContext db,
    IAutomationEventRegistry events,
    JournaledDomainEventReader reader,
    IRateLimiterPartitionStore rateLimiter,
    DomainEventTypeRegistry types
) : IAutomationHistoryService
{
    private const int DefaultPageSize = 500;
    private const int MaximumPageSize = 1000;
    private const int PagesPerMinute = 60;
    private static readonly TimeSpan RateWindow = TimeSpan.FromMinutes(1);

    public async Task<Result<AutomationHistoryPage>> ReadAsync(
        AutomationPrincipal principal,
        DateTimeOffset from,
        DateTimeOffset to,
        string? cursor,
        int pageSize = DefaultPageSize,
        CancellationToken ct = default
    )
    {
        if (!principal.Scopes.Contains("events"))
            return Failure("This token lacks the 'events' scope.", "FORBIDDEN");
        if (
            from.Offset != TimeSpan.Zero
            || to.Offset != TimeSpan.Zero
            || to <= from
            || to - from > TimeSpan.FromHours(24)
        )
            return Failure(
                "from and to must be UTC instants no more than 24 hours apart.",
                "VALIDATION_FAILED"
            );
        if (pageSize < 0)
            return Failure("pageSize must be positive.", "VALIDATION_FAILED");
        int limit = pageSize == 0 ? DefaultPageSize : Math.Min(pageSize, MaximumPageSize);

        HistoryCursor? after = null;
        if (!string.IsNullOrEmpty(cursor))
        {
            after = DecodeCursor(cursor);
            if (
                after is null
                || after.Channel != principal.BroadcasterId
                || after.FromTicks != from.UtcTicks
                || after.ToTicks != to.UtcTicks
                || after.AtTicks < from.UtcTicks
                || after.AtTicks >= to.UtcTicks
                || after.Id <= 0
            )
                return Failure(
                    "Invalid cursor for this channel and time range.",
                    "VALIDATION_FAILED"
                );
        }

        RateLimitLease lease = await rateLimiter.AcquireAsync(
            $"automation:{principal.TokenId}:history",
            PagesPerMinute,
            RateWindow,
            ct
        );
        if (!lease.IsAcquired)
        {
            int seconds = Math.Max(1, (int)Math.Ceiling(lease.RetryAfter.TotalSeconds));
            return Result.Failure<AutomationHistoryPage>(
                $"History rate limit exceeded — retry in {seconds}s.",
                "RATE_LIMITED",
                seconds.ToString()
            );
        }

        List<AutomationEventFrame> items = [];
        HistoryCursor? lastReturned = null;
        DateTime afterAt = after is null
            ? from.UtcDateTime
            : new DateTime(after.AtTicks, DateTimeKind.Utc);
        long afterId = after?.Id ?? 0;

        while (true)
        {
            List<EventJournal> rows = await db
                .EventJournals.AsNoTracking()
                .Where(row =>
                    row.BroadcasterId == principal.BroadcasterId
                    && row.OccurredAt >= from.UtcDateTime
                    && row.OccurredAt < to.UtcDateTime
                    && (row.OccurredAt > afterAt || row.OccurredAt == afterAt && row.Id > afterId)
                )
                .OrderBy(row => row.OccurredAt)
                .ThenBy(row => row.Id)
                .Take(Math.Max(limit + 1, 500))
                .ToListAsync(ct);
            if (rows.Count == 0)
                break;

            List<(
                EventJournal Row,
                IAutomationEventDescriptor Descriptor,
                DomainEventBase Event
            )> candidates = [];
            foreach (EventJournal row in rows)
            {
                Type? type = types.Resolve(row.EventType);
                if (
                    type is null
                    || !events.TryGet(type, out IAutomationEventDescriptor? descriptor)
                )
                    continue;
                Result<IDomainEvent> read = await reader.ReadAsync(row, ct);
                if (read.IsFailure)
                {
                    if (read.ErrorCode is "KEY_DESTROYED" or "PAYLOAD_INVALID")
                        continue;
                    return Result.Failure<AutomationHistoryPage>(
                        read.ErrorMessage!,
                        read.ErrorCode,
                        read.ErrorDetail
                    );
                }
                if (read.Value is DomainEventBase domainEvent)
                    candidates.Add((row, descriptor, domainEvent));
            }

            List<string> chatIds =
            [
                .. candidates
                    .Where(candidate => candidate.Event is ChatMessageReceivedEvent)
                    .Select(candidate => ((ChatMessageReceivedEvent)candidate.Event).MessageId)
                    .Distinct(),
            ];
            HashSet<string> visibleChatIds =
                chatIds.Count == 0
                    ? []
                    :
                    [
                        .. await db
                            .ChatMessages.IgnoreQueryFilters()
                            .AsNoTracking()
                            .Where(message =>
                                message.BroadcasterId == principal.BroadcasterId
                                && message.DeletedAt == null
                                && chatIds.Contains(message.Id)
                            )
                            .Select(message => message.Id)
                            .ToListAsync(ct),
                    ];

            foreach (
                (
                    EventJournal row,
                    IAutomationEventDescriptor descriptor,
                    DomainEventBase domainEvent
                ) in candidates
            )
            {
                if (
                    domainEvent is ChatMessageReceivedEvent chat
                    && !visibleChatIds.Contains(chat.MessageId)
                )
                    continue;
                if (items.Count == limit)
                    return Result.Success(
                        new AutomationHistoryPage(items, EncodeCursor(lastReturned!))
                    );
                items.Add(
                    new AutomationEventFrame(
                        "event",
                        descriptor.PublicName,
                        principal.BroadcasterId.ToString("D"),
                        new DateTimeOffset(DateTime.SpecifyKind(row.OccurredAt, DateTimeKind.Utc)),
                        descriptor.ProjectPayload(domainEvent)
                    )
                );
                lastReturned = new(
                    principal.BroadcasterId,
                    from.UtcTicks,
                    to.UtcTicks,
                    row.OccurredAt.Ticks,
                    row.Id
                );
            }

            EventJournal lastScanned = rows[^1];
            afterAt = lastScanned.OccurredAt;
            afterId = lastScanned.Id;
            if (rows.Count < Math.Max(limit + 1, 500))
                break;
        }
        return Result.Success(new AutomationHistoryPage(items, null));
    }

    private static Result<AutomationHistoryPage> Failure(string message, string code) =>
        Result.Failure<AutomationHistoryPage>(message, code);

    private static string EncodeCursor(HistoryCursor cursor) =>
        Convert
            .ToBase64String(JsonSerializer.SerializeToUtf8Bytes(cursor))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static HistoryCursor? DecodeCursor(string value)
    {
        if (value.Length is 0 or > 512)
            return null;
        try
        {
            string encoded = value.Replace('-', '+').Replace('_', '/');
            byte[] bytes = Convert.FromBase64String(
                encoded.PadRight((encoded.Length + 3) / 4 * 4, '=')
            );
            return JsonSerializer.Deserialize<HistoryCursor>(bytes);
        }
        catch (FormatException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record HistoryCursor(
        Guid Channel,
        long FromTicks,
        long ToTicks,
        long AtTicks,
        long Id
    );
}
