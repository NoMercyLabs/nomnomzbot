// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Commands.Builtin.Personality;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Gdpr;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Services;

namespace NomNomzBot.Infrastructure.Identity.Builtins;

/// <summary>
/// The in-chat data-subject rights surface (gdpr-crypto.md §9): <c>!forgetme</c> [+ <c>!gdpr forget</c>],
/// <c>!mydata</c> [+ <c>!gdpr export</c>], and <c>!gdpr status</c> — a thin alternative surface over the
/// same <see cref="IErasureService"/> use cases the HTTP planes call, never a parallel implementation.
/// The subject is ALWAYS the Twitch-verified chatter (resolved via the standard
/// <see cref="IUserService.GetOrCreateAsync"/> ingest seam); arguments can never target another user.
/// Replies stay strictly PII-free: chat is public, so they carry state words, never data.
/// </summary>
public sealed class GdprSelfServiceExecutor
{
    /// <summary>
    /// The MANDATORY informed-re-entry clause (§9 part 2). Always appended to the erasure completion
    /// reply, outside any customizable template — it is what makes automatic re-entry (a fresh DEK on
    /// the subject's next message) legally informed. Never shorten, restyle, or make it removable.
    /// </summary>
    public const string MandatoryReEntryClause =
        "Sending another message or redeeming a reward will put you back into the system. "
        + "Stay quiet if you want to stay anonymous.";

    /// <summary>The default friendly completion copy (§9 part 1 — streamer-customizable, this is the fallback).</summary>
    public const string DefaultErasedCopy =
        "Done — your data's been sanitized, you're a clean slate.";

    private const string SelfService = "self_service";

    /// <summary>Chat erasure is the full clean slate — the widest scope, matching the §9 re-entry model.</summary>
    private const string ErasureScope = "deployment";

    private readonly IErasureService _erasure;
    private readonly IUserService _users;
    private readonly IBuiltinResponseComposer _composer;
    private readonly ErasureConfirmationTracker _confirmations;

    public GdprSelfServiceExecutor(
        IErasureService erasure,
        IUserService users,
        IBuiltinResponseComposer composer,
        ErasureConfirmationTracker confirmations
    )
    {
        _erasure = erasure;
        _users = users;
        _composer = composer;
        _confirmations = confirmations;
    }

    /// <summary>
    /// The two-step erasure flow. Anything but a bare <c>confirm</c> argument arms the 60s confirmation
    /// window and warns (so <c>!forgetme @someone</c> is just a prompt for the CALLER — no targeting);
    /// <c>confirm</c> inside the window executes the irreversible crypto-shred pipeline.
    /// </summary>
    public async Task<Result<string>> ForgetAsync(
        BuiltinCommandContext context,
        string args,
        CancellationToken ct
    )
    {
        Guid? subject = await ResolveSubjectAsync(context, ct);
        if (subject is not Guid subjectId)
            return Result.Success("Could not resolve your account — try again.");

        if (!string.Equals(args.Trim(), "confirm", StringComparison.OrdinalIgnoreCase))
        {
            _confirmations.Begin(subjectId);
            return Result.Success(
                "This permanently erases the data this bot holds about you — stats, currency, "
                    + "messages, everything — and it cannot be undone. "
                    + "Type !forgetme confirm within 60 seconds to proceed."
            );
        }

        if (!_confirmations.TryConsume(subjectId))
            return Result.Success(
                "There is no pending erasure to confirm — it may have expired. Type !forgetme to start over."
            );

        Result<ErasureRequestDto> erased = await _erasure.RequestErasureAsync(
            new RequestErasureRequest(subjectId, context.BroadcasterId, SelfService, ErasureScope),
            ct
        );
        if (erased.IsFailure)
            return Result.Success(
                "Your erasure could not be completed right now — the request is on record. "
                    + "Check !gdpr status or contact the channel operator."
            );

        // Part 1 (customizable copy: override → tone → default) + part 2 (the fixed clause, ALWAYS appended).
        string done = await _composer.ComposeAsync(
            new BuiltinResponseRequest
            {
                BroadcasterId = context.BroadcasterId,
                Personality = context.Personality,
                BuiltinKey = BuiltinResponseSlots.Forgetme.Key,
                Slot = BuiltinResponseSlots.Forgetme.Done,
                OverrideTemplate = context.CustomResponseTemplate,
                NeutralFallback = DefaultErasedCopy,
            },
            ct
        );
        return Result.Success($"{done} {MandatoryReEntryClause}");
    }

