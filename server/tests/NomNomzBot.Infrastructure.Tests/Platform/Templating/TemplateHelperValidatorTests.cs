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

using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Platform.Templating;
using NomNomzBot.Infrastructure.Tests.ViewerData;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating;

/// <summary>
/// S042 save-time guard: proves an unknown/misspelled helper is REJECTED by name (with a nearby
/// suggestion when one exists), a context-inappropriate helper is REJECTED even though it is spelled
/// correctly, and a genuinely valid template is accepted — so the validator is not simply refusing
/// everything it sees.
/// </summary>
public sealed class TemplateHelperValidatorTests
{
    private readonly ITemplateHelperValidator _sut = new TemplateHelperValidator();

    [Fact]
    public void Misspelled_helper_is_rejected_naming_the_offending_key()
    {
        Result result = _sut.Validate("Hi {user.nmae}!", TemplateHelperContext.Command);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
        result.ErrorMessage.Should().Contain("user.nmae");
    }

    [Fact]
    public void Misspelled_helper_close_to_a_real_one_suggests_the_nearest_match()
    {
        Result result = _sut.Validate("Hi {user.nmae}!", TemplateHelperContext.Command);

        result.ErrorMessage.Should().Contain("user.name");
    }

    [Fact]
    public void CommandOnly_helper_is_rejected_on_an_event_response()
    {
        Result result = _sut.Validate(
            "Thanks for the raid, arg was {args.1}",
            TemplateHelperContext.EventResponse
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorMessage.Should().Contain("args.1");
    }

    [Fact]
    public void Valid_command_template_with_multiple_real_helpers_is_accepted()
    {
        Result result = _sut.Validate(
            "Thanks {user.name}, arg was {args.1} on {channel.display}",
            TemplateHelperContext.Command
        );

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Valid_event_response_template_is_accepted()
    {
        Result result = _sut.Validate(
            "Welcome {user.name} to {channel.display}, {presentTense} live!",
            TemplateHelperContext.EventResponse
        );

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Valid_timer_template_never_referencing_a_trigger_user_is_accepted()
    {
        Result result = _sut.Validate(
            "{channel.display} has been live for {stream.uptime}!",
            TemplateHelperContext.Timer
        );

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void UserOnly_helper_is_rejected_on_a_timer()
    {
        Result result = _sut.Validate("Welcome {user.name}!", TemplateHelperContext.Timer);

        result.IsFailure.Should().BeTrue();
        result.ErrorMessage.Should().Contain("user.name");
    }

    [Fact]
    public void Prefixed_families_resolve_correctly_args_transform_and_random()
    {
        Result result = _sut.Validate(
            "{args.2} {transform.upper:{user.name}} {random.number.100} {count.deaths}",
            TemplateHelperContext.Command
        );

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Null_or_empty_template_is_always_valid()
    {
        _sut.Validate(null, TemplateHelperContext.Command).IsSuccess.Should().BeTrue();
        _sut.Validate(string.Empty, TemplateHelperContext.Command).IsSuccess.Should().BeTrue();
    }

    // Regression for the exact operator-facing bug: the ad-break event-response preset suggested
    // {ad.duration}/{ad.automatic} (EventResponsePresetCatalog) and AdBreakBeganAlertHandler seeded
    // them at runtime, but neither was in TemplateHelperRegistry, so saving this EXACT template 400'd
    // with "not a recognized template helper" even though it would have resolved correctly.
    [Fact]
    public void Ad_break_preset_variables_are_accepted_on_an_event_response()
    {
        Result result = _sut.Validate(
            "Ads incoming for {ad.duration} (automatic: {ad.automatic})",
            TemplateHelperContext.EventResponse
        );

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Ad_break_preset_variables_are_rejected_outside_their_context()
    {
        Result result = _sut.Validate("Ads: {ad.duration}", TemplateHelperContext.Command);

        result.IsFailure.Should().BeTrue();
        result.ErrorMessage.Should().Contain("ad.duration");
    }

    // The general form of the ad-break bug: EventResponsePresetCatalog is the dashboard's own claim
    // about which variables each event type seeds (verified by its authors against each handler's
    // BuildVariables), so every one of those variables MUST validate in EventResponse context — a gap
    // here is the exact "not a recognized template helper" 400 a streamer would hit saving the preset
    // AS SHIPPED, with no edits. Runs every preset individually so a failure names the offending one.
    [Theory]
    [MemberData(nameof(AllPresetEventTypes))]
    public void Every_preset_catalog_variable_validates_in_its_own_shipped_template(
        string eventType
    )
    {
        EventResponsePresetDto preset = EventResponsePresetCatalog.Presets.Single(p =>
            p.EventType == eventType
        );
        string template = string.Join(" ", preset.Variables.Select(v => $"{{{v}}}"));

        Result result = _sut.Validate(template, TemplateHelperContext.EventResponse);

        result
            .IsSuccess.Should()
            .BeTrue(
                $"preset '{eventType}' advertises {template} but the validator rejects it: {result.ErrorMessage}"
            );
    }

    public static IEnumerable<object[]> AllPresetEventTypes() =>
        EventResponsePresetCatalog.Presets.Select(p => new object[] { p.EventType });

    [Fact]
    public void Key_valid_on_other_surfaces_is_rejected_naming_the_surfaces_and_the_reason()
    {
        Result result = _sut.Validate("Arg is {args.1}", TemplateHelperContext.Timer);

        result.IsFailure.Should().BeTrue();
        result
            .ErrorMessage.Should()
            .Contain(
                "'{args.1}' is not available in timers because it needs a chat command, and only commands and pipelines carry one."
            )
            .And.Contain("It works in: commands, pipelines");
        result.ErrorMessage.Should().NotContain("not a recognized template helper");
    }

    [Fact]
    public void Viewer_only_gap_names_the_opt_in_rule_and_the_surfaces_that_have_the_key()
    {
        Result result = _sut.Validate("{user.name}", TemplateHelperContext.Timer);

        result.IsFailure.Should().BeTrue();
        result
            .ErrorMessage.Should()
            .Contain(
                "'{user.name}' is not available in timers because it needs a trigger that carries a viewer or a chat message"
            )
            .And.Contain("It works in: commands, event responses");
    }

    [Fact]
    public void Typo_key_that_exists_nowhere_keeps_the_unrecognized_message()
    {
        Result result = _sut.Validate("{zzzz.nothing}", TemplateHelperContext.Timer);

        result
            .ErrorMessage.Should()
            .Contain("'{zzzz.nothing}' is not a recognized template helper");
    }

    [Fact]
    public void Readme_user_surface_line_matches_the_registry()
    {
        using ServiceProvider services = FullRegistryServices();
        ITemplateHelperRegistry registry = services.GetRequiredService<ITemplateHelperRegistry>();
        TemplateHelperEntry user = registry.All.Single(entry => entry.Key == "user");
        string readme = File.ReadAllText(LocateReadmeDocument()).Replace("\r\n", "\n");

        user.Contexts.Should().Contain(TemplateHelperContext.Viewer);
        readme
            .Should()
            .Contain(
                "`{user}`\nThe display name of the person who triggered it, as the platform shows it.\n"
                    + "Works in: commands, event responses, pipelines, Discord posts, webhooks, viewer text."
            );
    }

    [Fact]
    public void Readme_generator_rewrites_only_surface_lines_for_literal_family_and_grouped_helpers()
    {
        using ServiceProvider services = FullRegistryServices();
        ITemplateHelperRegistry registry = services.GetRequiredService<ITemplateHelperRegistry>();
        string source =
            "`{user}`\r\nA description.\r\nWorks in: stale.\r\n\r\n"
            + "For `{viewer.*}` and `{target.*}`.\nWorks in: stale.\n"
            + "`{args.N}` — arguments.\nWorks in: stale.\n"
            + "An inline Works in: phrase stays.\n";

        Result<string> generated = TemplateSurfaceAvailability.RenderReadmeWorksIn(
            source,
            registry
        );

        generated.IsSuccess.Should().BeTrue(generated.ErrorMessage);
        generated
            .Value.Should()
            .Be(
                "`{user}`\r\nA description.\r\nWorks in: commands, event responses, pipelines, Discord posts, webhooks, viewer text.\r\n\r\n"
                    + "For `{viewer.*}` and `{target.*}`.\nWorks in: event responses, pipelines.\n"
                    + "`{args.N}` — arguments.\nWorks in: commands, pipelines.\n"
                    + "An inline Works in: phrase stays.\n"
            );
    }

    [Fact]
    public void Readme_generator_reports_an_unknown_key_in_a_group()
    {
        using ServiceProvider services = FullRegistryServices();
        ITemplateHelperRegistry registry = services.GetRequiredService<ITemplateHelperRegistry>();
        string source = "`{user}` and `{missing.helper}`\nWorks in: stale.\n";

        Result<string> generated = TemplateSurfaceAvailability.RenderReadmeWorksIn(
            source,
            registry
        );

        generated.IsFailure.Should().BeTrue();
        generated.ErrorCode.Should().Be("TEMPLATE_README_INVALID");
        generated.ErrorMessage.Should().Contain("missing.helper");
    }

    [Fact]
    public void Surface_matrix_document_matches_the_registry()
    {
        using ServiceProvider services = FullRegistryServices();
        ITemplateHelperRegistry registry = services.GetRequiredService<ITemplateHelperRegistry>();

        string generated = TemplateSurfaceAvailability.RenderMarkdown(registry);

        string documentPath = LocateSurfacesDocument();
        string committed = File.Exists(documentPath)
            ? File.ReadAllText(documentPath).Replace("\r\n", "\n")
            : string.Empty;
        string scratchPath = Path.Combine(Path.GetTempPath(), "surfaces.generated.md");
        if (committed != generated)
            File.WriteAllText(scratchPath, generated);

        committed
            .Should()
            .Be(
                generated,
                $"docs/templates/surfaces.md is generated from the helper registry. The generated text is in '{scratchPath}'. "
                    + "Regenerate with: dotnet test server/tests/NomNomzBot.Infrastructure.Tests --filter Surface_matrix_document_matches_the_registry, "
                    + $"then copy '{scratchPath}' to '{documentPath}'"
            );

        string readmePath = LocateReadmeDocument();
        string readme = File.ReadAllText(readmePath);
        Result<string> generatedReadme = TemplateSurfaceAvailability.RenderReadmeWorksIn(
            readme,
            registry
        );
        generatedReadme.IsSuccess.Should().BeTrue(generatedReadme.ErrorMessage);
        string readmeScratchPath = Path.Combine(Path.GetTempPath(), "README.generated.md");
        if (readme != generatedReadme.Value)
            File.WriteAllText(readmeScratchPath, generatedReadme.Value);

        readme
            .Should()
            .Be(
                generatedReadme.Value,
                $"docs/templates/README.md Works in: lines are generated from the helper registry. The generated text is in '{readmeScratchPath}'. "
                    + "Regenerate with: dotnet test server/tests/NomNomzBot.Infrastructure.Tests --filter Surface_matrix_document_matches_the_registry, "
                    + $"then copy '{readmeScratchPath}' to '{readmePath}'"
            );
    }

    [Fact]
    public void Every_absent_cell_has_a_reason_that_the_document_legend_prints()
    {
        using ServiceProvider services = FullRegistryServices();
        ITemplateHelperRegistry registry = services.GetRequiredService<ITemplateHelperRegistry>();
        string document = TemplateSurfaceAvailability.RenderMarkdown(registry);

        int absent = 0;
        foreach (TemplateHelperEntry entry in registry.All)
        foreach (TemplateHelperContext surface in Enum.GetValues<TemplateHelperContext>())
        {
            if (entry.Contexts.Contains(surface))
                continue;
            absent++;
            TemplateSurfaceCell cell = TemplateSurfaceAvailability.Describe(entry, surface);
            cell.Available.Should().BeFalse($"{entry.Key} is not declared for {surface}");
            cell.Code.Should().NotBeNullOrWhiteSpace($"{entry.Key} on {surface} needs a code");
            cell.Reason.Should().NotBeNullOrWhiteSpace($"{entry.Key} on {surface} needs a reason");
            document.Should().Contain($"| `{cell.Code}` | {cell.Reason}");
        }

        absent.Should().BeGreaterThan(0);
    }

    private static string LocateSurfacesDocument()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string templates = Path.Combine(dir.FullName, "docs", "templates");
            if (Directory.Exists(templates))
                return Path.Combine(templates, "surfaces.md");
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException(
            $"Could not locate docs/templates above '{AppContext.BaseDirectory}'."
        );
    }

    private static string LocateReadmeDocument() =>
        Path.Combine(Path.GetDirectoryName(LocateSurfacesDocument())!, "README.md");

    /// <summary>The live registry: every real provider, with fakes only for the outside world.</summary>
    private static ServiceProvider FullRegistryServices()
    {
        ServiceCollection services = new();
        services.AddSingleton(Substitute.For<IChannelRegistry>());
        services.AddSingleton<IApplicationDbContext>(ViewerDataTestDbContext.New());
        services.AddSingleton<TimeProvider>(new FakeTimeProvider());
        services.AddSingleton(new Random(1));
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddTemplateHelpers(typeof(TemplateResolver).Assembly);

        HashSet<Type> known = [.. services.Select(d => d.ServiceType)];
        Assembly[] assemblies =
        [
            typeof(ITemplateResolver).Assembly,
            typeof(IChannelRegistry).Assembly,
            typeof(TemplateResolver).Assembly,
        ];
        foreach (
            Type type in assemblies
                .SelectMany(a => a.GetTypes())
                .Where(t => t.IsInterface && !t.IsGenericType && !known.Contains(t))
        )
            services.AddSingleton(
                type,
                _ => type.IsVisible ? Substitute.For([type], []) : EmptyServiceFake.Create(type)
            );
        return services.BuildServiceProvider();
    }
}
