// -----------------------------------------------------------------------
//  <copyright file="ReceiveActorHandlers.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace Akka.Actor;
#nullable enable
internal class ReceiveActorHandlers
{
    public ReceiveActorHandlers()
    {
        TypedHandlers = new Dictionary<Type, ITypeHandler>();
        HandleAny = null;
    }

    public Dictionary<Type, ITypeHandler> TypedHandlers { get; }

    public Action<object>? HandleAny { get; private set; }

    public void AddGenericReceiveHandler<T>(Predicate<T>? shouldHandle, Func<T, bool> handler)
    {
        if (!TypedHandlers.TryGetValue(typeof(T), out var typeHandlerInterface))
        {
            typeHandlerInterface = new TypeHandler<T>();
            TypedHandlers[typeHandlerInterface.HandlesType] = typeHandlerInterface;
        }

        var typedHandler = (TypeHandler<T>)typeHandlerInterface;

        var predicateHandler = new PredicateHandler<T>() { Predicate = shouldHandle, Handler = handler };

        typedHandler.Handlers.Add(predicateHandler);
    }

    public void AddTypedReceiveHandler(Type messageType, Predicate<object>? shouldHandle, Func<object, bool> handler)
    {
        // Need to add cases here if more than one object type is passed in
        // More of the tests from match handler need to be replicated.

        if (!TypedHandlers.TryGetValue(messageType, out var typeHandlerInterface))
        {
            typeHandlerInterface = new TypeHandler<object>();
            TypedHandlers[messageType] = typeHandlerInterface;
        }

        var typedHandler = (TypeHandler<object>)typeHandlerInterface;

        // Have to use object here as dont have the generic type information
        var predicateHandler = new PredicateHandler<object>() { Predicate = shouldHandle, Handler = handler };

        typedHandler.Handlers.Add(predicateHandler);
    }

    public void AddReceiveAnyHandler(Action<object> handler)
    {
        if (HandleAny != null)
        {
            throw new InvalidOperationException(
                "A handler that catches all messages has been added. No handler can be added after that.");
        }

        HandleAny = handler;
    }
}

internal interface ITypeHandler
{
    Type HandlesType { get; }

    bool TryHandle(object message);
}

internal class TypeHandler<T> : ITypeHandler
{
    public TypeHandler()
    {
        HandlesType = typeof(T);
        Handlers = new List<PredicateHandler<T>>();
    }

    public Type HandlesType { get; }

    public List<PredicateHandler<T>> Handlers { get; }

    public bool TryHandle(object message)
    {
        var typedMessage = (T)message;
        foreach (var predicateHandler in Handlers)
        {
            if (predicateHandler.TryHandle(typedMessage))
            {
                return true;
            }
        }

        return false;
    }
}

internal class PredicateHandler<T>
{
    public Predicate<T>? Predicate { get; init; }
    public Func<T, bool> Handler { get; init; }

    public bool TryHandle(T typedMessage)
    {
        if (Predicate == null || Predicate(typedMessage))
        {
            return Handler(typedMessage);
        }

        return false;
    }
}