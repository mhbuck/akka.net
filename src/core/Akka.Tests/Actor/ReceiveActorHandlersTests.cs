// -----------------------------------------------------------------------
//  <copyright file="ReceiveActorHandlersTests.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using Akka.Actor;
using Xunit;

namespace Akka.Tests.Actor;

public class ReceiveActorHandlersTests
{
    [Fact]
    public void Given_a_ReceiveAny_handler_has_been_added_When_adding_handler_Then_it_fails()
    {
        var handlers = new ReceiveActorHandlers();
        handlers.AddReceiveAnyHandler(_ => { });

        // As we have added a handler that matches everything, adding another handler is pointless, so
        // the builder should throw an exception.
        Assert.Throws<InvalidOperationException>(() =>
            handlers.AddTypedReceiveHandler(typeof(object), _ => true, _ => true));
    }

    [Fact]
    public void Given_a_TypedReceive_handler_has_been_added_When_adding_handler_Then_it_fails()
    {
        var handlers = new ReceiveActorHandlers();
        handlers.AddTypedReceiveHandler(typeof(object), null, _ => true);

        Assert.Throws<InvalidOperationException>(() =>
            handlers.AddTypedReceiveHandler(typeof(object), null, _ => true));
    }

    [Fact]
    public void Given_a_TypedReceive_handler_with_predicate_has_been_added_When_adding_handler_Then_it_succeeds()
    {
        var handlers = new ReceiveActorHandlers();
        handlers.AddTypedReceiveHandler(typeof(object), _ => true, _ => true);

        handlers.AddTypedReceiveHandler(typeof(object), null, _ => true);
    }

    [Fact]
    public void Given_a_TypedReceive_handler_for_different_type_When_adding_handler_Then_it_succeeds()
    {
        var handlers = new ReceiveActorHandlers();
        handlers.AddTypedReceiveHandler(typeof(string), _ => true, _ => true);

        handlers.AddTypedReceiveHandler(typeof(int), _ => true, _ => true);
    }

    [Fact]
    public void Given_a_ReceiveAny_handler_has_been_added_When_adding_any_handler_Then_it_fails()
    {
        var handlers = new ReceiveActorHandlers();
        handlers.AddReceiveAnyHandler(_ => { });

        Assert.Throws<InvalidOperationException>(() => handlers.AddReceiveAnyHandler(_ => { }));
    }

    [Fact]
    public void Given_a_TypedReceive_handler_has_been_added_When_adding_any_handler_Then_it_fails()
    {
        var handlers = new ReceiveActorHandlers();
        handlers.AddTypedReceiveHandler(typeof(object), _ => true, _ => true);

        Assert.Throws<InvalidOperationException>(() => handlers.AddReceiveAnyHandler(_ => { }));
    }

    [Fact]
    public void Given_a_TypedReceive_handler_with_predicate_has_been_added_When_adding_any_handler_Then_it_succeeds()
    {
        var handlers = new ReceiveActorHandlers();
        handlers.AddTypedReceiveHandler(typeof(object), _ => true, _ => true);

        handlers.AddReceiveAnyHandler(_ => { });
    }
}