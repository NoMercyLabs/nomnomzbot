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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NomNomzBot.Application;
using NomNomzBot.Infrastructure.Music;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// The host starts hosted services one after another in registration order. A chat worker that started
/// before the queue restore could take a <c>!sr</c> while the restore was still reading the database; that
/// request's save replaced the persisted rows, and the restore then brought back only the new request. The
/// whole queue was lost on a deploy. The restore therefore registers first and finishes before anything else
/// starts.
/// </summary>
public sealed class SongRequestQueueRestoreStartOrderTests
{
    [Fact]
    public void The_queue_restore_is_the_first_hosted_service_to_start_and_is_registered_once()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Encryption:Key"] = Convert.ToBase64String(new byte[32]),
                    ["Jwt:Secret"] = "test-secret-key-at-least-32-characters-long!!",
                    ["ConnectionStrings:DefaultConnection"] =
                        "Host=localhost;Database=restore_order_test;Username=test;Password=test",
                }
            )
            .Build();
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton(configuration);
        services.AddApplication();
        services.AddInfrastructure(configuration);

        List<ServiceDescriptor> hosted =
        [
            .. services.Where(d => d.ServiceType == typeof(IHostedService)),
        ];

        hosted[0].ImplementationType.Should().Be<SongRequestQueueRestoreHostedService>();
        services
            .Where(d =>
                d.ServiceType == typeof(SongRequestQueueRestoreHostedService)
                || d.ImplementationType == typeof(SongRequestQueueRestoreHostedService)
            )
            .Should()
            .ContainSingle("a second registration would restore the queue twice");
    }
}
