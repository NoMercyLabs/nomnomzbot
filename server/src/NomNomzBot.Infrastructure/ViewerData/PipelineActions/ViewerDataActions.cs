// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Globalization;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.ViewerData.Services;

namespace NomNomzBot.Infrastructure.ViewerData.PipelineActions;

/// <summary>
/// Pipeline action <c>set_viewer_data</c> (per-viewer-data.md §4): upserts a per-viewer key/value for the
/// triggering viewer, or for <c>target</c> when given. Params: <c>key</c> (slug), <c>value</c>
/// (template-resolved), <c>target</c> (optional — a <c>{variable}</c> reference, <c>@login</c>, external
/// id, or platform user Guid).
/// </summary>
public sealed class SetViewerDataAction(
    IViewerDataService viewerData,
    IUserService users,
    IApplicationDbContext db,
    ITemplateResolver templates
) : ICommandAction
{
    public string ActionType => "set_viewer_data";

    public async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        string? key = action.GetString("key");
        if (string.IsNullOrWhiteSpace(key))
            return ActionResult.Failure("set_viewer_data requires a 'key'.");

        string value = await templates.ResolveAsync(
            action.GetString("value") ?? string.Empty,
            ctx.Variables,
            ctx.BroadcasterId,
            ctx.CancellationToken
        );

        Result<Guid> subject = await ViewerDataActionSupport.ResolveSubjectAsync(
            users,
            db,
            ctx,
            action,
            "set_viewer_data"
        );
        if (subject.IsFailure)
            return ActionResult.Failure(subject.ErrorMessage!);

        Result set = await viewerData.SetAsync(
            ctx.BroadcasterId,
            subject.Value,
            key,
            value,
            ctx.CancellationToken
        );
        if (set.IsFailure)
            return ActionResult.Failure(set.ErrorMessage ?? "set_viewer_data failed.");

        ctx.Variables[$"viewer.data.{key.Trim().ToLowerInvariant()}"] = value;
        return ActionResult.Success(value);
    }
}

/// <summary>
/// Pipeline action <c>adjust_viewer_data</c> (per-viewer-data.md §4): atomic numeric increment of a
/// per-viewer value (unset starts at the delta). Params: <c>key</c>, <c>delta</c> (default 1),
/// <c>target</c> (optional, as on <c>set_viewer_data</c>). The new value lands in
/// <c>{viewer.data.&lt;key&gt;}</c> for the rest of the run.
/// </summary>
public sealed class AdjustViewerDataAction(
    IViewerDataService viewerData,
    IUserService users,
    IApplicationDbContext db
) : ICommandAction
{
    public string ActionType => "adjust_viewer_data";

    public async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        string? key = action.GetString("key");
        if (string.IsNullOrWhiteSpace(key))
            return ActionResult.Failure("adjust_viewer_data requires a 'key'.");

        string? rawDelta = action.GetString("delta");
        long delta = 1;
        if (
            !string.IsNullOrWhiteSpace(rawDelta)
            && !long.TryParse(
                rawDelta,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out delta
            )
        )
            return ActionResult.Failure("adjust_viewer_data 'delta' must be a whole number.");

        Result<Guid> subject = await ViewerDataActionSupport.ResolveSubjectAsync(
            users,
            db,
            ctx,
            action,
            "adjust_viewer_data"
        );
        if (subject.IsFailure)
            return ActionResult.Failure(subject.ErrorMessage!);

        Result<long> adjusted = await viewerData.AdjustAsync(
            ctx.BroadcasterId,
            subject.Value,
            key,
            delta,
            ctx.CancellationToken
        );
        if (adjusted.IsFailure)
            return ActionResult.Failure(adjusted.ErrorMessage ?? "adjust_viewer_data failed.");

        string rendered = adjusted.Value.ToString(CultureInfo.InvariantCulture);
        ctx.Variables[$"viewer.data.{key.Trim().ToLowerInvariant()}"] = rendered;
        return ActionResult.Success(rendered);
    }
}

