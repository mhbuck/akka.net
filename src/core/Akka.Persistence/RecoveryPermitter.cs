﻿//-----------------------------------------------------------------------
// <copyright file="RecoveryPermitter.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Akka.Actor;
using Akka.Event;
using Akka.Pattern;
using Akka.Persistence.Internal;

namespace Akka.Persistence
{
    internal sealed class RequestRecoveryPermit
    {
        public static RequestRecoveryPermit Instance { get; } = new();
        private RequestRecoveryPermit() { }
    }

    internal sealed class RecoveryPermitGranted
    {
        public static RecoveryPermitGranted Instance { get; } = new();
        private RecoveryPermitGranted() { }
    }

    internal sealed class ReturnRecoveryPermit
    {
        public static ReturnRecoveryPermit Instance { get; } = new();
        private ReturnRecoveryPermit() { }
    }

    internal sealed class GetMaxPermits
    {
        public static GetMaxPermits Instance { get; } = new();
        private GetMaxPermits() { }
    }

    /// <summary>
    /// When starting many persistent actors at the same time the journal its data store is protected 
    /// from being overloaded by limiting number of recoveries that can be in progress at the same time.
    /// </summary>
    internal class RecoveryPermitter : UntypedActor
    {
        private readonly LinkedList<IActorRef> pending = new();
        private readonly ILoggingAdapter Log = Context.GetLogger();
        private int _usedPermits;
        private int _maxPendingStats;

        public static Props Props(int maxPermits) =>
            Actor.Props.Create(() => new RecoveryPermitter(maxPermits));

        public int MaxPermits { get; }

        public RecoveryPermitter(int maxPermits)
        {
            MaxPermits = maxPermits;
        }

        protected override void OnReceive(object message)
        {
            switch (message)
            {
                case RequestRecoveryPermit:
                    Context.Watch(Sender);
                    if (_usedPermits >= MaxPermits)
                    {
                        if (pending.Count == 0)
                            Log.Debug("Exceeded max-concurrent-recoveries [{0}]. First pending {1}", MaxPermits, Sender);
                        pending.AddLast(Sender);
                        _maxPendingStats = Math.Max(_maxPendingStats, pending.Count);
                    }
                    else
                    {
                        RecoveryPermitGranted(Sender);
                    }

                    break;
                
                case Akka.Persistence.ReturnRecoveryPermit:
                    ReturnRecoveryPermit(Sender);
                    break;
                
                case Terminated terminated when !pending.Remove(terminated.ActorRef):
                    // pre-mature termination should be rare
                    ReturnRecoveryPermit(terminated.ActorRef);
                    break;
                
                case GetMaxPermits:
                    Sender.Tell(MaxPermits);
                    break;
            }
        }

        private void ReturnRecoveryPermit(IActorRef actorRef)
        {
            _usedPermits--;
            Context.Unwatch(actorRef);

            if (_usedPermits < 0)
                throw new IllegalStateException("Permits must not be negative");

            if (pending.Count > 0)
            {
                var popRef = pending.Pop();
                RecoveryPermitGranted(popRef);
            }

            if (pending.Count != 0 || _maxPendingStats <= 0)
                return;

            Log.Debug("Drained pending recovery permit requests, max in progress was [{0}], still [{1}] in progress", _usedPermits + _maxPendingStats, _usedPermits);
            _maxPendingStats = 0;
        }

        private void RecoveryPermitGranted(IActorRef actorRef)
        {
            _usedPermits++;
            actorRef.Tell(Akka.Persistence.RecoveryPermitGranted.Instance);
        }
    }
}
