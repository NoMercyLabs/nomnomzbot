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
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.EventStore;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Domain.Music.ValueObjects;
using NomNomzBot.Domain.Platform.Entities;

namespace NomNomzBot.Infrastructure.Music.Replay;

/// <summary>
/// Reconstructs song-request history from the journal. See <see cref="ISongRequestHistoryBackfill"/> for why
/// this is explicit and silent.
///
/// <para>
/// Two event shapes carry a request. A channel-point redemption of the song-request reward puts the viewer's
/// input on <c>UserInput</c>; a chat command puts it in the message text. Both are read as JSON rather than
/// deserialized into the domain event, the same way <c>LegacyChannelEventMapper</c> reads legacy rows: the
/// journal holds years of payloads written against older shapes, and a required-member mismatch on one field
/// would throw away an entire channel's history rather than one row.
/// </para>
/// </summary>
public sealed partial class SongRequestHistoryBackfill : ISongRequestHistoryBackfill
{
    /// <summary>One page of journal reads. Large enough that a 50k-event channel is tens of round trips,
    /// small enough that a rebuild never holds the whole stream in memory.</summary>
    private const int PageSize = 500;

    /// <summary>
    /// A Spotify track id as it appears in either form a viewer pastes — <c>open.spotify.com/track/&lt;id&gt;</c>
    /// or <c>spotify:track:&lt;id&gt;</c>. Ids are exactly 22 base62 characters, which is what keeps this from
    /// matching an album or playlist link that happens to sit in the same message.
    /// </summary>
    [GeneratedRegex(@"track[/:]([A-Za-z0-9]{22})")]
    private static partial Regex TrackIdPattern();

    private static readonly JsonSerializerOptions HistoryJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly IApplicationDbContext _db;
    private readonly IEventJournal _journal;
    private readonly IEventPayloadProtector _payloads;
    private readonly ILogger<SongRequestHistoryBackfill> _logger;

    public SongRequestHistoryBackfill(
        IApplicationDbContext db,
        IEventJournal journal,
        IEventPayloadProtector payloads,
        ILogger<SongRequestHistoryBackfill> logger
    )
    {
        _db = db;
        _journal = journal;
        _payloads = payloads;
        _logger = logger;
    }

    public async Task<Result<SongRequestBackfillSummary>> RunAsync(
        Guid broadcasterId,
        SongRequestBackfillOptions options,
        CancellationToken cancellationToken = default
    )
    {
        if (options.RewardTitles.Count == 0 && options.CommandNames.Count == 0)
            return Result.Failure<SongRequestBackfillSummary>(
                "Nothing to look for: give at least one reward title or one command name.",
                "NO_SOURCES"
            );

        HashSet<string> rewardTitles = new(options.RewardTitles, StringComparer.OrdinalIgnoreCase);
        List<string> commands = [.. options.CommandNames.Select(c => c.Trim().ToLowerInvariant())];
        HashSet<string> alreadyImported = await LoadImportedSourceIdsAsync(
            broadcasterId,
            cancellationToken
        );

        int scanned = 0;
        int found = 0;
        int written = 0;
        int alreadyPresent = 0;
        int freeText = 0;
        long position = 0;

        while (true)
        {
            Result<IReadOnlyList<EventRecord>> page = await _journal.ReadStreamAsync(
                broadcasterId,
                position,
                PageSize,
                cancellationToken
            );
            if (page.IsFailure)
                return Result.Failure<SongRequestBackfillSummary>(
                    page.ErrorMessage ?? "Could not read the event journal.",
                    page.ErrorCode ?? "JOURNAL_READ_FAILED"
                );
            if (page.Value.Count == 0)
                break;

            foreach (EventRecord record in page.Value)
            {
                position = record.StreamPosition;
                scanned++;

                RequestedTrack? request = await ReadRequestAsync(
                    record,
                    rewardTitles,
                    commands,
                    cancellationToken
                );
                if (request is null)
                    continue;

                if (request.TrackUri is null)
                {
                    // The viewer typed a search phrase. The track it resolved to was the provider's answer and
                    // was never journaled, so no amount of replaying gets it back — counted, never guessed.
                    freeText++;
                    continue;
                }

                found++;

                if (!alreadyImported.Add(record.EventId.ToString()))
                {
                    alreadyPresent++;
                    continue;
                }

                if (options.DryRun)
                    continue;

                _db.Records.Add(
                    new Record
                    {
                        BroadcasterId = broadcasterId,
                        UserId = request.UserId,
                        RecordType = SongRequestHistory.RecordType,
                        Data = JsonSerializer.Serialize(
                            new SongRequestHistory(
                                request.TrackUri,
                                // Deliberately blank: naming the track is an outbound call, and a replay makes
                                // none. The metadata backfill fills these in as its own explicit step.
                                TrackName: string.Empty,
                                Artist: string.Empty,
                                ImageUrl: null,
                                Provider: "spotify",
                                SourceEventId: record.EventId.ToString()
                            ),
                            HistoryJson
                        ),
                    }
                );
                written++;
            }

            if (page.Value.Count < PageSize)
                break;
        }

        if (written > 0)
            await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Song-request backfill on {Tenant}: scanned {Scanned}, found {Found}, wrote {Written}, "
                + "already present {Present}, unresolvable free text {FreeText}{DryRun}",
            broadcasterId,
            scanned,
            found,
            written,
            alreadyPresent,
            freeText,
            options.DryRun ? " (dry run)" : string.Empty
        );

