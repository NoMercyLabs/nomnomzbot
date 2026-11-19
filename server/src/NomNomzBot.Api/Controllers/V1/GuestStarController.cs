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

using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NomNomzBot.Api.Authorization;
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Contracts.Twitch;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>
/// Per-channel Twitch Guest Star (beta) endpoints: the channel's Guest Star settings, the live session, its
/// invites, and slot management. Every route is a thin pass-through to <see cref="ITwitchGuestStarApi"/>,
/// which acts with the channel's own Twitch token; Twitch values are returned as sent.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/channels/{channelId:guid}/guest-star")]
[Authorize]
[Tags("Guest Star")]
public class GuestStarController(ITwitchGuestStarApi guestStar) : BaseController
{
    /// <summary>Invite body: the Twitch user id of the guest.</summary>
    public record InviteGuestRequest(string GuestTwitchUserId);

    /// <summary>Slot assignment body: the ready guest and the slot to place them in.</summary>
    public record AssignSlotRequest(string GuestTwitchUserId, string SlotId);

    /// <summary>Slot move body: a null destination swaps the guest with the empty interface.</summary>
    public record MoveSlotRequest(string? DestinationSlotId);

    /// <summary>Slot settings body: only the values that are set are changed.</summary>
    public record UpdateSlotSettingsRequest(
        bool? IsAudioEnabled = null,
        bool? IsVideoEnabled = null,
        bool? IsLive = null,
        int? Volume = null
    );

    // ─── Channel settings ─────────────────────────────────────────────────────

    /// <summary>The channel's Guest Star settings.</summary>
    [RequireAction("guest-star:read")]
    [HttpGet("settings")]
    [ProducesResponseType<StatusResponseDto<TwitchGuestStarChannelSettings>>(
        StatusCodes.Status200OK
    )]
    public async Task<IActionResult> GetSettings(Guid channelId, CancellationToken ct) =>
        TwitchResultResponse(await guestStar.GetChannelSettingsAsync(channelId, ct));

