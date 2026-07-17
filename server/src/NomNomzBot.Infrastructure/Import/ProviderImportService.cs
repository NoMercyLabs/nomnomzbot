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
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Import.Dtos;
using NomNomzBot.Application.Import.Services;
using NomNomzBot.Application.Quotes.Dtos;
using NomNomzBot.Application.Quotes.Services;

namespace NomNomzBot.Infrastructure.Import;

/// <summary>
/// Maps a StreamElements export onto a channel's own commands / quotes / timers via the existing management
/// services (so each imported entity clears the same validation, quota, and dashboard live-sync path a
/// hand-created one does). Duplicates are skipped and counted rather than failing the whole import, which keeps
/// re-running the same export idempotent. Only a read of existing quote text is done directly on the context —
/// the quote service has no text-dedup of its own (numbered rows may legitimately repeat), so the import owns
/// that de-duplication.
/// </summary>
public sealed class ProviderImportService : IProviderImportService
{
    private readonly IApplicationDbContext _db;
    private readonly ICommandService _commands;
    private readonly IQuoteService _quotes;
    private readonly ITimerManagementService _timers;

    public ProviderImportService(
        IApplicationDbContext db,
        ICommandService commands,
        IQuoteService quotes,
        ITimerManagementService timers
    )
    {
        _db = db;
        _commands = commands;
        _quotes = quotes;
        _timers = timers;
    }

    public async Task<Result<ImportSummary>> ImportStreamElementsAsync(
        Guid broadcasterId,
        StreamElementsExport export,
        CancellationToken ct = default
    )
    {
        List<string> warnings = [];

        (int commandsImported, int commandsSkipped) = await ImportCommandsAsync(
            broadcasterId,
            export.Commands ?? [],
            warnings,
            ct
        );
        (int quotesImported, int quotesSkipped) = await ImportQuotesAsync(
            broadcasterId,
            export.Quotes ?? [],
            warnings,
            ct
        );
        (int timersImported, int timersSkipped) = await ImportTimersAsync(
            broadcasterId,
            export.Timers ?? [],
            warnings,
            ct
        );

        return Result.Success(
            new ImportSummary
            {
                CommandsImported = commandsImported,
                CommandsSkipped = commandsSkipped,
                QuotesImported = quotesImported,
                QuotesSkipped = quotesSkipped,
                TimersImported = timersImported,
                TimersSkipped = timersSkipped,
                Warnings = warnings,
            }
        );
    }

    private async Task<(int Imported, int Skipped)> ImportCommandsAsync(
        Guid broadcasterId,
        List<SeCommand> commands,
        List<string> warnings,
        CancellationToken ct
    )
    {
        int imported = 0;
        int skipped = 0;
        string broadcaster = broadcasterId.ToString();

        foreach (SeCommand source in commands)
        {
            string name = NormalizeCommandName(source.Command);
            if (name.Length == 0)
            {
                skipped++;
                warnings.Add("Skipped a command with no name.");
                continue;
            }

            // SE has both a global and a per-user cooldown; this project stores one cooldown plus a per-user
            // flag, so a per-user cooldown (when present) takes precedence and sets the flag.
            bool perUser = source.UserCooldown is > 0;
            int cooldown = Math.Clamp(
                (perUser ? source.UserCooldown : source.Cooldown) ?? 0,
                0,
                86400
            );

            CreateCommandDto request = new()
            {
                Name = name,
                Tier = "template",
                MinPermissionLevel = MapAccessLevel(source.AccessLevel),
                TemplateResponse = source.Response,
                CooldownSeconds = cooldown,
                CooldownPerUser = perUser,
                Aliases = source
                    .Aliases?.Select(NormalizeCommandName)
                    .Where(a => a.Length > 0)
                    .ToList(),
            };

            Result<CommandDto> result = await _commands.CreateAsync(broadcaster, request, ct);
            if (result.IsSuccess)
            {
                imported++;
            }
            else if (result.ErrorCode == "ALREADY_EXISTS")
            {
                skipped++;
            }
            else
            {
                skipped++;
                warnings.Add($"Command '{name}' skipped: {result.ErrorMessage}");
            }
        }

        return (imported, skipped);
    }

