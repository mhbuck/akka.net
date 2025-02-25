﻿//-----------------------------------------------------------------------
// <copyright file="ClusterShardingLeaseSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.Tools.Singleton;
using Akka.Configuration;
using Akka.Coordination.Tests;
using Akka.TestKit;
using Akka.TestKit.TestActors;
using Akka.Util;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Cluster.Sharding.Tests
{
    public class ClusterShardingLeaseSpec : AkkaSpec
    {
        private sealed class MessageExtractor: IMessageExtractor
        {
            public string EntityId(object message)
                => message switch
                {
                    int i => i.ToString(),
                    _ => null
                };

            public object EntityMessage(object message)
                => message;

            public string ShardId(object message)
                => message switch
                {
                    int i => (i % 10).ToString(),
                    _ => null
                };

            public string ShardId(string entityId, object messageHint = null)
                => entityId;
        }

        public class LeaseFailed : Exception
        {
            public LeaseFailed(string message) : base(message)
            {
            }

            public LeaseFailed(string message, Exception innerEx)
                : base(message, innerEx)
            {
            }

            protected LeaseFailed(SerializationInfo info, StreamingContext context)
                : base(info, context)
            {
            }
        }

        private static Config SpecConfig =>
            ConfigurationFactory.ParseString(@"
                akka.loglevel = DEBUG
                akka.loggers = [Akka.Event.DefaultLogger]
                akka.actor.provider = ""cluster""
                akka.remote.dot-netty.tcp.port = 0
                akka.cluster.sharding {
                    use-lease = ""test-lease""
                    lease-retry-interval = 200ms
                    distributed-data.durable {
                        keys = []
                    }
                    verbose-debug-logging = on
                    fail-on-invalid-entity-state-transition = on
                }
                ")
                .WithFallback(ClusterSharding.DefaultConfig())
                .WithFallback(ClusterSingletonManager.DefaultConfig())
                .WithFallback(TestLease.Configuration);

        TimeSpan shortDuration = TimeSpan.FromMilliseconds(200);
        Cluster cluster;
        string leaseOwner;
        TestLeaseExt testLeaseExt;

        const string typeName = "echo";
        IActorRef region;

        public ClusterShardingLeaseSpec(ITestOutputHelper helper) : this(null, false, helper)
        {
        }

        protected ClusterShardingLeaseSpec(Config config, bool rememberEntities, ITestOutputHelper helper)
            : base(config?.WithFallback(SpecConfig) ?? SpecConfig, helper)
        {
            cluster = Cluster.Get(Sys);
            leaseOwner = cluster.SelfMember.Address.HostPort();
            testLeaseExt = TestLeaseExt.Get(Sys);

            cluster.Join(cluster.SelfAddress);
            AwaitAssert(() =>
            {
                cluster.SelfMember.Status.ShouldBe(MemberStatus.Up);
            });
            ClusterSharding.Get(Sys).Start(
              typeName: typeName,
              entityProps: SimpleEchoActor.Props(),
              settings: ClusterShardingSettings.Create(Sys).WithRememberEntities(rememberEntities),
              messageExtractor: new MessageExtractor());

            region = ClusterSharding.Get(Sys).ShardRegion(typeName);
        }


        private TestLease LeaseForShard(int shardId)
        {
            TestLease lease = null;
            AwaitAssert(() =>
            {
                lease = testLeaseExt.GetTestLease(LeaseNameFor(shardId));
            }, TimeSpan.FromSeconds(6));
            return lease;
        }

        private string LeaseNameFor(int shardId, string typeName = typeName) => $"{Sys.Name}-shard-{typeName}-{shardId}";

        [Fact]
        public void Cluster_sharding_with_lease_should_not_start_until_lease_is_acquired()
        {
            region.Tell(1);
            ExpectNoMsg(shortDuration);
            var testLease = LeaseForShard(1);
            testLease.InitialPromise.SetResult(true);
            ExpectMsg(1);
        }

        [Fact]
        public void Cluster_sharding_with_lease_should_retry_if_initial_acquire_is_false()
        {
            region.Tell(2);
            ExpectNoMsg(shortDuration);
            var testLease = LeaseForShard(2);
            testLease.InitialPromise.SetResult(false);
            ExpectNoMsg(shortDuration);
            testLease.SetNextAcquireResult(Task.FromResult(true));
            ExpectMsg(2);
        }

        [Fact]
        public void Cluster_sharding_with_lease_should_retry_if_initial_acquire_fails()
        {
            region.Tell(3);
            ExpectNoMsg(shortDuration);
            var testLease = LeaseForShard(3);
            testLease.InitialPromise.SetException(new LeaseFailed("oh no"));
            ExpectNoMsg(shortDuration);
            testLease.SetNextAcquireResult(Task.FromResult(true));
            ExpectMsg(3);
        }

        [Fact]
        public void Cluster_sharding_with_lease_should_recover_if_lease_lost()
        {
            region.Tell(4);
            ExpectNoMsg(shortDuration);
            var testLease = LeaseForShard(4);
            testLease.InitialPromise.SetResult(true);
            ExpectMsg(4);
            testLease.GetCurrentCallback()(new LeaseFailed("oh dear"));
            AwaitAssert(() =>
            {
                region.Tell(4);
                ExpectMsg(4);
            }, TimeSpan.FromSeconds(10));
        }

        [Fact]
        public void Cluster_sharding_with_lease_should_release_lease_when_shard_stopped()
        {
            region.Tell(5);
            ExpectNoMsg(shortDuration);
            var testLease = LeaseForShard(5);
            testLease.InitialPromise.SetResult(true);
            testLease.Probe.ExpectMsg(new TestLease.AcquireReq(leaseOwner));
            ExpectMsg(5);

            region.Tell(new ShardCoordinator.HandOff("5"));
            testLease.Probe.ExpectMsg(new TestLease.ReleaseReq(leaseOwner));
        }
    }

    public class PersistenceClusterShardingLeaseSpec : ClusterShardingLeaseSpec
    {
        public PersistenceClusterShardingLeaseSpec(ITestOutputHelper helper)
            : base(ConfigurationFactory.ParseString(@"
                akka.cluster.sharding {
                    state-store-mode = persistence
                    journal-plugin-id = ""akka.persistence.journal.inmem""
                }
                "), true, helper)
        {
        }
    }

    public class DDataClusterShardingLeaseSpec : ClusterShardingLeaseSpec
    {
        public DDataClusterShardingLeaseSpec(ITestOutputHelper helper)
            : base(ConfigurationFactory.ParseString(@"
                akka.cluster.sharding {
                    state-store-mode = ddata
                }
                "), true, helper)
        {
        }
    }
}
