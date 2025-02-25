﻿//-----------------------------------------------------------------------
// <copyright file="OldCoordinatorStateMigrationEventAdapter.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Cluster.Sharding.Serialization;
using Akka.Persistence;
using Akka.Persistence.Journal;

namespace Akka.Cluster.Sharding
{
    /// <summary>
    /// Used for migrating from persistent state store mode to the new event sourced remember entities. No user API,
    /// used through configuration. See reference docs for details.
    /// </summary>
    internal sealed class OldCoordinatorStateMigrationEventAdapter : IEventAdapter
    {
        public string Manifest(object evt)
        {
            if (evt is IPersistentRepresentation p)
                return p.Manifest;
            
            return ClusterShardingMessageSerializer.GetManifest(evt);
        }

        public object ToJournal(object evt)
        {
            return evt;
        }

        public IEventSequence FromJournal(object evt, string manifest)
        {
            if (evt is ShardCoordinator.ShardHomeAllocated sha)
            {
                return new SingleEventSequence(sha.Shard);
            }
            return EmptyEventSequence.Instance;
        }
    }
}
