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

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Billing;
using NomNomzBot.Application.DTOs.Billing;

namespace NomNomzBot.Infrastructure.Tts;

/// <summary>
/// The plan's monthly TTS total (<c>tts_monthly_characters</c>): the characters sent to synthesis for a channel in
/// the current calendar month (UTC), kept by the usage meter. -1 (the paid plans, self-host) is no total.
/// </summary>
internal static class TtsMonthlyAllowance
{
    public const string MetricKey = "tts_monthly_characters";

    /// <summary>True when <paramref name="characters"/> more still fit this month. A failed check refuses.</summary>
    public static async Task<bool> FitsAsync(
        IUsageMeteringService usage,
        Guid broadcasterId,
        int characters,
        CancellationToken ct
    )
    {
        Result<QuotaCheckDto> check = await usage.CheckAsync(
            broadcasterId,
            MetricKey,
            characters,
            ct
        );
        return check is { IsSuccess: true, Value.Allowed: true };
    }

    /// <summary>Adds the characters of one spoken line to this month's total.</summary>
    public static Task<Result> RecordAsync(
        IUsageMeteringService usage,
        Guid broadcasterId,
        int characters,
        CancellationToken ct
    ) =>
        characters > 0
            ? usage.RecordAsync(broadcasterId, MetricKey, characters, ct)
            : Task.FromResult(Result.Success());
}
