﻿//-----------------------------------------------------------------------
// <copyright file="ShardRegionSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.Tools.Singleton;
using Akka.Configuration;
using Akka.TestKit;
using Akka.Util;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using static Akka.Cluster.ClusterEvent;
using static FluentAssertions.FluentActions;

namespace Akka.Cluster.Sharding.Tests
{
    public class ShardRegionSpec : AkkaSpec
    {
        private const string shardTypeName = "Caat";

        private const int numberOfShards = 3;

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
                => (int.Parse(entityId) % 10).ToString();
        }

        internal class EntityActor : ActorBase
        {
            protected override bool Receive(object message)
            {
                Sender.Tell(message);
                return true;
            }
        }

        private static Config SpecConfig =>
            ConfigurationFactory.ParseString(@"
                akka.loglevel = DEBUG
                akka.actor.provider = cluster
                akka.remote.dot-netty.tcp.port = 0
                akka.remote.log-remote-lifecycle-events = off

                akka.test.single-expect-default = 5 s
                akka.cluster.sharding.state-store-mode = ""ddata""
                akka.cluster.sharding.verbose-debug-logging = on
                akka.cluster.sharding.fail-on-invalid-entity-state-transition = on
                akka.cluster.sharding.distributed-data.durable.keys = []")
                .WithFallback(ClusterSingletonManager.DefaultConfig()
                .WithFallback(ClusterSharding.DefaultConfig()));


        private ActorSystem sysA;
        private ActorSystem sysB;

        private TestProbe p1;
        private TestProbe p2;

        private IActorRef region1;
        private IActorRef region2;


        public ShardRegionSpec(ITestOutputHelper helper) : base(SpecConfig, helper)
        {
            sysA = Sys;
            sysB = ActorSystem.Create(Sys.Name, Sys.Settings.Config);
            InitializeLogger(sysB, "[sysB]");

            p1 = CreateTestProbe(sysA);
            p2 = CreateTestProbe(sysB);

            region1 = StartShard(sysA);
            region2 = StartShard(sysB);
        }

        protected override void AfterAll()
        {
            if(sysA != null)
                Shutdown(sysA);
            if(sysB != null)
                Shutdown(sysB);
            base.AfterAll();
        }

        private IActorRef StartShard(ActorSystem sys)
        {
            return ClusterSharding.Get(sys).Start(
                shardTypeName,
                Props.Create(() => new EntityActor()),
                ClusterShardingSettings.Create(Sys).WithRememberEntities(true),
                new MessageExtractor());
        }

        private IActorRef StartProxy(ActorSystem sys)
        {
            return ClusterSharding.Get(sys).StartProxy(shardTypeName, null, new MessageExtractor());
        }

        [Fact(DisplayName = "ClusterSharding must clean up its internal regions cache when ShardRegion actor died")]
        public void ClusterShardingDeadShardRegionTest()
        {
            ClusterSharding_must_initialize_cluster_and_allocate_sharded_actors();
            
            var sharding = ClusterSharding.Get(sysA);

            IActorRef testRegion = null;
            // sanity check, region should be started and registered
            Invoking(() => testRegion = sharding.ShardRegion(shardTypeName))
                .Should().NotThrow();
            testRegion.Should().Be(region1);
            
            sysA.Stop(region1);
            // Shard should be unregistered after termination
            AwaitCondition(() => !sharding.ShardTypeNames.Contains(shardTypeName));

            testRegion = StartShard(sysA);
            // Shard should start up and be registered again
            AwaitCondition(() => sharding.ShardTypeNames.Contains(shardTypeName));
            
            // Original actor ref should not be the same as the old one
            testRegion.Should().NotBe(region1);
        }
        
        [Fact]
        public void ClusterSharding_must()
        {
            ClusterSharding_must_initialize_cluster_and_allocate_sharded_actors();
            ClusterSharding_must_only_deliver_buffered_RestartShard_to_the_local_region();
        }

        public void ClusterSharding_must_initialize_cluster_and_allocate_sharded_actors()
        {
            Cluster.Get(sysA).Join(Cluster.Get(sysA).SelfAddress); // coordinator on A
            AwaitAssert(() =>
            {
                Cluster.Get(sysA).SelfMember.Status.Should().Be(MemberStatus.Up);
            }, TimeSpan.FromSeconds(1));

            Cluster.Get(sysB).Join(Cluster.Get(sysA).SelfAddress);

            Within(TimeSpan.FromSeconds(10), () =>
            {
                AwaitAssert(() =>
                {
                    foreach (var s in ImmutableHashSet.Create(sysA, sysB))
                    {
                        Cluster.Get(s).SendCurrentClusterState(TestActor);
                        ExpectMsg<CurrentClusterState>().Members.Count.Should().Be(2);
                    }
                });
            });

            region1.Tell(1, p1.Ref);
            p1.ExpectMsg(1);

            region2.Tell(2, p2.Ref);
            p2.ExpectMsg(2);

            region2.Tell(3, p2.Ref);
            p2.ExpectMsg(3);
        }

        public void ClusterSharding_must_only_deliver_buffered_RestartShard_to_the_local_region()
        {
            ImmutableHashSet<string> StatesFor(IActorRef region, TestProbe probe, int expect)
            {
                region.Tell(GetShardRegionState.Instance, probe.Ref);
                return probe
                  .ReceiveWhile(message =>
                  {
                      if (message is CurrentShardRegionState e)
                      {
                          e.Failed.Should().BeEmpty();
                          return e.Shards.Select(i => i.ShardId);
                      }
                      throw new InvalidOperationException();
                  }, msgs: expect).SelectMany(i => i).ToImmutableHashSet();
            }

            bool AwaitRebalance(IActorRef region, int msg, TestProbe probe)
            {
                region.Tell(msg, probe.Ref);
                var m = probe.ExpectMsg<int>(TimeSpan.FromSeconds(2));
                if (m == msg)
                    return true;
                else
                    return AwaitRebalance(region, msg, probe);
            }

            void Swap<T>(ref T v1, ref T v2)
            {
                T t = v1;
                v1 = v2;
                v2 = t;
            }

            var region1Shards = StatesFor(region1, p1, expect: 2);
            var region2Shards = StatesFor(region2, p2, expect: 1);

            // sometimes shards are distributed differently
            if (region1Shards.Count == 1)
            {
                Swap(ref region1Shards, ref region2Shards);
                Swap(ref region1, ref region2);
                Swap(ref p1, ref p2);
            }
            int shardIdToMove = int.Parse(region2Shards.First());

            region1Shards.Should().BeEquivalentTo(ImmutableHashSet.Create("1", "2", "3").Remove(shardIdToMove.ToString()));
            region2Shards.Should().BeEquivalentTo(shardIdToMove.ToString());
            var allShards = region1Shards.Union(region2Shards);

            Watch(region2);
            region2.Tell(PoisonPill.Instance);
            AwaitAssert(() =>
            {
                ExpectTerminated(region2);
            });

            // Difficult to raise the RestartShard in conjunction with the rebalance for mode=ddata
            AwaitAssert(() =>
            {
                AwaitRebalance(region1, shardIdToMove, p1).Should().BeTrue();
            });

            var rebalancedOnRegion1 = StatesFor(region1, p1, expect: numberOfShards);
            AwaitAssert(() =>
            {
                rebalancedOnRegion1.Count.Should().Be(numberOfShards);
            }, TimeSpan.FromSeconds(5));
            rebalancedOnRegion1.Should().BeEquivalentTo(allShards);
        }
    }
}