        return Result.Success(
            new SongRequestBackfillSummary(scanned, found, written, alreadyPresent, freeText)
        );
    }

    /// <summary>Source event ids this channel has already turned into rows — what makes a re-run a no-op.</summary>
    private async Task<HashSet<string>> LoadImportedSourceIdsAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken
    )
    {
        List<string> rows = await _db
            .Records.AsNoTracking()
            .Where(r =>
                r.BroadcasterId == broadcasterId && r.RecordType == SongRequestHistory.RecordType
            )
            .Select(r => r.Data)
            .ToListAsync(cancellationToken);

        HashSet<string> ids = new(StringComparer.Ordinal);
        foreach (string row in rows)
        {
            try
            {
                string? sourceId = JsonSerializer
                    .Deserialize<SongRequestHistory>(row, HistoryJson)
                    ?.SourceEventId;
                if (sourceId is not null)
                    ids.Add(sourceId);
            }
            catch (JsonException)
            {
                // An unreadable row cannot claim a source event; worst case its event is re-imported once.
            }
        }

        return ids;
    }

    /// <summary>The request carried by this event, or null when the event is not a song request at all.
    /// A non-null result with a null uri is a request whose track cannot be recovered.</summary>
    private async Task<RequestedTrack?> ReadRequestAsync(
        EventRecord record,
        HashSet<string> rewardTitles,
        List<string> commands,
        CancellationToken cancellationToken
    )
    {
        if (record.EventType is not ("RewardRedeemedEvent" or "ChatMessageReceivedEvent"))
            return null;

        JObject? payload = await ReadPayloadAsync(record, cancellationToken);
        if (payload is null)
            return null;

        return record.EventType == "RewardRedeemedEvent"
            ? ReadRedemption(payload, rewardTitles)
            : ReadChatCommand(payload, commands);
    }

    private static RequestedTrack? ReadRedemption(JObject payload, HashSet<string> rewardTitles)
    {
        string? title = payload["RewardTitle"]?.Value<string>();
        string? userId = payload["UserId"]?.Value<string>();
        if (title is null || userId is null || !rewardTitles.Contains(title))
            return null;

        return new(userId, TrackUriFrom(payload["UserInput"]?.Value<string>()));
    }

    private static RequestedTrack? ReadChatCommand(JObject payload, List<string> commands)
    {
        string? text = payload["Message"]?.Value<string>();
        string? userId = payload["UserId"]?.Value<string>();
        if (text is null || userId is null)
            return null;

        string trimmed = text.TrimStart();
        // The command must be the whole first word: `!srsomething` is a different command, and a message
        // merely mentioning "!sr" later on is someone talking about the command, not using it.
        int space = trimmed.IndexOf(' ');
        string firstWord = (space < 0 ? trimmed : trimmed[..space]).ToLowerInvariant();
        if (!commands.Contains(firstWord))
            return null;

        return new(userId, TrackUriFrom(trimmed));
    }

    private static string? TrackUriFrom(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        Match match = TrackIdPattern().Match(input);
        return match.Success ? $"spotify:track:{match.Groups[1].Value}" : null;
    }

    private async Task<JObject?> ReadPayloadAsync(
        EventRecord record,
        CancellationToken cancellationToken
    )
    {
        // Goes through the protector even for a plaintext row: a PII-bearing payload is sealed under its
        // subject's key, and the protector is the only thing that knows which rows those are.
        Result<string> json = await _payloads.UnprotectAsync(record, cancellationToken);
        if (json.IsFailure)
        {
            _logger.LogWarning(
                "Skipping journal payload {EventId} during song-request backfill: {Error}",
                record.EventId,
                json.ErrorMessage
            );
            return null;
        }

        try
        {
            return JObject.Parse(json.Value);
        }
        catch (Newtonsoft.Json.JsonException ex)
        {
            // One unreadable payload must not abandon the rest of the channel's history.
            _logger.LogWarning(
                ex,
                "Skipping unreadable journal payload {EventId} during song-request backfill",
                record.EventId
            );
            return null;
        }
    }

    private sealed record RequestedTrack(string UserId, string? TrackUri);
}
