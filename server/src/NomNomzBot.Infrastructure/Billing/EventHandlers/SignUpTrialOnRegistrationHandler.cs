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

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Billing;
using NomNomzBot.Application.DTOs.Billing;
using NomNomzBot.Domain.Identity.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Billing.EventHandlers;

/// <summary>
/// Gives a new sign-up <see cref="BillingOptions.SignUpTrialDays"/> of Base as a gift, once per channel. The event
/// fires only for a brand-new account, before an invite in the sign-up link is redeemed; that invite's gift then
/// ends the trial. 0 days (the shipped setting) gives no trial.
/// </summary>
public sealed class SignUpTrialOnRegistrationHandler(
    IEntitlementGrantService grants,
    IOptions<BillingOptions> options,
    ILogger<SignUpTrialOnRegistrationHandler> logger
) : IEventHandler<UserRegisteredEvent>
{
    public async Task HandleAsync(UserRegisteredEvent @event, CancellationToken ct = default)
    {
        int days = options.Value.SignUpTrialDays;
        if (days <= 0 || @event.BroadcasterId == Guid.Empty)
            return;

        Result<EntitlementGrantDto> trial = await grants.IssueSignUpTrialAsync(
            @event.BroadcasterId,
            @event.UserId,
            days,
            ct
        );
        if (trial.IsFailure)
            logger.LogInformation(
                "No sign-up trial for channel {BroadcasterId}: {Reason}",
                @event.BroadcasterId,
                trial.ErrorMessage
            );
    }
}
