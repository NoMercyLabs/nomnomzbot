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

using NomNomzBot.Application.Abstractions.Localization;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.MediaShare.Dtos;
using NomNomzBot.Application.MediaShare.Services;
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Infrastructure.MediaShare.PipelineActions;

/// <summary>
/// Pipeline action <c>media_playback</c>: runs one media-share player command. Params: <c>command</c>
/// (play, pause, stop, skip, seek, volume, replay, play_now) and <c>value</c> (template): seconds for seek,
/// percent for volume, the request id for play_now and replay.
/// </summary>
public sealed class MediaPlaybackAction(
    IMediaSharePlaybackService playback,
    ITemplateResolver templates
) : ICommandAction
{
    public string ActionType => "media_playback";

    public LocalizedText Category => new("pipeline.category.media");

    public LocalizedText Description => new("pipeline.media_playback.description");

    public bool ResolvesOwnTemplates => true;

    public IReadOnlyList<PipelineActionFieldDescriptor> Fields =>
        [
            new(
                "command",
                PipelineActionFieldKind.Enum,
                Required: true,
                Description: new("pipeline.media_playback.command.help"),
                Options: MediaPlaybackCommands.All
            ),
            new(
                "value",
                PipelineActionFieldKind.Text,
                Templated: true,
                Description: new("pipeline.media_playback.value.help")
            ),
        ];

    public async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        string command = action.GetString("command") ?? string.Empty;
        string value = await templates.ResolveAsync(
            action.GetString("value") ?? string.Empty,
            ctx.Variables,
            ctx.BroadcasterId,
            ctx.CancellationToken
        );

        Result<MediaPlaybackCommandRequest> request = BuildRequest(command, value.Trim());
        if (request.IsFailure)
            return ActionResult.Failure(request.ErrorMessage!);

        Result<MediaPlayerStateDto> result = await playback.SendCommandAsync(
            ctx.BroadcasterId,
            request.Value,
            ctx.CancellationToken
        );
        return result.IsSuccess
            ? ActionResult.Success(result.Value.State)
            : ActionResult.Failure(result.ErrorMessage ?? "media_playback failed.");
    }

    private static Result<MediaPlaybackCommandRequest> BuildRequest(string command, string value)
    {
        bool isNumber = int.TryParse(value, out int number);
        bool isId = OwnedIdCodec.TryDecode(value, out Guid id);

        return command switch
        {
            MediaPlaybackCommands.Seek or MediaPlaybackCommands.Volume when !isNumber =>
                Result.Failure<MediaPlaybackCommandRequest>(
                    $"media_playback {command} needs a number as its value."
                ),
            MediaPlaybackCommands.Seek => Result.Success(
                new MediaPlaybackCommandRequest(command, number)
            ),
            MediaPlaybackCommands.Volume => Result.Success(
                new MediaPlaybackCommandRequest(command, Volume: number)
            ),
            MediaPlaybackCommands.PlayNow
            or MediaPlaybackCommands.Replay when value.Length > 0 && !isId =>
                Result.Failure<MediaPlaybackCommandRequest>(
                    $"media_playback {command} needs a request id as its value."
                ),
            _ => Result.Success(
                new MediaPlaybackCommandRequest(command, RequestId: isId ? id : null)
            ),
        };
    }
}