/// <summary>
/// Shared subject resolution for the viewer-data actions. The <c>target</c> parameter accepts a
/// <c>{variable}</c> reference (e.g. <c>{target.id}</c>, the dispatcher-seeded @mention), an
/// <c>@login</c>/login, a platform external id, or a platform user Guid; absent means the triggering
/// viewer. Logins resolve against known local users only — data about a viewer who never appeared here
/// is meaningless, so there is deliberately no remote lookup.
/// </summary>
internal static class ViewerDataActionSupport
{
    public static async Task<Result<Guid>> ResolveSubjectAsync(
        IUserService users,
        IApplicationDbContext db,
        PipelineExecutionContext ctx,
        ActionDefinition action,
        string verb
    )
    {
        string? target = action.GetString("target");
        if (string.IsNullOrWhiteSpace(target))
            return await ResolveTriggeringViewerAsync(users, ctx, verb);

        string resolved = target.Trim();
        if (resolved.StartsWith('{') && resolved.EndsWith('}'))
            resolved = ctx.Variables.GetValueOrDefault(resolved[1..^1].Trim()) ?? string.Empty;
        resolved = resolved.Trim().TrimStart('@');
        if (resolved.Length == 0)
            return Result.Failure<Guid>(
                $"{verb}: the target could not be resolved — mention a user with @name.",
                "VALIDATION_FAILED"
            );

        if (Guid.TryParse(resolved, out Guid alreadyGuid))
            return Result.Success(alreadyGuid);

        if (resolved.All(char.IsAsciiDigit))
        {
            string login = ctx.Variables.GetValueOrDefault("target.name") ?? resolved;
            string display = ctx.Variables.GetValueOrDefault("target") ?? login;
            return await GetOrCreateAsync(
                users,
                resolved,
                login,
                display,
                verb,
                ctx.CancellationToken
            );
        }

        Guid known = await db
            .Users.AsNoTracking()
            .Where(u => u.Username == resolved.ToLowerInvariant())
            .Select(u => u.Id)
            .FirstOrDefaultAsync(ctx.CancellationToken);
        return known == Guid.Empty
            ? Result.Failure<Guid>($"{verb}: '{resolved}' has not been seen here yet.", "NOT_FOUND")
            : Result.Success(known);
    }

    private static Task<Result<Guid>> ResolveTriggeringViewerAsync(
        IUserService users,
        PipelineExecutionContext ctx,
        string verb
    )
    {
        // Redemption-triggered pipelines carry the platform user Guid; chat-triggered ones carry the
        // provider's external id.
        if (Guid.TryParse(ctx.TriggeredByUserId, out Guid alreadyGuid))
            return Task.FromResult(Result.Success(alreadyGuid));

        return GetOrCreateAsync(
            users,
            ctx.TriggeredByUserId,
            ctx.Variables.GetValueOrDefault("user.name") ?? ctx.TriggeredByDisplayName,
            ctx.TriggeredByDisplayName,
            verb,
            ctx.CancellationToken
        );
    }

    private static async Task<Result<Guid>> GetOrCreateAsync(
        IUserService users,
        string externalUserId,
        string username,
        string displayName,
        string verb,
        CancellationToken ct
    )
    {
        Result<UserDto> user = await users.GetOrCreateAsync(
            externalUserId,
            username,
            displayName,
            cancellationToken: ct
        );
        if (user.IsFailure)
            return Result.Failure<Guid>(
                user.ErrorMessage ?? $"{verb}: the viewer could not be resolved.",
                user.ErrorCode
            );
        return Guid.TryParse(user.Value.Id, out Guid id)
            ? Result.Success(id)
            : Result.Failure<Guid>(
                $"{verb}: the viewer could not be resolved.",
                "VALIDATION_FAILED"
            );
    }
}
