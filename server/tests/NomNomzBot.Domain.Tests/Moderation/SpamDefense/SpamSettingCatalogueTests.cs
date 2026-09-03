// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Reflection;
using FluentAssertions;
using NomNomzBot.Domain.Moderation.SpamDefense;

namespace NomNomzBot.Domain.Tests.Moderation.SpamDefense;

/// <summary>
/// The configuration surface (spam-defense.md §6).
///
/// <para>The owner's headline requirement for this feature is that the operator has full control over
/// what is bannable and over every weight, and that every weight is explained. These tests are what
/// make that true over time rather than on the day it shipped: a knob added without an explanation
/// fails the build, and so does an explanation that says nothing about what moving it costs.</para>
/// </summary>
public class SpamSettingCatalogueTests
{
    private static PropertyInfo[] SettingProperties =>
        typeof(SpamDefenseSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance);

    [Fact]
    public void EverySetting_HasAPlainLanguageExplanation()
    {
        // Reflection-driven on purpose. A hand-listed check would pass forever while new weights
        // shipped undocumented beside it.
        List<string> undocumented = SettingProperties
            .Where(p => SpamSettingCatalogue.For(p.Name) is null)
            .Select(p => p.Name)
            .ToList();

        undocumented
            .Should()
            .BeEmpty(
                "every weight the operator can move must say what it does — add it to "
                    + "SpamSettingCatalogue.All"
            );
    }

    [Fact]
    public void TheCatalogueDescribesNothingThatIsNotASetting()
    {
        // The other direction: a descriptor for a property that no longer exists is copy describing a
        // control the operator will never find.
        HashSet<string> real = SettingProperties.Select(p => p.Name).ToHashSet();

        SpamSettingCatalogue.All.Select(d => d.Key).Should().OnlyContain(key => real.Contains(key));
    }

    [Fact]
    public void NoSettingIsDescribedTwice()
    {
        SpamSettingCatalogue
            .All.Select(d => d.Key)
            .Should()
            .OnlyHaveUniqueItems("two descriptions of one control is two chances to be wrong");
    }

    [Fact]
    public void EverySettingPointsAtThreeDistinctResourceKeys()
    {
        // The backend never holds user-facing prose — the product ships in English and Dutch. What it
        // holds is the key, and the keys are derived from the property name so a descriptor cannot
        // point at a resource nobody wrote.
        foreach (SpamSettingDescriptor descriptor in SpamSettingCatalogue.All)
        {
            descriptor.LabelKey.Should().StartWith("spam_setting_").And.EndWith("_label");
            descriptor.ExplanationKey.Should().EndWith("_explanation");
            descriptor.CostKey.Should().EndWith("_cost");
            descriptor.LabelKey.Should().NotBe(descriptor.ExplanationKey);
        }
    }

    [Fact]
    public void ResourceKeysAreSnakeCase_AndUniquePerSetting()
    {
        // Two settings resolving to one key would silently show one control's copy on another.
        SpamSettingCatalogue.All.Select(d => d.LabelKey).Should().OnlyHaveUniqueItems();

        SpamSettingCatalogue
            .For(nameof(SpamDefenseSettings.QualifyNoStandingShare))!
            .LabelKey.Should()
            .Be("spam_setting_qualify_no_standing_share_label");
    }

    [Fact]
    public void NoBackendStringIsUserFacingProse()
    {
        // The house rule, asserted rather than trusted: everything the catalogue carries is an
        // identifier or a number. If prose reappears here it will not be translated, and a Dutch
        // operator will read English.
        foreach (SpamSettingDescriptor descriptor in SpamSettingCatalogue.All)
        {
            descriptor.Key.Should().NotContain(" ");
            descriptor.Group.Should().NotContain(" ");
        }
    }

    [Fact]
    public void EveryNumericSetting_HasARange()
    {
        // Ranges are enforced server-side (§6.1). A numeric knob with no bounds is one 0 away from
        // disabling a protection by accident.
        foreach (PropertyInfo property in SettingProperties)
        {
            if (property.PropertyType != typeof(int) && property.PropertyType != typeof(double))
                continue;

            SpamSettingDescriptor? descriptor = SpamSettingCatalogue.For(property.Name);
            descriptor.Should().NotBeNull(property.Name);
            descriptor!.Minimum.Should().NotBeNull($"{property.Name} needs a lower bound");
            descriptor.Maximum.Should().NotBeNull($"{property.Name} needs an upper bound");
            descriptor.Maximum.Should().BeGreaterThan(descriptor.Minimum!.Value, property.Name);
        }
    }

