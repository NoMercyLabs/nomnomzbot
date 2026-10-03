// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Contracts.CustomCode;

namespace NomNomzBot.Infrastructure.CustomEvents.EventHandlers;

/// <summary>A test-run sample of a custom data source, built by the code the live trigger uses.</summary>
public sealed class CustomDataTriggerSampleSource : ITriggerSampleSource
{
    private const string SourceName = "example";

    public TriggerSample Sample(DateTimeOffset now) =>
        new(
            $"custom.{SourceName}",
            $"custom.{SourceName}",
            null,
            SourceName,
            CustomDataVariables.Build(
                SourceName,
                new Dictionary<string, string> { ["temperature"] = "21.5", ["status"] = "ok" }
            )
        );
}
