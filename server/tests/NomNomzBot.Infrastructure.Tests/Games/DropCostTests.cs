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

using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;
using NomNomzBot.Infrastructure.Games.Catalog;
using NomNomzBot.Infrastructure.Games.Frames;
using Xunit.Abstractions;

namespace NomNomzBot.Infrastructure.Tests.Games;

/// <summary>
/// Measures the per-drop cost that drop-game.md section 2 quotes: the seeded flight plus the wire frame.
/// The bounds are loose on purpose (a shared CI box); the measured line goes to the test output.
/// </summary>
public sealed class DropCostTests(ITestOutputHelper output)
{
    private const int Drops = 100_000;

    [Fact]
    public void One_drop_costs_microseconds_of_cpu_and_a_few_hundred_bytes_on_the_wire()
    {
        // Warm up the JIT so the loop measures steady state.
        for (ulong seed = 0; seed < 1_000; seed++)
            DropFlight.Compute(seed, 50.0, 50.0, 0.0);

        Stopwatch watch = Stopwatch.StartNew();
        double checksum = 0;
        for (ulong seed = 0; seed < Drops; seed++)
            checksum += DropFlight.Compute(seed * 7919UL, 50.0, 50.0, 0.0).LandingX;
        watch.Stop();
        double flightMicros = watch.Elapsed.TotalMilliseconds * 1000.0 / Drops;

        DropFlightPath path = DropFlight.Compute(ulong.MaxValue, 50.0, 50.0, 0.0);
        GameDropFrame frame = new()
        {
            DropId = 12,
            Player = "Alice_Example",
            Landed = path.LandingX,
            LandingX = path.LandingX,
            Distance = 12.3,
            Hit = false,
            Score = 0,
            Seed = ulong.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture),
            SpawnX = path.SpawnX,
        };
        JsonSerializerOptions options = new(JsonSerializerDefaults.Web);
        JsonSerializer.Serialize(frame, options);
        watch.Restart();
        int bytes = 0;
        for (int i = 0; i < Drops; i++)
            bytes = JsonSerializer.SerializeToUtf8Bytes(frame, options).Length;
        watch.Stop();
        double jsonMicros = watch.Elapsed.TotalMilliseconds * 1000.0 / Drops;

        output.WriteLine(
            $"MEASURED per drop: flight {flightMicros:F3} us, frame JSON {jsonMicros:F3} us, frame {bytes} bytes (checksum {checksum:F1})"
        );

        flightMicros.Should().BeLessThan(20.0, "the seeded flight is a handful of integer draws");
        jsonMicros.Should().BeLessThan(100.0);
        bytes.Should().BeInRange(100, 400, "one small frame per drop");
    }
}