    /// <summary>Change the channel's Guest Star settings; only the fields that are set are sent.</summary>
    [RequireAction("guest-star:write")]
    [HttpPut("settings")]
    [ProducesResponseType<StatusResponseDto<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateSettings(
        Guid channelId,
        [FromBody] UpdateGuestStarSettingsRequest request,
        CancellationToken ct
    ) => TwitchResultResponse(await guestStar.UpdateChannelSettingsAsync(channelId, request, ct));

    // ─── Session ──────────────────────────────────────────────────────────────

    /// <summary>The ongoing session and the guests in its slots.</summary>
    [RequireAction("guest-star:read")]
    [HttpGet("session")]
    [ProducesResponseType<StatusResponseDto<TwitchGuestStarSession>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSession(Guid channelId, CancellationToken ct) =>
        TwitchResultResponse(await guestStar.GetSessionAsync(channelId, ct));

    /// <summary>Start a session and return it.</summary>
    [RequireAction("guest-star:write")]
    [HttpPost("session")]
    [ProducesResponseType<StatusResponseDto<TwitchGuestStarSession>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateSession(Guid channelId, CancellationToken ct) =>
        TwitchResultResponse(await guestStar.CreateSessionAsync(channelId, ct));

    /// <summary>End a session and return it.</summary>
    [NotDestructive(
        "Ends one Twitch-side session through the Helix API; no local table stores Guest Star data, so nothing here is deleted. A new session can start at once."
    )]
    [RequireAction("guest-star:write")]
    [HttpDelete("sessions/{sessionId}")]
    [ProducesResponseType<StatusResponseDto<TwitchGuestStarSession>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> EndSession(
        Guid channelId,
        string sessionId,
        CancellationToken ct
    ) => TwitchResultResponse(await guestStar.EndSessionAsync(channelId, sessionId, ct));

    // ─── Invites ──────────────────────────────────────────────────────────────

    /// <summary>The pending invites to a session, with each invitee's ready status.</summary>
    [RequireAction("guest-star:read")]
    [HttpGet("sessions/{sessionId}/invites")]
    [ProducesResponseType<StatusResponseDto<IReadOnlyList<TwitchGuestStarInvite>>>(
        StatusCodes.Status200OK
    )]
    public async Task<IActionResult> GetInvites(
        Guid channelId,
        string sessionId,
        CancellationToken ct
    ) => TwitchResultResponse(await guestStar.GetInvitesAsync(channelId, sessionId, ct));

    /// <summary>Invite a guest to the session.</summary>
    [RequireAction("guest-star:write")]
    [HttpPost("sessions/{sessionId}/invites")]
    [ProducesResponseType<StatusResponseDto<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SendInvite(
        Guid channelId,
        string sessionId,
        [FromBody] InviteGuestRequest request,
        CancellationToken ct
    ) =>
        TwitchResultResponse(
            await guestStar.SendInviteAsync(channelId, sessionId, request.GuestTwitchUserId, ct)
        );

    /// <summary>Revoke a guest's invite.</summary>
    [NotDestructive(
        "Revokes one pending Twitch-side invite through the Helix API; no local table stores Guest Star data. The guest can be invited again at once."
    )]
    [RequireAction("guest-star:write")]
    [HttpDelete("sessions/{sessionId}/invites/{guestTwitchUserId}")]
    [ProducesResponseType<StatusResponseDto<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> DeleteInvite(
        Guid channelId,
        string sessionId,
        string guestTwitchUserId,
        CancellationToken ct
    ) =>
        TwitchResultResponse(
            await guestStar.DeleteInviteAsync(channelId, sessionId, guestTwitchUserId, ct)
        );

    // ─── Slots ────────────────────────────────────────────────────────────────

    /// <summary>Place a ready guest in a slot.</summary>
    [RequireAction("guest-star:write")]
    [HttpPost("sessions/{sessionId}/slots")]
    [ProducesResponseType<StatusResponseDto<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> AssignSlot(
        Guid channelId,
        string sessionId,
        [FromBody] AssignSlotRequest request,
        CancellationToken ct
    ) =>
        TwitchResultResponse(
            await guestStar.AssignSlotAsync(
                channelId,
                sessionId,
                request.GuestTwitchUserId,
                request.SlotId,
                ct
            )
        );

    /// <summary>Move the guest in a slot to another slot (swapping if it is taken), or to the empty interface.</summary>
    [RequireAction("guest-star:write")]
    [HttpPatch("sessions/{sessionId}/slots/{slotId}")]
    [ProducesResponseType<StatusResponseDto<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> MoveSlot(
        Guid channelId,
        string sessionId,
        string slotId,
        [FromBody] MoveSlotRequest request,
        CancellationToken ct
    ) =>
        TwitchResultResponse(
            await guestStar.UpdateSlotAsync(
                channelId,
                sessionId,
                slotId,
                request.DestinationSlotId,
                ct
            )
        );

    /// <summary>Remove a guest from a slot, optionally sending them back to the waiting room.</summary>
    [NotDestructive(
        "Removes one guest from a Twitch-side slot through the Helix API; no local table stores Guest Star data. The guest can be invited again."
    )]
    [RequireAction("guest-star:write")]
    [HttpDelete("sessions/{sessionId}/slots/{slotId}")]
    [ProducesResponseType<StatusResponseDto<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> DeleteSlot(
        Guid channelId,
        string sessionId,
        string slotId,
        [FromQuery] string guestTwitchUserId,
        [FromQuery] bool? shouldReinviteGuest,
        CancellationToken ct
    ) =>
        TwitchResultResponse(
            await guestStar.DeleteSlotAsync(
                channelId,
                sessionId,
                guestTwitchUserId,
                slotId,
                shouldReinviteGuest,
                ct
            )
        );

    /// <summary>Change a guest slot's audio, video, live state or volume.</summary>
    [RequireAction("guest-star:write")]
    [HttpPatch("sessions/{sessionId}/slots/{slotId}/settings")]
    [ProducesResponseType<StatusResponseDto<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateSlotSettings(
        Guid channelId,
        string sessionId,
        string slotId,
        [FromBody] UpdateSlotSettingsRequest request,
        CancellationToken ct
    ) =>
        TwitchResultResponse(
            await guestStar.UpdateSlotSettingsAsync(
                channelId,
                sessionId,
                slotId,
                request.IsAudioEnabled,
                request.IsVideoEnabled,
                request.IsLive,
                request.Volume,
                ct
            )
        );
}
