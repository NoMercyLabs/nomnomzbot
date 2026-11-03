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

using NomNomzBot.Application.Abstractions.Auth;

namespace NomNomzBot.Infrastructure.Platform.Pipeline;

/// <summary>
/// Who a pipeline test run says triggered it, and the start variables the run begins with. A dry run keeps the
/// channel as the actor. A live run acts as the caller unless the request names another trigger user, so a
/// timeout or a gift lands on the person testing and never on a viewer by accident.
/// </summary>
internal sealed record TestRunTrigger(
    string UserId,
    string DisplayName,
    Dictionary<string, string> Variables
)
{
    private const string UserVariable = "user";
    private const string UserIdVariable = "user.id";
    private const string UserNameVariable = "user.name";

    public static TestRunTrigger Dry(
        IReadOnlyDictionary<string, string> requested,
        Guid broadcasterId
    ) => new(broadcasterId.ToString(), "Test Run", Copy(requested));

    public static TestRunTrigger Live(
        IReadOnlyDictionary<string, string> requested,
        ICurrentUserService caller
    )
    {
        Dictionary<string, string> variables = Copy(requested);
        string callerId = caller.UserId ?? string.Empty;
        string callerName = caller.Username ?? callerId;

        variables.TryAdd(UserIdVariable, callerId);
        variables.TryAdd(UserVariable, callerName);
        variables.TryAdd(UserNameVariable, variables[UserVariable]);

        return new(variables[UserIdVariable], variables[UserVariable], variables);
    }

    private static Dictionary<string, string> Copy(IReadOnlyDictionary<string, string> requested) =>
        new(requested, StringComparer.OrdinalIgnoreCase);
}
