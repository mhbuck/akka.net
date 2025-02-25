﻿//-----------------------------------------------------------------------
// <copyright file="SomeMessage.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;

namespace Akka.Tests.Serialization
{
    public class SomeMessage
    {
        public IActorRef ActorRef { get; set; }
    }
}
