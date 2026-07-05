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
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Discord;
using NomNomzBot.Domain.Discord.Entities;

namespace NomNomzBot.Infrastructure.Discord.Interactions;

/// <summary>
/// Routes signature-verified Discord interactions: PING (1) → PONG (<c>{"type":1}</c>); MESSAGE_COMPONENT (3)
/// with the opt-in button's <c>custom_id notify_optin:{roleId:N}</c> → toggles the clicking member's opt-in
/// through <see cref="IDiscordNotificationRoleService"/> (source <c>"button"</c>) and answers with an ephemeral
/// CHANNEL_MESSAGE_WITH_SOURCE (4, flags 64) confirmation. The webhook is app-level (no tenant context) — the
/// tenant is resolved FROM the role row the custom_id names; the ambient tenant filter passes all rows when no
/// tenant is set, and the soft-delete filter still hides deleted roles. Unknown types/custom_ids get an
/// ephemeral "not supported" reply (never an error status, so Discord doesn't surface a scary failure to the
/// member); only an unparseable body is a <c>VALIDATION_FAILED</c> failure.
/// </summary>
public sealed class DiscordInteractionService : IDiscordInteractionService
{
    /// <summary>Must match the custom_id the gateway stamps on the posted button (<c>DiscordRestBotGateway</c>).</summary>
    internal const string OptInCustomIdPrefix = "notify_optin:";
    internal const string ButtonSource = "button";

    // Interaction types / callback types / flags (docs.discord.com receiving-and-responding).
    private const int InteractionTypePing = 1;
    private const int InteractionTypeMessageComponent = 3;
    private const int CallbackTypePong = 1;
    private const int CallbackTypeChannelMessageWithSource = 4;
    private const int MessageFlagEphemeral = 1 << 6;

    private static readonly JsonSerializerOptions WireJson = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IApplicationDbContext _db;
    private readonly IDiscordNotificationRoleService _roles;
    private readonly ILogger<DiscordInteractionService> _logger;

    public DiscordInteractionService(
        IApplicationDbContext db,
        IDiscordNotificationRoleService roles,
        ILogger<DiscordInteractionService> logger
    )
    {
        _db = db;
        _roles = roles;
        _logger = logger;
    }

    public async Task<Result<string>> HandleAsync(string rawBody, CancellationToken ct = default)
    {
        InteractionPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<InteractionPayload>(rawBody, WireJson);
        }
        catch (JsonException)
        {
            return Result.Failure<string>(
                "The interaction payload is not valid JSON.",
                "VALIDATION_FAILED"
            );
        }
        if (payload is null)
            return Result.Failure<string>("The interaction payload is empty.", "VALIDATION_FAILED");

        return payload.Type switch
        {
            InteractionTypePing => Result.Success(
                Serialize(new InteractionReply(CallbackTypePong))
            ),
            InteractionTypeMessageComponent => await HandleComponentAsync(payload, ct),
            _ => Result.Success(Ephemeral("This interaction isn't supported.")),
        };
    }

    private async Task<Result<string>> HandleComponentAsync(
        InteractionPayload payload,
        CancellationToken ct
    )
    {
        string? customId = payload.Data?.CustomId;
        if (
            customId is null
            || !customId.StartsWith(OptInCustomIdPrefix, StringComparison.Ordinal)
            || !Guid.TryParseExact(customId[OptInCustomIdPrefix.Length..], "N", out Guid roleId)
        )
            return Result.Success(Ephemeral("This interaction isn't supported."));

        // In a guild the invoking user rides inside `member`; in a DM it is the top-level `user`.
        string? memberId = payload.Member?.User?.Id ?? payload.User?.Id;
        if (string.IsNullOrEmpty(memberId))
            return Result.Success(Ephemeral("This interaction isn't supported."));

        DiscordNotificationRole? role = await _db.DiscordNotificationRoles.FirstOrDefaultAsync(
            r => r.Id == roleId,
            ct
        );
        if (role is null)
            return Result.Success(Ephemeral("This button isn't active anymore."));
        if (!role.SelfAssignEnabled)
            return Result.Success(Ephemeral("Self-assign is currently disabled for this role."));

        string roleLabel = role.RoleName ?? "notify";
        bool optedIn = await _db.DiscordMemberOptIns.AnyAsync(
            o =>
                o.NotificationRoleId == roleId
                && o.DiscordMemberId == memberId
                && o.OptedOutAt == null,
            ct
        );

        Result toggled = optedIn
            ? await _roles.OptOutMemberAsync(role.BroadcasterId, roleId, memberId, ButtonSource, ct)
            : await _roles.OptInMemberAsync(role.BroadcasterId, roleId, memberId, ButtonSource, ct);
        if (toggled.IsFailure)
        {
            // The opt-in row may already be updated (the role push into the guild is best-effort) — tell the
            // member something went wrong without leaking internals. PII discipline: no member id in the log.
            _logger.LogWarning(
                "Discord opt-in button toggle failed for role {RoleId}: {Code}",
                roleId,
                toggled.ErrorCode
            );
            return Result.Success(Ephemeral("Something went wrong — please try again later."));
        }

        return Result.Success(
            Ephemeral(
                optedIn
                    ? $"Notifications off — the **{roleLabel}** role was removed."
                    : $"Notifications on — the **{roleLabel}** role was added."
            )
        );
    }

    private static string Ephemeral(string content) =>
        Serialize(
            new InteractionReply(
                CallbackTypeChannelMessageWithSource,
                new InteractionReplyData(content, MessageFlagEphemeral)
            )
        );

    private static string Serialize(InteractionReply reply) =>
        JsonSerializer.Serialize(reply, WireJson);

    // ─── Discord wire DTOs (System.Text.Json — Discord's own wire format) ─────

    private sealed record InteractionPayload(
        [property: JsonPropertyName("type")] int Type,
        [property: JsonPropertyName("data")] InteractionData? Data = null,
        [property: JsonPropertyName("member")] InteractionMember? Member = null,
        [property: JsonPropertyName("user")] InteractionUser? User = null
    );

    private sealed record InteractionData(
        [property: JsonPropertyName("custom_id")] string? CustomId = null
    );

    private sealed record InteractionMember(
        [property: JsonPropertyName("user")] InteractionUser? User = null
    );

    private sealed record InteractionUser([property: JsonPropertyName("id")] string? Id = null);

    private sealed record InteractionReply(
        [property: JsonPropertyName("type")] int Type,
        [property: JsonPropertyName("data")] InteractionReplyData? Data = null
    );

    private sealed record InteractionReplyData(
        [property: JsonPropertyName("content")] string Content,
        [property: JsonPropertyName("flags")] int Flags
    );
}
