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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NomNomzBot.Api.Hubs;
using NomNomzBot.Api.Hubs.Broadcasters;
using NomNomzBot.Api.Hubs.Overlay;
using NomNomzBot.Application;
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Contracts.EventStore;
using NomNomzBot.Application.Notifications.Services;
using NomNomzBot.Application.Obs.Services;
using NomNomzBot.Application.Overlays.Services;
using NomNomzBot.Application.Sound.Services;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Infrastructure;

namespace NomNomzBot.Api.Tests.Hubs;

/// <summary>
/// Infrastructure registers silent stand-ins (they swallow their output) and the API host swaps in the
/// hub-backed adapters. If one swap is lost or runs before the stand-in, overlays, sounds, TTS, event-response
/// overlays and the external chat push go quiet with no error. This locks, per service, which class the API
/// composition resolves to.
/// </summary>
public sealed class HubSeamRegistrationTests
{
    // Service type -> the hub-backed class the API composition must resolve it to.
    private static readonly (Type Service, Type Real)[] Seams =
    [
        (typeof(ISoundClipOverlayNotifier), typeof(SoundClipOverlayNotifierAdapter)),
        (typeof(IWidgetEventNotifier), typeof(WidgetEventNotifierAdapter)),
        (typeof(IOverlayRetractionNotifier), typeof(OverlayRetractionNotifierAdapter)),
        (typeof(IEventResponseOverlayNotifier), typeof(EventResponseOverlayNotifierAdapter)),
        (typeof(IOverlayEventFeed), typeof(OverlayEventFeedAdapter)),
        (typeof(IExternalChatPush), typeof(ExternalChatPush)),
        (typeof(IRenderedAlertReplayer), typeof(RenderedAlertReplayer)),
        (typeof(IActionRequiredChangeNotifier), typeof(ActionRequiredChangeNotifier)),
        (typeof(IEventJournalChangeNotifier), typeof(EventJournalChangeNotifier)),
        (typeof(IOverlayPresenceRegistry), typeof(OverlayPresenceRegistry)),
        (typeof(IObsBridgePusher), typeof(ObsBridgePusher)),
    ];

    [Fact]
    public void Every_stand_in_resolves_to_its_hub_backed_class_in_the_API_composition()
    {
        ServiceCollection services = ApiComposition();
        using ServiceProvider provider = services.BuildServiceProvider();

        List<string> wrong = [];
        foreach ((Type service, Type real) in Seams)
        {
            ServiceDescriptor last = services.Last(d => d.ServiceType == service);
            Type actual = ResolvedType(last, provider);
            if (actual != real)
                wrong.Add($"{service.Name}: {actual.Name} (expected {real.Name})");
        }

        wrong.Should().BeEmpty("each stand-in must be replaced by the class that drives the hub");
    }

    [Fact]
    public void Each_replaced_service_keeps_exactly_one_registration_so_order_never_decides()
    {
        ServiceCollection services = ApiComposition();

        List<string> duplicated =
        [
            .. Seams
                .Select(s => (s.Service, Count: services.Count(d => d.ServiceType == s.Service)))
                .Where(s => s.Count != 1)
                .Select(s => $"{s.Service.Name} x{s.Count}"),
        ];

        duplicated
            .Should()
            .BeEmpty("a stand-in left beside its replacement wins again if the order ever changes");
    }

    [Fact]
    public void The_presence_registry_interface_and_the_hub_share_one_instance()
    {
        ServiceCollection services = ApiComposition();
        using ServiceProvider provider = services.BuildServiceProvider();

        IOverlayPresenceRegistry viaInterface =
            provider.GetRequiredService<IOverlayPresenceRegistry>();
        OverlayPresenceRegistry viaConcrete =
            provider.GetRequiredService<OverlayPresenceRegistry>();

        viaInterface
            .Should()
            .BeSameAs(
                viaConcrete,
                "the hub writes attachments to the concrete type, TTS and sound read them through the interface"
            );
    }

    [Fact]
    public void Program_registers_the_hub_seams_after_Infrastructure()
    {
        string source = File.ReadAllText(LocateProgramCs());

        int infrastructure = source.IndexOf("AddInfrastructure(", StringComparison.Ordinal);
        int seams = source.IndexOf("AddHubBackedSeams(", StringComparison.Ordinal);

        infrastructure.Should().BeGreaterThan(-1, "Program.cs must call AddInfrastructure");
        seams.Should().BeGreaterThan(-1, "Program.cs must call AddHubBackedSeams");
        seams
            .Should()
            .BeGreaterThan(
                infrastructure,
                "the hub-backed adapters must be registered after the Infrastructure stand-ins"
            );
    }

    private static ServiceCollection ApiComposition()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Encryption:Key"] = Convert.ToBase64String(new byte[32]),
                    ["Jwt:Secret"] = "test-secret-key-at-least-32-characters-long!!",
                    ["ConnectionStrings:DefaultConnection"] =
                        "Host=localhost;Database=hub_seam_test;Username=test;Password=test",
                }
            )
            .Build();
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton(configuration);
        services.AddApplication();
        services.AddInfrastructure(configuration);
        services.AddHubBackedSeams();
        return services;
    }

    private static Type ResolvedType(ServiceDescriptor descriptor, IServiceProvider provider)
    {
        if (descriptor.ImplementationType is not null)
            return descriptor.ImplementationType;
        if (descriptor.ImplementationInstance is not null)
            return descriptor.ImplementationInstance.GetType();
        return provider.GetRequiredService(descriptor.ServiceType).GetType();
    }

    private static string LocateProgramCs()
    {
        DirectoryInfo? dir = new FileInfo(
            Assembly.GetAssembly(typeof(Program))!.Location
        ).Directory;
        while (dir is not null)
        {
            string candidate = Path.Combine(dir.FullName, "src", "NomNomzBot.Api", "Program.cs");
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new FileNotFoundException(
            "Could not locate server/src/NomNomzBot.Api/Program.cs from the test assembly location."
        );
    }
}
