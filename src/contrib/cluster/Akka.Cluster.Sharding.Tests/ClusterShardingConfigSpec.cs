﻿//-----------------------------------------------------------------------
// <copyright file="ClusterShardingConfigSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Configuration;
using Akka.TestKit;
using Xunit;
using FluentAssertions;

namespace Akka.Cluster.Sharding.Tests
{
    public class ClusterShardingConfigSpec : AkkaSpec
    {
        public ClusterShardingConfigSpec() : base(GetConfig())
        {
        }

        public static Config GetConfig()
        {
            return ConfigurationFactory.ParseString(@"akka.actor.provider = cluster
                                                      akka.remote.dot-netty.tcp.port = 0");
        }

        [Fact]
        public void Should_cluster_sharding_settings_have_default_config()
        {
            ClusterSharding.Get(Sys);
            var config = Sys.Settings.Config.GetConfig("akka.cluster.sharding");

            var clusterShardingSettings = ClusterShardingSettings.Create(Sys);

            Assert.False(config.IsNullOrEmpty());
            Assert.Equal("sharding", config.GetString("guardian-name"));
            Assert.Equal(string.Empty, config.GetString("role"));
            Assert.False(config.GetBoolean("remember-entities"));
            Assert.Equal(TimeSpan.FromSeconds(5), config.GetTimeSpan("coordinator-failure-backoff"));
            Assert.Equal(TimeSpan.FromSeconds(2), config.GetTimeSpan("retry-interval"));
            Assert.Equal(100000, config.GetInt("buffer-size"));
            Assert.Equal(TimeSpan.FromSeconds(60), config.GetTimeSpan("handoff-timeout"));
            Assert.Equal(TimeSpan.FromSeconds(10), config.GetTimeSpan("shard-start-timeout"));
            Assert.Equal(TimeSpan.FromSeconds(10), config.GetTimeSpan("entity-restart-backoff"));
            Assert.Equal(TimeSpan.FromSeconds(10), config.GetTimeSpan("rebalance-interval"));
            Assert.Equal(string.Empty, config.GetString("journal-plugin-id"));
            Assert.Equal(string.Empty, config.GetString("snapshot-plugin-id"));
            Assert.Equal("persistence", config.GetString("state-store-mode"));
            Assert.Equal("ddata", config.GetString("remember-entities-store"));
            Assert.Equal(TimeSpan.FromSeconds(2), config.GetTimeSpan("waiting-for-state-timeout"));
            Assert.Equal(TimeSpan.FromSeconds(5), config.GetTimeSpan("updating-state-timeout"));
            Assert.Equal("akka.cluster.singleton", config.GetString("coordinator-singleton"));
            Assert.Equal(string.Empty, config.GetString("use-dispatcher"));

            Assert.Equal(1, config.GetInt("least-shard-allocation-strategy.rebalance-threshold"));
            Assert.Equal(3, config.GetInt("least-shard-allocation-strategy.max-simultaneous-rebalance"));

            Assert.Equal("all", config.GetString("entity-recovery-strategy"));
            Assert.Equal(TimeSpan.FromMilliseconds(100), config.GetTimeSpan("entity-recovery-constant-rate-strategy.frequency"));
            Assert.Equal(5, config.GetInt("entity-recovery-constant-rate-strategy.number-of-entities"));

            var singletonConfig = Sys.Settings.Config.GetConfig("akka.cluster.singleton");

            Assert.NotNull(singletonConfig);
            Assert.Equal("singleton", singletonConfig.GetString("singleton-name"));
            Assert.Equal(string.Empty, singletonConfig.GetString("role"));
            Assert.Equal(TimeSpan.FromSeconds(1), singletonConfig.GetTimeSpan("hand-over-retry-interval"));
            Assert.Equal(15, singletonConfig.GetInt("min-number-of-hand-over-retries"));
            
            // DData settings
            var minCap = config.GetInt("distributed-data.majority-min-cap");
            minCap.Should().Be(5);
            clusterShardingSettings.TuningParameters.CoordinatorStateReadMajorityPlus.Should().Be(5);
            clusterShardingSettings.TuningParameters.CoordinatorStateWriteMajorityPlus.Should().Be(3);
        }

        [Fact]
        public void ClusterSharding_replicator_settings_should_have_default_values()
        {
            ClusterSharding.Get(Sys);
            var clusterShardingSettings = ClusterShardingSettings.Create(Sys);
            var replicatorSettings = ClusterShardingGuardian.GetReplicatorSettings(clusterShardingSettings);
            
            replicatorSettings.Should().NotBeNull();
            replicatorSettings.Role.Should().BeNullOrEmpty();
            
            // only populated when remember-entities is enabled
            replicatorSettings.DurableKeys.Should().BeEmpty();
            replicatorSettings.MaxDeltaElements.Should().Be(5);
            replicatorSettings.PreferOldest.Should().BeTrue();
        }
    }
}
