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
using Microsoft.Extensions.DependencyInjection;

namespace NomNomzBot.Infrastructure.Platform;

/// <summary>
/// Hand-rolled reflection assembly scan (D5, backend-structure §4). Binds every
/// pluggable artifact by convention so no marker type is ever added to a DI list
/// by hand — drop a file, it is live next boot. No Scrutor, no new dependency.
/// </summary>
public static class AssemblyScanExtensions
{
    /// <summary>
    /// Registers every concrete class in <paramref name="assembly"/> assignable to the
    /// closed marker interface <typeparamref name="TMarker"/>, bound to <typeparamref name="TMarker"/>
    /// itself. Multiple impls register as a multi-binding (resolvable as <c>IEnumerable&lt;TMarker&gt;</c>).
    /// Use for collection contracts — pipeline actions/conditions, music providers.
    /// </summary>
    public static IServiceCollection AddImplementationsOf<TMarker>(
        this IServiceCollection services,
        Assembly assembly,
        ServiceLifetime lifetime
    )
    {
        foreach (Type implementation in ConcreteTypesAssignableTo(assembly, typeof(TMarker)))
        {
            services.Add(new ServiceDescriptor(typeof(TMarker), implementation, lifetime));
        }

        return services;
    }

    /// <summary>
    /// Registers every concrete <see cref="Microsoft.Extensions.Hosting.IHostedService"/> /
    /// <c>BackgroundService</c> worker in <paramref name="assembly"/> as a hosted service —
    /// EXCEPT types in <paramref name="excluded"/>, which require special construction
    /// (e.g. a singleton instance shared between a service interface and the hosted lifecycle).
    /// This is what wires the currently-unregistered <c>TokenRefreshService</c>.
    /// </summary>
    public static IServiceCollection AddHostedWorkers(
        this IServiceCollection services,
        Assembly assembly,
        params Type[] excluded
    )
    {
        Type hostedServiceType = typeof(Microsoft.Extensions.Hosting.IHostedService);

        foreach (Type worker in ConcreteTypesAssignableTo(assembly, hostedServiceType))
        {
            if (excluded.Contains(worker))
                continue;

            // AddHostedService<T> resolves T as a singleton and registers it for the
            // hosted lifecycle. The generic overload needs a compile-time type, so call
            // the closed AddSingleton<IHostedService>(factory) form by hand.
            services.AddSingleton(worker);
            services.Add(
                new ServiceDescriptor(
                    hostedServiceType,
                    sp => sp.GetRequiredService(worker),
                    ServiceLifetime.Singleton
                )
            );
        }

        return services;
    }

    /// <summary>
    /// Registers every concrete <c>I&lt;X&gt;Service</c> impl in <paramref name="assembly"/>
    /// bound to its matching <c>I&lt;X&gt;Service</c> interface, at the given lifetime.
    /// <para>
    /// Ambiguity guard (D5): if two concrete types implement the same service interface
    /// (a SaaS vs self-host variant), this THROWS at build time rather than silently
    /// picking one — that is the <c>DeploymentProfile</c>-decorator case and must be
    /// resolved explicitly. Interfaces listed in <paramref name="explicitlyRegistered"/>
    /// are skipped (already wired by hand for that reason or for special construction).
    /// </para>
    /// </summary>
    public static IServiceCollection AddServicesByConvention(
        this IServiceCollection services,
        Assembly assembly,
        ServiceLifetime lifetime,
        params Type[] explicitlyRegistered
    )
    {
        Dictionary<Type, Type> bindings = [];

        foreach (Type implementation in ConcreteTypes(assembly))
        {
            foreach (Type serviceInterface in ServiceInterfacesOf(implementation))
            {
                if (explicitlyRegistered.Contains(serviceInterface))
                    continue;

                if (bindings.TryGetValue(serviceInterface, out Type? existing))
                {
                    throw new InvalidOperationException(
                        $"Ambiguous DI binding: '{serviceInterface.Name}' is implemented by both "
                            + $"'{existing.Name}' and '{implementation.Name}'. Two impls of one service "
                            + "interface is the DeploymentProfile-decorator case (backend-structure §4) — "
                            + "register it explicitly, never let the scan pick one."
                    );
                }

                bindings[serviceInterface] = implementation;
            }
        }

        foreach ((Type serviceInterface, Type implementation) in bindings)
        {
            services.Add(new ServiceDescriptor(serviceInterface, implementation, lifetime));
        }

        return services;
    }

    /// <summary>
    /// Registers every concrete <c>I&lt;X&gt;Repository</c>-style repository
    /// (subclass of <typeparamref name="TRepositoryBase"/>) bound to its own concrete
    /// type, at the given lifetime. Repositories in this codebase are consumed by
    /// concrete type (no <c>I&lt;X&gt;Repository</c> interface), so they self-register.
    /// </summary>
    public static IServiceCollection AddRepositoriesByConvention<TRepositoryBase>(
        this IServiceCollection services,
        Assembly assembly,
        ServiceLifetime lifetime
    )
    {
        foreach (Type repository in ConcreteTypes(assembly))
        {
            if (DerivesFromOpenGeneric(repository, typeof(TRepositoryBase).GetGenericTypeDefinition()))
            {
                services.Add(new ServiceDescriptor(repository, repository, lifetime));
            }
        }

        return services;
    }

    /// <summary>
    /// Registers every concrete impl of the open generic handler interface
    /// <paramref name="openHandlerInterface"/> (e.g. <c>IEventHandler&lt;&gt;</c>) bound to
    /// EACH closed interface it implements, at the given lifetime. One handler may react
    /// to several events, so every closed interface is bound (multi-binding per event type).
    /// </summary>
    public static IServiceCollection AddOpenGenericHandlers(
        this IServiceCollection services,
        Assembly assembly,
        Type openHandlerInterface,
        ServiceLifetime lifetime
    )
    {
        foreach (Type handler in ConcreteTypes(assembly))
        {
            foreach (Type closedInterface in ClosedInterfacesOf(handler, openHandlerInterface))
            {
                services.Add(new ServiceDescriptor(closedInterface, handler, lifetime));
            }
        }

        return services;
    }

    // ── Reflection primitives ────────────────────────────────────────────────

    private static IEnumerable<Type> ConcreteTypes(Assembly assembly) =>
        assembly.GetTypes().Where(t => t is { IsAbstract: false, IsInterface: false, IsGenericTypeDefinition: false });

    private static IEnumerable<Type> ConcreteTypesAssignableTo(Assembly assembly, Type marker) =>
        ConcreteTypes(assembly).Where(marker.IsAssignableFrom);

    private static IEnumerable<Type> ServiceInterfacesOf(Type implementation) =>
        implementation
            .GetInterfaces()
            .Where(i =>
                !i.IsGenericType
                && i.Name.Length > "IService".Length
                && i.Name.StartsWith('I')
                && i.Name.EndsWith("Service", StringComparison.Ordinal)
                // Only this project's own contracts — never framework I…Service interfaces
                // (e.g. IHostedService), which are handled by their own registration paths.
                && i.Namespace?.StartsWith("NomNomzBot.", StringComparison.Ordinal) == true
            );

    private static IEnumerable<Type> ClosedInterfacesOf(Type implementation, Type openInterface) =>
        implementation
            .GetInterfaces()
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == openInterface);

    private static bool DerivesFromOpenGeneric(Type type, Type openGenericBase)
    {
        for (Type? current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.IsGenericType && current.GetGenericTypeDefinition() == openGenericBase)
                return true;
        }

        return false;
    }
}