    [Fact]
    public void EveryDefaultSitsInsideItsOwnRange()
    {
        // A shipped default outside its own bounds would be rejected the first time anyone saved the
        // form without touching it.
        SpamDefenseSettings defaults = new();

        foreach (PropertyInfo property in SettingProperties)
        {
            SpamSettingDescriptor? descriptor = SpamSettingCatalogue.For(property.Name);
            if (descriptor?.Minimum is null || descriptor.Maximum is null)
                continue;

            object? value = property.GetValue(defaults);
            double number = Convert.ToDouble(value);

            number
                .Should()
                .BeInRange(
                    descriptor.Minimum.Value,
                    descriptor.Maximum.Value,
                    $"{property.Name}'s default must be a value the form would accept"
                );
        }
    }

    // ---- The things that are deliberately not settings -------------------------------------------

    [Fact]
    public void TheFiveInvariants_AreDocumentedAsHavingNoSwitch()
    {
        // §6.1: a switch that turns off "never punish a regular" is a switch someone eventually flips
        // at 3am during a raid. They are listed so the operator can see the guarantees they get for
        // free, not hidden so nobody asks.
        SpamSettingCatalogue
            .Invariants.Should()
            .BeEquivalentTo(["SD0", "SD8", "SD9", "SD11", "SD12"]);

        foreach (string decision in SpamSettingCatalogue.Invariants)
            SpamSettingCatalogue
                .GuaranteeKey(decision)
                .Should()
                .Be($"spam_invariant_{decision.ToLowerInvariant()}_guarantee");
    }

    [Fact]
    public void NoInvariant_HasASettingThatCouldTurnItOff()
    {
        // The invariants must not be reachable through the settings record under any name. If one of
        // these words appears as a knob, someone has made a guarantee configurable.
        string[] forbidden =
        [
            "Immunity",
            "Immune",
            "PunishRegulars",
            "BypassStanding",
            "OverrideSD",
        ];
        IEnumerable<string> names = SettingProperties.Select(p => p.Name);

        names
            .Should()
            .NotContain(name =>
                forbidden.Any(f => name.Contains(f, StringComparison.OrdinalIgnoreCase))
            );
    }

    [Fact]
    public void DryRunShipsOn_SoANewChannelActsOnNobodyOnItsFirstDay()
    {
        // §6.2, and the whole safety story. The default must be the safe one.
        new SpamDefenseSettings()
            .DryRun.Should()
            .BeTrue();
    }

    [Fact]
    public void TheNonLatinGateShipsOff()
    {
        // SD2. Real viewers write in these alphabets every day; the gate exists only for a channel
        // under active attack. Whether the copy warns about that is asserted on the dashboard side,
        // where the copy lives.
        new SpamDefenseSettings()
            .NonLatinScriptGate.Should()
            .BeFalse();
    }

    [Fact]
    public void NetworkContributionShipsOff_BecauseItIsOptIn()
    {
        SpamDefenseSettings defaults = new();

        defaults.NetworkContribute.Should().BeFalse("SD3: contributing is opt-in");
        defaults.NetworkSubscribe.Should().BeTrue("subscribing is free and read-only");
    }

    [Fact]
    public void TheDequalifyShareIsBelowTheQualifyShare_SoTheHysteresisBandExists()
    {
        // If a future default edit crossed these, cohorts would flap between actioning and reversing.
        SpamDefenseSettings defaults = new();

        defaults.DequalifyNoStandingShare.Should().BeLessThan(defaults.QualifyNoStandingShare);
    }

    [Fact]
    public void TheDefaultsMatchWhatTheEngineActuallyUses()
    {
        // Two records could drift silently: the settings the operator edits, and the thresholds the
        // engine defaults to. If they disagree, the dashboard describes a machine we do not run.
        SpamDefenseSettings settings = new();
        CohortThresholds engine = new();
        ContentPolicy content = new();

        settings.QualifyNoStandingShare.Should().Be(engine.QualifyNoStandingShare);
        settings.DequalifyNoStandingShare.Should().Be(engine.DequalifyNoStandingShare);
        settings.MinimumCohortSize.Should().Be(engine.MinimumDistinctAccounts);
        settings.WindowSeconds.Should().Be((int)engine.Window.TotalSeconds);
        settings.MaxWindowSeconds.Should().Be((int)engine.MaximumWindow.TotalSeconds);
        settings.ActionDelaySeconds.Should().Be((int)engine.ActionDelay.TotalSeconds);
        settings.NearDuplicateSimilarity.Should().Be(content.NearDuplicateSimilarity);
        settings.MinimumSkeletonLength.Should().Be(content.MinimumSkeletonLength);
    }
}
