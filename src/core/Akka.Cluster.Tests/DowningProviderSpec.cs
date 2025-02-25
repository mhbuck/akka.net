﻿//-----------------------------------------------------------------------
// <copyright file="DowningProviderSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading;
using Akka.Actor;
using Akka.Configuration;
using Akka.TestKit;
using Akka.TestKit.Xunit2.Attributes;
using Akka.Util;
using FluentAssertions;
using Xunit;

namespace Akka.Cluster.Tests
{
    internal class FailingDowningProvider : IDowningProvider
    {
        public FailingDowningProvider(ActorSystem system, Cluster cluster)
        {
        }

        public TimeSpan DownRemovalMargin { get; } = TimeSpan.FromSeconds(20);

        public Props DowningActorProps
        {
            get
            {
                throw new ConfigurationException("this provider never works");
            }
        }
    }

    internal class DummyDowningProvider : IDowningProvider
    {
        public readonly AtomicBoolean ActorPropsAccessed = new(false);
        public DummyDowningProvider(ActorSystem system, Cluster cluster)
        {
        }

        public TimeSpan DownRemovalMargin { get; } = TimeSpan.FromSeconds(20);

        public Props DowningActorProps
        {
            get
            {
                ActorPropsAccessed.Value = true;
                return null;
            }
        }
    }

    public class DowningProviderSpec : AkkaSpec
    {
        public readonly Config BaseConfig = ConfigurationFactory.ParseString(@"
          akka {
            loglevel = WARNING
            actor.provider = ""Akka.Cluster.ClusterActorRefProvider, Akka.Cluster""
            remote {
              dot-netty.tcp {
                hostname = ""127.0.0.1""
                port = 0
              }
            }
          }
        ");

        [Fact]
        public void Downing_provider_should_default_to_KeepMajority()
        {
            using (var system = ActorSystem.Create("default", BaseConfig))
            {
                Cluster.Get(system).DowningProvider.Should().BeOfType<Akka.Cluster.SBR.SplitBrainResolverProvider>();
            }
        }

        [Fact]
        public void Downing_provider_should_ignore_AutoDowning_if_auto_down_unreachable_after_is_configured()
        {
            var config = ConfigurationFactory.ParseString(@"
                akka.cluster.downing-provider-class = """"
                akka.cluster.auto-down-unreachable-after=18s");
            using (var system = ActorSystem.Create("auto-downing", config.WithFallback(BaseConfig)))
            {
                Cluster.Get(system).DowningProvider.Should().BeOfType<AutoDowning>();
            }
        }

        [Fact]
        public void Downing_provider_should_use_specified_downing_provider()
        {
            var config = ConfigurationFactory.ParseString(
                @"akka.cluster.downing-provider-class = ""Akka.Cluster.Tests.DummyDowningProvider, Akka.Cluster.Tests""");
            using (var system = ActorSystem.Create("auto-downing", config.WithFallback(BaseConfig)))
            {
                var downingProvider = Cluster.Get(system).DowningProvider;
                downingProvider.Should().BeOfType<DummyDowningProvider>();
                AwaitCondition(() =>
                    ((DummyDowningProvider)downingProvider).ActorPropsAccessed.Value,
                    TimeSpan.FromSeconds(3));
            }
        }

        [LocalFact(SkipLocal = "Racy on Azure DevOps")]
        public void Downing_provider_should_stop_the_cluster_if_the_downing_provider_throws_exception_in_props()
        {
            var config = ConfigurationFactory.ParseString(
                @"akka.cluster.downing-provider-class = ""Akka.Cluster.Tests.FailingDowningProvider, Akka.Cluster.Tests""");

            var system = ActorSystem.Create("auto-downing", config.WithFallback(BaseConfig));

            var cluster = Cluster.Get(system);
            cluster.Join(cluster.SelfAddress);

            AwaitCondition(() => cluster.IsTerminated, TimeSpan.FromSeconds(3));

            Shutdown(system);
        }
    }
}

