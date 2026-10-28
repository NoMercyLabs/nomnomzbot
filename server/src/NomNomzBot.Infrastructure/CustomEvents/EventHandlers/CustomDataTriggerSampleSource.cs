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