    /// <summary>
    /// Records a data-export request for the chatter. The export document itself is NEVER posted to
    /// chat (public surface) — the reply is a neutral pointer to where the file is furnished: the
    /// dashboard privacy page, or the operator on a no-HTTP self-host (§9).
    /// </summary>
    public async Task<Result<string>> ExportAsync(
        BuiltinCommandContext context,
        CancellationToken ct
    )
    {
        Guid? subject = await ResolveSubjectAsync(context, ct);
        if (subject is not Guid subjectId)
            return Result.Success("Could not resolve your account — try again.");

        Result<DataExportDto> export = await _erasure.RequestExportAsync(
            new RequestExportRequest(subjectId, context.BroadcasterId, SelfService),
            ct
        );
        if (export.IsFailure)
            return Result.Success(
                "Your data export could not be prepared right now — try again later."
            );

        return Result.Success(
            "Your data export has been recorded — for privacy it is never posted in chat. "
                + "Collect it from the dashboard's privacy page, or ask the channel operator "
                + "on a self-hosted bot. Check progress anytime with !gdpr status."
        );
    }

    /// <summary>Reports the chatter's LATEST request (self-scoped by subject) — state words only, no data.</summary>
    public async Task<Result<string>> StatusAsync(
        BuiltinCommandContext context,
        CancellationToken ct
    )
    {
        Guid? subject = await ResolveSubjectAsync(context, ct);
        if (subject is not Guid subjectId)
            return Result.Success("Could not resolve your account — try again.");

        Result<PagedList<ErasureRequestDto>> page = await _erasure.ListRequestsAsync(
            new PaginationParams(1, 1),
            subjectId,
            null,
            ct
        );
        if (page.IsFailure)
            return Result.Success("Could not look up your data requests — try again later.");

        if (page.Value.Items.Count == 0)
            return Result.Success(
                "You have no data requests on record. !forgetme requests erasure, !mydata requests an export."
            );

        ErasureRequestDto latest = page.Value.Items[0];
        return Result.Success(
            $"Your latest data request ({latest.RequestType}) is {latest.Status}."
        );
    }

    /// <summary>
    /// The subject is the verified chatter, resolved through the same get-or-create seam every chat
    /// ingest path uses — by construction a chat command can only ever touch the issuer's own data.
    /// </summary>
    private async Task<Guid?> ResolveSubjectAsync(
        BuiltinCommandContext context,
        CancellationToken ct
    )
    {
        Result<UserDto> user = await _users.GetOrCreateAsync(
            context.TriggeringUserId,
            context.TriggeringUserLogin,
            context.TriggeringUserDisplayName,
            cancellationToken: ct
        );
        return user.IsSuccess && Guid.TryParse(user.Value.Id, out Guid subjectId)
            ? subjectId
            : null;
    }
}

/// <summary>!forgetme [confirm] — reserved self-service erasure (two-step, gdpr-crypto.md §9).</summary>
public sealed class ForgetMeBuiltin(GdprSelfServiceExecutor executor) : IBuiltinCommand
{
    public string BuiltinKey => BuiltinResponseSlots.Forgetme.Key;
    public int DefaultCooldownSeconds => 0;
    public int DefaultMinPermissionLevel => 0;
    public bool IsReserved => true;

    public Task<Result<string>> ExecuteAsync(
        BuiltinCommandContext context,
        CancellationToken ct = default
    ) => executor.ForgetAsync(context, context.Args, ct);
}

/// <summary>!mydata — reserved self-service data export request (gdpr-crypto.md §9).</summary>
public sealed class MyDataBuiltin(GdprSelfServiceExecutor executor) : IBuiltinCommand
{
    public string BuiltinKey => "mydata";
    public int DefaultCooldownSeconds => 0;
    public int DefaultMinPermissionLevel => 0;
    public bool IsReserved => true;

    public Task<Result<string>> ExecuteAsync(
        BuiltinCommandContext context,
        CancellationToken ct = default
    ) => executor.ExportAsync(context, ct);
}

/// <summary>
/// !gdpr status | forget | export — the reserved umbrella trigger (§9): <c>status</c> is its own
/// read-only surface; <c>forget</c> / <c>export</c> are aliases into the same flows as
/// <c>!forgetme</c> / <c>!mydata</c> (shared executor, shared confirmation window).
/// </summary>
public sealed class GdprBuiltin(GdprSelfServiceExecutor executor) : IBuiltinCommand
{
    public string BuiltinKey => "gdpr";
    public int DefaultCooldownSeconds => 0;
    public int DefaultMinPermissionLevel => 0;
    public bool IsReserved => true;

    public Task<Result<string>> ExecuteAsync(
        BuiltinCommandContext context,
        CancellationToken ct = default
    )
    {
        string[] parts = context.Args.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        string verb = parts.Length > 0 ? parts[0].ToLowerInvariant() : string.Empty;
        string rest = parts.Length > 1 ? parts[1].Trim() : string.Empty;

        return verb switch
        {
            "status" => executor.StatusAsync(context, ct),
            "forget" => executor.ForgetAsync(context, rest, ct),
            "export" => executor.ExportAsync(context, ct),
            _ => Task.FromResult(
                Result.Success(
                    "Your data rights, always available: !gdpr status | !gdpr forget | !gdpr export."
                )
            ),
        };
    }
}
