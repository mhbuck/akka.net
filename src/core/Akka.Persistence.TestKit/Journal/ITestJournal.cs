﻿//-----------------------------------------------------------------------
// <copyright file="ITestJournal.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

namespace Akka.Persistence.TestKit
{
    /// <summary>
    ///     <see cref="TestJournal"/> proxy object interface. Used to simplify communication with <see cref="TestJournal"/> actor instance.
    /// </summary>
    public interface ITestJournal
    {
        /// <summary>
        ///     List of interceptors to alter write behavior of proxied journal.
        /// </summary>
        JournalWriteBehavior OnWrite { get; }
        
        /// <summary>
        ///     List of interceptors to alter recovery behavior of proxied journal.
        /// </summary>
        JournalRecoveryBehavior OnRecovery { get; }
        
        JournalConnectionBehavior OnConnect { get; }
    }
}
