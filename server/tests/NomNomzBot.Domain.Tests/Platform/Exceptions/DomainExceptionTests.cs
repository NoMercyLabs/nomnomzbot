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
using NomNomzBot.Domain.Commands.Exceptions;
using NomNomzBot.Domain.Platform.Exceptions;

namespace NomNomzBot.Domain.Tests.Platform.Exceptions;

public class DomainExceptionTests
{
    [Fact]
    public void DomainException_WithMessage_SetsMessage()
    {
        DomainException ex = new("something went wrong");
        ex.Message.Should().Be("something went wrong");
    }

    [Fact]
    public void DomainException_WithMessageAndInner_SetsMessageAndInner()
    {
        InvalidOperationException inner = new("inner");
        DomainException ex = new("outer", inner);

        ex.Message.Should().Be("outer");
        ex.InnerException.Should().BeSameAs(inner);
    }

    [Fact]
    public void DomainException_IsExceptionSubclass()
    {
        DomainException ex = new("test");
        ex.Should().BeAssignableTo<Exception>();
    }
}

public class CooldownActiveExceptionTests
{
    [Fact]
    public void CooldownActiveException_SetsRemainingCooldown()
    {
        TimeSpan remaining = TimeSpan.FromSeconds(15);
        CooldownActiveException ex = new("!so", remaining);

        ex.RemainingCooldown.Should().Be(remaining);
    }

    [Fact]
    public void CooldownActiveException_MessageContainsCommandName()
    {
        CooldownActiveException ex = new("!uptime", TimeSpan.FromSeconds(30));
        ex.Message.Should().Contain("!uptime");
    }

    [Fact]
    public void CooldownActiveException_MessageContainsSeconds()
    {
        CooldownActiveException ex = new("!so", TimeSpan.FromSeconds(45));
        ex.Message.Should().Contain("45");
    }

    [Fact]
    public void CooldownActiveException_IsDomainException()
    {
        CooldownActiveException ex = new("!cmd", TimeSpan.FromSeconds(5));
        ex.Should().BeAssignableTo<DomainException>();
    }

    [Fact]
    public void CooldownActiveException_ZeroRemaining_StillCreates()
    {
        CooldownActiveException ex = new("!cmd", TimeSpan.Zero);
        ex.RemainingCooldown.Should().Be(TimeSpan.Zero);
    }
}

public class CommandNotFoundExceptionTests
{
    [Fact]
    public void CommandNotFoundException_MessageContainsCommandName()
    {
        CommandNotFoundException ex = new("!mystery");
        ex.Message.Should().Contain("!mystery");
    }

    [Fact]
    public void CommandNotFoundException_IsDomainException()
    {
        CommandNotFoundException ex = new("!cmd");
        ex.Should().BeAssignableTo<DomainException>();
    }
}

public class EntityNotFoundExceptionTests
{
    [Fact]
    public void EntityNotFoundException_SetsEntityTypeAndId()
    {
        EntityNotFoundException ex = new("Channel", "ch123");

        ex.EntityType.Should().Be("Channel");
        ex.EntityId.Should().Be("ch123");
    }

    [Fact]
    public void EntityNotFoundException_MessageContainsTypeAndId()
    {
        EntityNotFoundException ex = new("User", "user-456");

        ex.Message.Should().Contain("User");
        ex.Message.Should().Contain("user-456");
    }

    [Fact]
    public void EntityNotFoundException_WithInnerException_SetsInner()
    {
        Exception inner = new("db error");
        EntityNotFoundException ex = new("Command", "cmd-1", inner);

        ex.InnerException.Should().BeSameAs(inner);
        ex.EntityType.Should().Be("Command");
        ex.EntityId.Should().Be("cmd-1");
    }

    [Fact]
    public void EntityNotFoundException_IsDomainException()
    {
        EntityNotFoundException ex = new("Channel", "x");
        ex.Should().BeAssignableTo<DomainException>();
    }
}

public class InsufficientPermissionExceptionTests
{
    [Fact]
    public void InsufficientPermissionException_MessageContainsAction()
    {
        InsufficientPermissionException ex = new("delete channel");
        ex.Message.Should().Contain("delete channel");
    }

    [Fact]
    public void InsufficientPermissionException_IsDomainException()
    {
        InsufficientPermissionException ex = new("some action");
        ex.Should().BeAssignableTo<DomainException>();
    }
}
