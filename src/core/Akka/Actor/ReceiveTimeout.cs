﻿//-----------------------------------------------------------------------
// <copyright file="ReceiveTimeout.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

namespace Akka.Actor
{

    /// <summary>
    /// This message is sent to an actor that has set a receive timeout, either by calling 
    /// <see cref="IActorContext.SetReceiveTimeout">Context.SetReceiveTimeout</see> or
    /// <see cref="ActorBase.SetReceiveTimeout">SetReceiveTimeout</see>
    /// and no message has been sent to the actor during the specified amount of time.
    /// </summary>
    public class ReceiveTimeout : IPossiblyHarmful
    {
        private ReceiveTimeout() { }

        /// <summary>
        /// Gets the <see cref="ReceiveTimeout"/> singleton instance.
        /// </summary>
        public static ReceiveTimeout Instance { get; } = new();
    }
}

