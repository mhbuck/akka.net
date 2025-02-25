﻿//-----------------------------------------------------------------------
// <copyright file="Receive.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

namespace Akka.Actor
{
    /// <summary>
    ///     Delegate Receive
    /// </summary>
    /// <param name="message">The message.</param>
    public delegate bool Receive(object message);
}

