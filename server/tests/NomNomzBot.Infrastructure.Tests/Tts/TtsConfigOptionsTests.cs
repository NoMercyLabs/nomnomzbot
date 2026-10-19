// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using NomNomzBot.Application.Tts.Dtos;
using NomNomzBot.Domain.Tts.Entities;

namespace NomNomzBot.Infrastructure.Tests.Tts;

/// <summary>
/// The choices the TTS settings page offers come from one place (<see cref="TtsConfigOptions"/>), and the
/// update DTO validates against that same place, so a choice the page lists is always one the API accepts.
/// </summary>
public sealed class TtsConfigOptionsTests
{
    private static T Attribute<T>(string property)
        where T : System.Attribute
    {
        PropertyInfo info = typeof(UpdateTtsConfigDto).GetProperty(property)!;
        return info.GetCustomAttribute<T>()!;
    }

    private static string[] Accepted(string property, IEnumerable<string> candidates)
    {
        Regex pattern = new(Attribute<RegularExpressionAttribute>(property).Pattern);
        return candidates.Where(c => pattern.IsMatch(c)).ToArray();
    }

    [Fact]
    public void EveryListedProviderIsAcceptedByTheUpdateDto()
    {
        Accepted(nameof(UpdateTtsConfigDto.DefaultProvider), TtsConfigOptions.Providers)
            .Should()
            .BeEquivalentTo(TtsConfigOptions.Providers);
        Accepted(nameof(UpdateTtsConfigDto.DefaultProvider), ["nope"]).Should().BeEmpty();
    }

    [Fact]
    public void EveryListedStandingIsAcceptedByTheUpdateDto()
    {
        Accepted(nameof(UpdateTtsConfigDto.MinPermission), TtsConfigOptions.MinPermissions)
            .Should()
            .BeEquivalentTo(TtsConfigOptions.MinPermissions);
    }

    [Fact]
    public void EveryListedModeIsAcceptedByTheUpdateDto()
    {
        Accepted(nameof(UpdateTtsConfigDto.Mode), TtsConfigOptions.Modes)
            .Should()
            .BeEquivalentTo(TtsConfigOptions.Modes);
    }

    [Fact]
    public void TheCharacterLimitIsTheRangeTheUpdateDtoEnforces()
    {
        RangeAttribute range = Attribute<RangeAttribute>(nameof(UpdateTtsConfigDto.MaxCharacters));

        range.Maximum.Should().Be(TtsConfigOptions.MaxCharactersLimit);
        new TtsConfig()
            .MaxCharacters.Should()
            .BeLessThanOrEqualTo(TtsConfigOptions.MaxCharactersLimit);
    }

    [Fact]
    public void TheDefaultsAreAmongTheListedChoices()
    {
        TtsConfig defaults = new();

        TtsConfigOptions.Providers.Should().Contain(defaults.DefaultProvider);
        TtsConfigOptions.Modes.Should().Contain(defaults.Mode);
        TtsConfigOptions.MinPermissions.Should().Contain(defaults.MinPermission);
    }
}
