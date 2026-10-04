// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NomNomzBot.Application;
using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Infrastructure.Commands.Builtins;

namespace NomNomzBot.Infrastructure.Tests.Commands.Builtins;

/// <summary>The built-in catalog is built from the DI registrations, so a registered <c>!banger</c> reaches every channel.</summary>
public sealed class BangerBuiltinRegistrationTests
{
    [Fact]
    public void The_banger_builtin_is_registered_under_the_key_banger()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Encryption:Key"] = Convert.ToBase64String(new byte[32]),
                    ["Jwt:Secret"] = "test-secret-key-at-least-32-characters-long!!",
                    ["ConnectionStrings:DefaultConnection"] =
                        "Host=localhost;Database=banger_registration_test;Username=test;Password=test",
                }
            )
            .Build();
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton(configuration);
        services.AddApplication();
        services.AddInfrastructure(configuration);

        List<Type?> builtinTypes =
        [
            .. services
                .Where(d => d.ServiceType == typeof(IBuiltinCommand))
                .Select(d => d.ImplementationType),
        ];

        builtinTypes.Should().Contain(typeof(BangerBuiltin));
        new BangerBuiltinKey().Value.Should().Be("banger");
    }

    private sealed class BangerBuiltinKey
    {
        public string Value => new BangerBuiltin(null!, null!, null!, null!).BuiltinKey;
    }
}
