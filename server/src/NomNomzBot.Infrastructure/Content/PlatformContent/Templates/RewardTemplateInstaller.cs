// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.PlatformContent;
using NomNomzBot.Application.Rewards.Dtos;
using NomNomzBot.Application.Rewards.Services;
using NomNomzBot.Domain.PlatformContent.Entities;
using NomNomzBot.Domain.Rewards.Entities;

namespace NomNomzBot.Infrastructure.Content.PlatformContent.Templates;

/// <summary>
/// Installs a <c>reward</c> template: creates the reward through <see cref="IRewardService.CreateAsync"/> with
/// Twitch push on — the same path as the Rewards page, so the reward is live on the channel's Twitch, a title
/// the channel already uses on Twitch is refused (never duplicated), and a bound pipeline must belong to the
/// channel. Then stamps provenance on the local row.
/// </summary>
public sealed partial class RewardTemplateInstaller(
    IApplicationDbContext db,
    IRewardService rewards
) : IPlatformTemplateInstaller
{
    // Twitch Create Custom Reward limits.
    private const int MaxTitleLength = 45;
    private const int MaxPromptLength = 200;
    private const int MaxResponseLength = 2000;
    private const int MaxCooldownSeconds = 604_800;
    private const int MaxTimerDurationSeconds = 86_400;

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex HexColor();

    public string Kind => PlatformContentKinds.Reward;

    public string WriteActionKey => "reward:manage";

    public Task<Result> ValidatePayloadAsync(string payloadJson, CancellationToken ct = default)
    {
        Result<RewardTemplatePayload> parsed = PlatformTemplateJson.Parse<RewardTemplatePayload>(
            payloadJson
        );
        return Task.FromResult(parsed.IsFailure ? parsed : Validate(parsed.Value));
    }

    public async Task<Result<InstalledPlatformTemplateDto>> InstallAsync(
        PlatformTemplateInstall install,
        CancellationToken ct = default
    )
    {
        Result<RewardTemplatePayload> parsed = PlatformTemplateJson.Parse<RewardTemplatePayload>(
            install.PayloadJson
        );
        if (parsed.IsFailure)
            return parsed.WithValue<InstalledPlatformTemplateDto>(null!);
        RewardTemplatePayload payload = parsed.Value;

        Result valid = Validate(payload);
        if (valid.IsFailure)
            return valid.WithValue<InstalledPlatformTemplateDto>(null!);

        Result<RewardDetail> created = await rewards.CreateAsync(
            install.BroadcasterId.ToString(),
            new()
            {
                Title = payload.Title.Trim(),
                Cost = payload.Cost,
                Prompt = payload.Prompt,
                Response = payload.Response,
                IsUserInputRequired = payload.IsUserInputRequired,
                BackgroundColor = payload.BackgroundColor,
                MaxPerStream = payload.MaxPerStream,
                MaxPerUserPerStream = payload.MaxPerUserPerStream,
                GlobalCooldownSeconds = payload.GlobalCooldownSeconds,
                TimerDurationSeconds = payload.TimerDurationSeconds,
                PipelineId = install.PipelineId == Guid.Empty ? null : install.PipelineId,
            },
            pushToTwitch: true,
            ct
        );
        if (created.IsFailure)
            return created.WithValue<InstalledPlatformTemplateDto>(null!);

        Guid rewardId = Guid.Parse(created.Value.Id);
        Reward row = await db.Rewards.FirstAsync(
            r => r.Id == rewardId && r.BroadcasterId == install.BroadcasterId,
            ct
        );
        row.Stamp(
            install.Source,
            RewardTemplatePayload.FromEntity(row).ComputeHash(),
            DateTime.UtcNow
        );
        await db.SaveChangesAsync(ct);

        return Result.Success(new InstalledPlatformTemplateDto(Kind, row.Id, row.Title));
    }

    public Task<IReadOnlyList<PlatformContentCopy>> ListCopiesAsync(
        Guid definitionId,
        CancellationToken ct = default
    ) =>
        PlatformTemplateCopies.ListAsync(
            db.Rewards,
            definitionId,
            row => row.Id,
            row => RewardTemplatePayload.FromEntity(row).ComputeHash(),
            ct
        );

    public async Task<Result> UpdateCopyAsync(
        PlatformTemplateCopyUpdate update,
        CancellationToken ct = default
    )
    {
        Result<RewardTemplatePayload> parsed = PlatformTemplateJson.Parse<RewardTemplatePayload>(
            update.PayloadJson
        );
        if (parsed.IsFailure)
            return parsed;
        RewardTemplatePayload payload = parsed.Value;

        Result valid = Validate(payload);
        if (valid.IsFailure)
            return valid;

        Reward? row = await db
            .Rewards.IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                r =>
                    r.Id == update.RowId
                    && r.BroadcasterId == update.BroadcasterId
                    && r.DeletedAt == null,
                ct
            );
        if (row is null)
            return Result.Failure("The installed reward no longer exists.", "NOT_FOUND");

        // Goes through the reward update path, so the change is pushed to the channel's Twitch and the local
        // row mirrors what Twitch confirmed. Prompt and response are sent as empty rather than omitted so a
        // version that drops them clears them; the optional limits the update API cannot clear keep the
        // copy's value, and the restamp below records the copy's real state either way.
        Result<RewardDetail> updated = await rewards.UpdateAsync(
            update.BroadcasterId.ToString(),
            row.Id.ToString(),
            new()
            {
                Title = payload.Title.Trim(),
                Cost = payload.Cost,
                Prompt = payload.Prompt ?? string.Empty,
                Response = payload.Response ?? string.Empty,
                IsUserInputRequired = payload.IsUserInputRequired,
                BackgroundColor = payload.BackgroundColor,
                MaxPerStream = payload.MaxPerStream,
                MaxPerUserPerStream = payload.MaxPerUserPerStream,
                GlobalCooldownSeconds = payload.GlobalCooldownSeconds,
                TimerDurationSeconds = payload.TimerDurationSeconds,
            },
            ct
        );
        if (updated.IsFailure)
            return updated;

        row.Stamp(
            update.Source,
            RewardTemplatePayload.FromEntity(row).ComputeHash(),
            DateTime.UtcNow
        );
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    private static Result Validate(RewardTemplatePayload payload)
    {
        string title = payload.Title.Trim();
        if (title.Length == 0)
            return Failure("A reward template needs a title.");
        if (title.Length > MaxTitleLength)
            return Failure($"The reward title is longer than {MaxTitleLength} characters.");
        if (payload.Cost < 1)
            return Failure("The reward cost must be at least 1 channel point.");
        if (payload.Prompt is { Length: > MaxPromptLength })
            return Failure($"The prompt is longer than {MaxPromptLength} characters.");
        if (payload.Response is { Length: > MaxResponseLength })
            return Failure($"The chat response is longer than {MaxResponseLength} characters.");
        if (payload.BackgroundColor is { } color && !HexColor().IsMatch(color))
            return Failure("The background color must be a hex color like #9146FF.");
        if (payload.MaxPerStream is < 1)
            return Failure("The per-stream limit must be at least 1.");
        if (payload.MaxPerUserPerStream is < 1)
            return Failure("The per-viewer limit must be at least 1.");
        if (payload.GlobalCooldownSeconds is < 1 or > MaxCooldownSeconds)
            return Failure($"The cooldown must be between 1 and {MaxCooldownSeconds} seconds.");
        if (payload.TimerDurationSeconds is < 1 or > MaxTimerDurationSeconds)
            return Failure(
                $"The countdown must be between 1 and {MaxTimerDurationSeconds} seconds."
            );
        return Result.Success();
    }

    private static Result Failure(string message) => Result.Failure(message, "VALIDATION_FAILED");
}
