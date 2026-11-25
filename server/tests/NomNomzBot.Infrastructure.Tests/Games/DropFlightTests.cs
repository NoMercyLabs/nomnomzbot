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

using FluentAssertions;
using NomNomzBot.Infrastructure.Games.Catalog;

namespace NomNomzBot.Infrastructure.Tests.Games;

/// <summary>
/// Pins the drop generator (drop-game.md section 2): splitmix64 seeds xoshiro256**, and the first two draws
/// give the spawn and the landing offset. The expected numbers come from a separate reference
/// implementation, so a change to the generator or to the way a draw becomes a position fails here.
/// </summary>
public sealed class DropFlightTests
{
    [Theory]
    [InlineData(0UL, 11091344671253066420UL, 13793997310169335082UL, 1900383378846508768UL)]
    [InlineData(1UL, 12966619160104079557UL, 9600361134598540522UL, 10590380919521690900UL)]
    public void The_generator_stream_matches_the_reference_vector(
        ulong seed,
        ulong first,
        ulong second,
        ulong third
    )
    {
        Xoshiro256StarStar generator = new(seed);

        generator.Next().Should().Be(first);
        generator.Next().Should().Be(second);
        generator.Next().Should().Be(third);
    }

    [Theory]
    [InlineData(0UL, 50.0, 20.0, 78.6)]
    [InlineData(2UL, 50.0, 37.3, 47.3)]
    [InlineData(3UL, 50.0, 64.8, 3.1)]
    [InlineData(5UL, 50.0, 45.5, 39.2)]
    [InlineData(ulong.MaxValue, 50.0, 4.0, 47.0)]
    [InlineData(0UL, 10.0, 20.0, 38.6)]
    [InlineData(2UL, 10.0, 37.3, 7.3)]
    [InlineData(3UL, 10.0, 64.8, 0.0)]
    [InlineData(5UL, 10.0, 45.5, 0.0)]
    [InlineData(ulong.MaxValue, 10.0, 4.0, 7.0)]
    public void A_seed_gives_the_pinned_spawn_and_landing(
        ulong seed,
        double target,
        double spawnX,
        double landingX
    )
    {
        DropFlightPath path = DropFlight.Compute(seed, target, 50.0, 0.0);

        path.SpawnX.Should().Be(spawnX);
        path.LandingX.Should().Be(landingX, "a landing past the track end is clamped to it");
    }

    [Fact]
    public void The_same_seed_always_gives_the_same_path_and_another_seed_a_different_one()
    {
        DropFlightPath first = DropFlight.Compute(123456789UL, 40.0, 50.0, 0.0);
        DropFlightPath again = DropFlight.Compute(123456789UL, 40.0, 50.0, 0.0);
        DropFlightPath other = DropFlight.Compute(123456790UL, 40.0, 50.0, 0.0);

        again.Should().Be(first);
        other.Should().NotBe(first);
    }

    [Fact]
    public void A_boost_pulls_the_landing_toward_the_target_and_leaves_the_spawn_alone()
    {
        // Seed 0 sits 28.64 right of target 50; a boost of 0.5 halves that distance: 50 + 14.32 = 64.3.
        DropFlightPath plain = DropFlight.Compute(0UL, 50.0, 50.0, 0.0);
        DropFlightPath boosted = DropFlight.Compute(0UL, 50.0, 50.0, 0.5);

        boosted.LandingX.Should().Be(64.3);
        boosted.SpawnX.Should().Be(plain.SpawnX, "the seed and its stream do not change");
    }

    [Fact]
    public void Every_landing_stays_on_the_track()
    {
        for (ulong seed = 0; seed < 5000; seed++)
        {
            DropFlightPath path = DropFlight.Compute(seed, 95.0, 50.0, 0.0);
            path.LandingX.Should().BeInRange(0.0, 100.0);
            path.SpawnX.Should().BeInRange(0.0, 100.0);
        }
    }
}
