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
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Platform;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Chat.Whispers;

namespace NomNomzBot.Infrastructure.Chat.PipelineActions;

/// <summary>
/// Whispers a user from a pipeline step or a script (<c>nnz.api.actions.invoke('send_whisper', ...)</c>). The
/// recipient is a login, an @mention or a Twitch user id; both fields are templates, so <c>{user.name}</c> whispers
/// the person who triggered the step. The send goes through the Twitch <see cref="IPlatformDirectMessageSender"/>
/// (picked by provider, so a second platform is one more sender) and counts against Twitch's daily limit of new
/// whisper recipients (<see cref="WhisperRecipientCap"/>). Every failure is a failed result with a plain reason.
/// </summary>
public sealed class SendWhisperAction : ICommandAction
{
    private readonly ITemplateResolver _resolver;
    private readonly ITwitchUsersApi _users;
    private readonly IMentionTargetResolver _mentionTargets;
    private readonly IReadOnlyDictionary<string, IPlatformDirectMessageSender> _sendersByProvider;
    private readonly WhisperRecipientCap _cap;

    public string ActionType => "send_whisper";

    public LocalizedText Category => new("pipeline.category.chat");

    public LocalizedText Description => new("pipeline.send_whisper.description");
    public bool ResolvesOwnTemplates => true;

    public IReadOnlyList<PipelineActionFieldDescriptor> Fields =>
        [
            new(
                "recipient",
                PipelineActionFieldKind.Text,
                Required: true,
                Templated: true,
                Description: new("pipeline.send_whisper.recipient.help")
            ),
            new(
                "message",
                PipelineActionFieldKind.Text,
                Required: true,
                Templated: true,
                Description: new("pipeline.send_whisper.message.help")
            ),
        ];

    public SendWhisperAction(
        ITemplateResolver resolver,
        ITwitchUsersApi users,
        IMentionTargetResolver mentionTargets,
        IEnumerable<IPlatformDirectMessageSender> senders,
        WhisperRecipientCap cap
    )
    {
        _resolver = resolver;
        _users = users;
        _mentionTargets = mentionTargets;
        _sendersByProvider = senders.ToDictionary(s => s.Provider);
        _cap = cap;
    }

    public async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        string recipient = await _mentionTargets.ResolveLoginAsync(
            await RenderAsync(ctx, action.GetString("recipient")),
            ctx.CancellationToken
        );
        if (string.IsNullOrWhiteSpace(recipient))
            return ActionResult.Failure("send_whisper requires a 'recipient'");

        string message = (await RenderAsync(ctx, action.GetString("message"))).Trim();
        if (message.Length == 0)
            return ActionResult.Failure("send_whisper requires a 'message' that is not empty");

        if (
            !_sendersByProvider.TryGetValue(
                AuthEnums.Platform.Twitch,
                out IPlatformDirectMessageSender? sender
            )
        )
            return ActionResult.Failure("send_whisper is not available: no Twitch whisper sender");

        Result<TwitchUser> target = await FindTwitchUserAsync(recipient, ctx.CancellationToken);
        if (target.IsFailure)
            return ActionResult.Failure(target.ErrorMessage ?? "send_whisper lookup failed");

        if (!_cap.TryAdmit(target.Value.Id))
            return ActionResult.Failure(
                $"send_whisper hit Twitch's daily limit of {WhisperRecipientCap.DailyNewRecipientLimit} new whisper recipients"
            );

        Result sent = await sender.SendAsync(
            ctx.BroadcasterId,
            target.Value.Id,
            message,
            ctx.CancellationToken
        );
        return sent.IsSuccess
            ? ActionResult.Success(message)
            : ActionResult.Failure(
                $"send_whisper to '{target.Value.DisplayName}' failed: {sent.ErrorMessage}"
            );
    }

    private async Task<string> RenderAsync(PipelineExecutionContext ctx, string? template) =>
        await _resolver.ResolveAsync(
            template ?? string.Empty,
            ctx.Variables,
            ctx.BroadcasterId,
            ctx.CancellationToken
        );

    // A numeric recipient is a Twitch user id, anything else a login (the same rule shoutout follows).
    // Both are checked against Twitch, so a whisper never goes to an id nobody owns.
    private async Task<Result<TwitchUser>> FindTwitchUserAsync(
        string recipient,
        CancellationToken ct
    )
    {
        Result<IReadOnlyList<TwitchUser>> lookup = recipient.All(char.IsAsciiDigit)
            ? await _users.GetUsersByIdsAsync([recipient], ct)
            : await _users.GetUsersByLoginsAsync([recipient.ToLowerInvariant()], ct);
        if (lookup.IsFailure)
            return Result.Failure<TwitchUser>(
                $"send_whisper could not look up '{recipient}' on Twitch: {lookup.ErrorMessage}"
            );

        TwitchUser? found = lookup.Value.FirstOrDefault();
        return found is null
            ? Result.Failure<TwitchUser>(
                $"send_whisper recipient '{recipient}' was not found on Twitch"
            )
            : Result.Success(found);
    }
}
