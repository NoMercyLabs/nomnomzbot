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
using System.Text.RegularExpressions;
using FluentAssertions;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating;

/// <summary>
/// S042c: the four save paths covered by hand (<c>CommandService</c>, <c>EventResponseService</c>,
/// <c>TimerManagementService</c>, pipeline action fields via <c>CommandConfigValidator</c>) plus Discord
/// (<c>DiscordNotificationConfigService</c>) were each validated by a person reading the code. This test
/// exists so the FIFTH one — a service nobody remembers to wire up — fails loud instead of shipping quietly
/// unvalidated, the way <c>ModerationController.SetShoutoutTemplate</c> did until this same slice fixed it.
/// <para>
/// <b>Enumeration is structural, not a hand list.</b> Every persisted entity property that holds
/// user-authored <c>{{helper}}</c> template text carries <see cref="TemplatedUserContentAttribute"/>
/// (Domain/Platform) — the same distinguishing signal a reviewer uses when deciding "is this a save path
/// or an execution-only read?" turned into a reflectable marker. Of the ~19 real <c>ITemplateResolver</c>
/// consumers, 15 are execution-only leaves (they RESOLVE already-saved text — command dispatch, event
/// dispatch, timer firing, Discord dispatch, webhook body rendering, TTS playback, etc.) and never touch
/// this guard at all: they don't persist anything, so no entity property of theirs carries the attribute.
/// The remaining ones are exactly the write paths, discovered here by:
/// </para>
/// <list type="number">
/// <item>Reflecting the Domain assembly for every property carrying <see cref="TemplatedUserContentAttribute"/>.</item>
/// <item>Reflecting <see cref="IApplicationDbContext"/> for the <c>DbSet&lt;TEntity&gt;</c> that persists
/// each such entity — if a marked entity has no matching <c>DbSet</c>, the guard FAILS LOUD naming the
/// entity rather than silently skipping it (it cannot classify what it cannot map to a table).</item>
/// <item>For each (DbSet, property) pair, scanning every <c>.cs</c> file under the Api and Infrastructure
/// source trees that references that DbSet AND assigns the marked property — i.e. every file structurally
/// touching that write path — and asserting the file also references <c>ITemplateHelperValidator</c>.</item>
/// </list>
/// <para>
/// <b>Honesty about the technique.</b> This is a text-pattern scan seeded by reflected data (property/DbSet
/// names come from the compiled assemblies, never hand-typed), not an AST/data-flow analysis (the project
/// bans Roslyn) — it can theoretically miss a validator call routed through an unrelated file. It cannot
/// under-report a genuinely new violation the way a hand list can, because the entity/property universe
/// itself is discovered by reflection, not maintained by a person. Six source areas are excluded by an
/// explicit, named list (owned by other in-flight agents at the time this guard was written; the pipeline
/// path already has its own dedicated guard, <c>PipelineTemplatedFieldValidationGuardTests</c>) — an
/// exclusion is a decision recorded in code, never a silent gap.
/// </para>
/// </summary>
public sealed class TemplatedUserContentSavePathGuardTests
{
    /// <summary>
    /// Source areas this guard does not scan, because another in-flight slice owns them (per this
    /// slice's brief) or they already have their own dedicated guard. Recorded explicitly — never a
    /// silent skip. Paths are relative to <c>server/src/NomNomzBot.Infrastructure</c> or
    /// <c>server/src/NomNomzBot.Api</c>.
    /// </summary>
    private static readonly string[] ExcludedPathSegments =
    [
        Path.Combine("Platform", "Pipeline"), // has its own guard: PipelineTemplatedFieldValidationGuardTests
        "Webhooks",
        Path.Combine("Content", "Widgets"),
        Path.Combine("Chat", "Kick"),
        "Import",
        "Music",
        "Analytics",
    ];