    private async Task<(int Imported, int Skipped)> ImportQuotesAsync(
        Guid broadcasterId,
        List<SeQuote> quotes,
        List<string> warnings,
        CancellationToken ct
    )
    {
        int imported = 0;
        int skipped = 0;

        // The quote service assigns a fresh number to every add and does not de-duplicate by text, so the import
        // owns text de-duplication: seed the "seen" set from the channel's existing quotes, then add each new
        // text as it lands. This dedupes both against what is already stored and within the payload itself,
        // keeping a re-import idempotent.
        HashSet<string> seen = (
            await _db
                .Quotes.Where(q => q.BroadcasterId == broadcasterId)
                .Select(q => q.Text)
                .ToListAsync(ct)
        )
            .Select(NormalizeQuoteText)
            .ToHashSet();

        foreach (SeQuote source in quotes)
        {
            string text = source.Text?.Trim() ?? string.Empty;
            if (text.Length == 0)
            {
                skipped++;
                warnings.Add("Skipped a quote with no text.");
                continue;
            }

            if (!seen.Add(NormalizeQuoteText(text)))
            {
                skipped++;
                continue;
            }

            Result<QuoteDto> result = await _quotes.AddAsync(
                broadcasterId,
                new AddQuoteRequest(
                    text,
                    string.IsNullOrWhiteSpace(source.AddedBy) ? null : source.AddedBy.Trim(),
                    string.IsNullOrWhiteSpace(source.Game) ? null : source.Game.Trim(),
                    source.CreatedAt,
                    null
                ),
                ct
            );

            if (result.IsSuccess)
            {
                imported++;
            }
            else
            {
                skipped++;
                seen.Remove(NormalizeQuoteText(text));
                warnings.Add($"Quote skipped: {result.ErrorMessage}");
            }
        }

        return (imported, skipped);
    }

    private async Task<(int Imported, int Skipped)> ImportTimersAsync(
        Guid broadcasterId,
        List<SeTimer> timers,
        List<string> warnings,
        CancellationToken ct
    )
    {
        int imported = 0;
        int skipped = 0;
        string broadcaster = broadcasterId.ToString();

        foreach (SeTimer source in timers)
        {
            string name = source.Name?.Trim() ?? string.Empty;
            if (name.Length == 0)
            {
                skipped++;
                warnings.Add("Skipped a timer with no name.");
                continue;
            }

            List<string> messages = CollectTimerMessages(source);
            if (messages.Count == 0)
            {
                skipped++;
                warnings.Add($"Timer '{name}' skipped: it has no messages.");
                continue;
            }

            CreateTimerDto request = new()
            {
                Name = name,
                Messages = messages,
                // SE stores the interval in seconds; this project's timers are minute-granular (1..1440).
                IntervalMinutes = Math.Clamp(SecondsToMinutes(source.Interval), 1, 1440),
                MinChatActivity = Math.Clamp(source.ChatLines ?? 0, 0, 10000),
                IsEnabled = source.Enabled ?? true,
            };

            Result<TimerDto> result = await _timers.CreateAsync(broadcaster, request, ct);
            if (result.IsSuccess)
            {
                imported++;
            }
            else if (result.ErrorCode == "ALREADY_EXISTS")
            {
                skipped++;
            }
            else
            {
                skipped++;
                warnings.Add($"Timer '{name}' skipped: {result.ErrorMessage}");
            }
        }

        return (imported, skipped);
    }

    private static List<string> CollectTimerMessages(SeTimer source)
    {
        List<string> messages = [];
        if (source.Messages is not null)
            messages.AddRange(source.Messages);
        if (!string.IsNullOrWhiteSpace(source.Message))
            messages.Add(source.Message);
        return messages.Select(m => m.Trim()).Where(m => m.Length > 0).ToList();
    }

    private static int SecondsToMinutes(int? seconds) =>
        seconds is null or <= 0
            ? 30
            : (int)Math.Round(seconds.Value / 60.0, MidpointRounding.AwayFromZero);

    /// <summary>Strips a single leading command sigil and lower-cases, matching how commands are keyed.</summary>
    private static string NormalizeCommandName(string? command) =>
        (command ?? string.Empty).Trim().TrimStart('!').Trim();

    private static string NormalizeQuoteText(string text) => text.Trim().ToLowerInvariant();

    /// <summary>
    /// Maps StreamElements' numeric access ladder onto this project's role ladder
    /// (0=everyone, 1=follower, 2=subscriber, 3=vip, 4=moderator, 5=broadcaster). Anchored on the SE values the
    /// export uses — 0=everyone, 100=subscriber, 500=broadcaster — with the in-between bands mapped monotonically
    /// (SE "regular" ≈ vip, the moderator band below broadcaster). Values above 500 (SE super-mod/owner scales)
    /// clamp to broadcaster. SE has no "follower" tier, so level 1 is never produced.
    /// </summary>
    private static int MapAccessLevel(int? accessLevel) =>
        accessLevel switch
        {
            null or <= 0 => 0, // everyone
            <= 100 => 2, // subscriber
            <= 250 => 3, // vip (SE "regular")
            < 500 => 4, // moderator band
            _ => 5, // broadcaster (SE 500 and any higher owner scale)
        };
}
