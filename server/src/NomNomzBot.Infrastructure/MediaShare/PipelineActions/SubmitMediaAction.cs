// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.MediaShare.Dtos;
using NomNomzBot.Application.MediaShare.Services;

namespace NomNomzBot.Infrastructure.MediaShare.PipelineActions;

/// <summary>
/// Pipeline action <c>submit_media</c> (media-share.md §4): submits a clip/video for the TRIGGERING viewer,
/// so a channel-point redemption can require a clip URL and enqueue it. Params: <c>url</c> (template).
/// </summary>
public sealed class SubmitMediaAction(
    IMediaShareService media,
    IUserService users,
    ITemplateResolver templates
) : ICommandAction
{
    public string ActionType => "submit_media";

    public async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        string url = await templates.ResolveAsync(
            action.GetString("url") ?? string.Empty,
            ctx.Variables,
            ctx.BroadcasterId,
            ctx.CancellationToken
        );
        if (string.IsNullOrWhiteSpace(url))
            return ActionResult.Failure("submit_media requires a 'url'.");

        // Redemption paths carry the platform user Guid; chat paths carry the provider's external id.
        Guid viewerUserId;
        if (!Guid.TryParse(ctx.TriggeredByUserId, out viewerUserId))
        {
            Result<UserDto> user = await users.GetOrCreateAsync(
                ctx.TriggeredByUserId,
                ctx.Variables.GetValueOrDefault("user.name") ?? ctx.TriggeredByDisplayName,
                ctx.TriggeredByDisplayName,
                cancellationToken: ctx.CancellationToken
            );
            if (user.IsFailure || !Guid.TryParse(user.Value.Id, out viewerUserId))
                return ActionResult.Failure("submit_media: the viewer could not be resolved.");
        }

        Result<MediaShareRequestDto> result = await media.SubmitAsync(
            ctx.BroadcasterId,
            viewerUserId,
            new SubmitMediaRequest(url),
            ctx.CancellationToken
        );
        return result.IsSuccess
            ? ActionResult.Success(result.Value.Title ?? result.Value.MediaRef)
            : ActionResult.Failure(result.ErrorMessage ?? "submit_media failed.");
    }
}