    [Fact]
    public void Every_TemplatedUserContent_property_write_path_references_the_helper_validator()
    {
        List<PropertyInfo> templatedProperties =
        [
            .. typeof(TemplatedUserContentAttribute)
                .Assembly.GetTypes()
                .Where(t => t is { IsClass: true, IsAbstract: false })
                .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                .Where(p => p.GetCustomAttribute<TemplatedUserContentAttribute>() is not null),
        ];

        templatedProperties
            .Should()
            .NotBeEmpty(
                "at least Command.TemplateResponse and the other four known save paths must carry the marker"
            );

        Dictionary<Type, string> dbSetNameByEntityType = typeof(IApplicationDbContext)
            .GetProperties()
            .Where(p =>
                p.PropertyType.IsGenericType
                && p.PropertyType.GetGenericTypeDefinition()
                    == typeof(Microsoft.EntityFrameworkCore.DbSet<>)
            )
            .ToDictionary(p => p.PropertyType.GetGenericArguments()[0], p => p.Name);

        string serverRoot = ServerRoot();
        string apiRoot = Path.Combine(serverRoot, "src", "NomNomzBot.Api");
        string infrastructureRoot = Path.Combine(serverRoot, "src", "NomNomzBot.Infrastructure");

        List<string> candidateFiles =
        [
            .. Directory
                .EnumerateFiles(apiRoot, "*.cs", SearchOption.AllDirectories)
                .Concat(
                    Directory.EnumerateFiles(
                        infrastructureRoot,
                        "*.cs",
                        SearchOption.AllDirectories
                    )
                )
                .Where(f =>
                    !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                )
                .Where(f =>
                    !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                )
                .Where(f =>
                    !ExcludedPathSegments.Any(segment =>
                        f.Contains(
                            $"{Path.DirectorySeparatorChar}{segment}{Path.DirectorySeparatorChar}"
                        )
                    )
                ),
        ];

        List<string> unclassifiable = [];
        List<string> unvalidated = [];

        foreach (
            IGrouping<Type, PropertyInfo> group in templatedProperties.GroupBy(p =>
                p.DeclaringType!
            )
        )
        {
            Type entityType = group.Key;

            if (!dbSetNameByEntityType.TryGetValue(entityType, out string? dbSetName))
            {
                unclassifiable.Add(
                    $"{entityType.FullName} carries [TemplatedUserContent] but has no matching "
                        + $"DbSet<{entityType.Name}> on IApplicationDbContext — the guard cannot map it "
                        + "to a write surface and refuses to pass it silently."
                );
                continue;
            }

            foreach (PropertyInfo property in group)
            {
                foreach (string file in candidateFiles)
                {
                    string text = File.ReadAllText(file);
                    bool touchesDbSet = text.Contains($".{dbSetName}.");
                    if (!touchesDbSet)
                        continue;

                    if (!AssignsEntityProperty(text, dbSetName, property.Name))
                        continue;

                    if (!text.Contains("ITemplateHelperValidator"))
                        unvalidated.Add(
                            $"{Path.GetRelativePath(serverRoot, file)} writes "
                                + $"{entityType.Name}.{property.Name} (via DbSet {dbSetName}) without "
                                + "referencing ITemplateHelperValidator"
                        );
                }
            }
        }

        unclassifiable
            .Should()
            .BeEmpty(
                "every [TemplatedUserContent] entity must be mappable to a DbSet — an unmappable entity "
                    + "is a guard defect, not something to skip"
            );

        unvalidated
            .Should()
            .BeEmpty(
                "every file that persists a [TemplatedUserContent] property must route it through "
                    + "ITemplateHelperValidator before saving"
            );
    }

    /// <summary>
    /// True when <paramref name="text"/> assigns <paramref name="propertyName"/> ON the entity itself —
    /// either the dotted mutation form (<c>command.TemplateResponse = ...</c>, the update-path shape) or
    /// the bare object-initializer form (<c>TemplateResponse = ...</c>) WHEN that assignment sits between
    /// the local variable's declaration and the <c>{dbSetName}.Add(variable)</c> call that persists it
    /// (the create-path shape). Anchoring on the real <c>Add(</c> call — rather than matching literal
    /// <c>new {EntityType}</c> text — survives target-typed <c>new()</c> and type-alias declarations
    /// (<c>DomainTimer timer = new()</c>) that a literal-type-name search would miss.
    /// <para>
    /// The proximity gate on the bare form exists because a save-path DTO can legitimately share the
    /// property name with the entity (e.g. <c>CreateCommandDto.TemplateResponse</c>) without itself being
    /// the entity write — without the gate, BundleExportService/BundleImportService (which only copy the
    /// field between DTOs, routing the actual save back through the already-validated
    /// <c>ICommandService.CreateAsync</c>) false-positive.
    /// </para>
    /// </summary>
    private static bool AssignsEntityProperty(string text, string dbSetName, string propertyName)
    {
        if (text.Contains($".{propertyName} ="))
            return true;

        string[] lines = text.Split('\n');
        Regex addCallPattern = new(@"\." + Regex.Escape(dbSetName) + @"\.Add(?:Range)?\(\s*(\w+)");
        Regex declarationPattern = new(@"\b(\w+)\s*=\s*new\s*[\w<>]*\s*\(");

        for (int addLine = 0; addLine < lines.Length; addLine++)
        {
            Match addMatch = addCallPattern.Match(lines[addLine]);
            if (!addMatch.Success)
                continue;

            string variableName = addMatch.Groups[1].Value;

            int declarationLine = -1;
            for (int i = addLine; i >= 0 && i >= addLine - 200; i--)
            {
                Match declMatch = declarationPattern.Match(lines[i]);
                if (declMatch.Success && declMatch.Groups[1].Value == variableName)
                {
                    declarationLine = i;
                    break;
                }
            }
            if (declarationLine < 0)
                continue;

            for (int i = declarationLine; i <= addLine; i++)
            {
                if (
                    lines[i].Contains($"{propertyName} =")
                    || lines[i].Contains($"{variableName}.{propertyName} =")
                )
                    return true;
            }
        }

        return false;
    }

    private static string ServerRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src")))
            directory = directory.Parent;

        Directory
            .Exists(Path.Combine(directory?.FullName ?? string.Empty, "src"))
            .Should()
            .BeTrue("the test must be able to find the server source tree to scan it");

        return directory!.FullName;
    }
}
