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

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating;

/// <summary>
/// A stand-in for an outside-world service whose interface is internal (NSubstitute cannot proxy it): every
/// call answers with the default of its return type, and a task or value task answers completed with the
/// default of its result. It holds no data, so a helper that reads from it renders its empty value.
/// </summary>
public class EmptyServiceFake : DispatchProxy
{
    /// <summary>A fake of <paramref name="serviceType"/>.</summary>
    public static object Create(Type serviceType)
    {
        MethodInfo create = typeof(DispatchProxy)
            .GetMethods()
            .Single(m => m.Name == nameof(Create) && m.GetGenericArguments().Length == 2);
        return create.MakeGenericMethod(serviceType, typeof(EmptyServiceFake)).Invoke(null, null)!;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        Type returnType = targetMethod!.ReturnType;
        if (returnType == typeof(void))
            return null;
        if (returnType == typeof(Task))
            return Task.CompletedTask;
        if (returnType == typeof(ValueTask))
            return default(ValueTask);
        if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
            return TaskOf(returnType.GetGenericArguments()[0]);
        if (
            returnType.IsGenericType
            && returnType.GetGenericTypeDefinition() == typeof(ValueTask<>)
        )
            return Activator.CreateInstance(
                returnType,
                DefaultOf(returnType.GetGenericArguments()[0])
            );
        return DefaultOf(returnType);
    }

    private static object TaskOf(Type result) =>
        typeof(Task)
            .GetMethod(nameof(Task.FromResult))!
            .MakeGenericMethod(result)
            .Invoke(null, [DefaultOf(result)])!;

    private static object? DefaultOf(Type type) =>
        type.IsValueType ? Activator.CreateInstance(type) : null;
}
