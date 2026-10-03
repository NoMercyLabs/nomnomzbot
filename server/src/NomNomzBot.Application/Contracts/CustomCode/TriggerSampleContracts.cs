// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Contracts.CustomCode;

/// <summary>
/// One trigger a test run can fire: a realistic sample of a live event, with the variables the live trigger
/// hands its pipeline and scripts for it. <see cref="Id"/> is unique per sample; <see cref="ResponseKey"/> is
/// the event response it runs, which two samples can share (a ban and a timeout both run <c>channel.ban</c>).
/// </summary>
public sealed record TriggerSample(
    string Id,
    string ResponseKey,
    string? UserId,
    string? UserDisplayName,
    IReadOnlyDictionary<string, string> Variables
);

/// <summary>A live trigger that can describe a sample of itself for a test run.</summary>
public interface ITriggerSampleSource
{
    TriggerSample Sample(DateTimeOffset now);
}
