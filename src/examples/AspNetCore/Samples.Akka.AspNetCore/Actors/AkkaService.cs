﻿//-----------------------------------------------------------------------
// <copyright file="AkkaService.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.DependencyInjection;
using Akka.Routing;
using Microsoft.Extensions.Hosting;
using Samples.Akka.AspNetCore.Messages;
using Samples.Akka.AspNetCore.Services;

namespace Samples.Akka.AspNetCore.Actors
{
    /// <summary>
    /// Implements <see cref="IPublicHashingService"/>, which is the public interface used by ASP.NET Core.
    /// </summary>
    // <AkkaServiceSetup>
    public class AkkaService : IPublicHashingService, IHostedService
    {
        private ActorSystem _actorSystem;
        public IActorRef RouterActor { get; private set; }
        private readonly IServiceProvider _sp;

        public AkkaService(IServiceProvider sp)
        {
            _sp = sp;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            var hocon = ConfigurationFactory.ParseString(await File.ReadAllTextAsync("app.conf", cancellationToken));
            var bootstrap = BootstrapSetup.Create().WithConfig(hocon);
            var di = DependencyResolverSetup.Create(_sp);
            var actorSystemSetup = bootstrap.And(di);
            _actorSystem = ActorSystem.Create("AspNetDemo", actorSystemSetup);
            // </AkkaServiceSetup>

            // <ServiceProviderFor>
            // props created via IServiceProvider dependency injection
            var hasherProps = DependencyResolver.For(_actorSystem).Props<HasherActor>();
            RouterActor = _actorSystem.ActorOf(hasherProps.WithRouter(FromConfig.Instance), "hasher");
            // </ServiceProviderFor>

            await Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            // theoretically, shouldn't even need this - will be invoked automatically via CLR exit hook
            // but it's good practice to actually terminate IHostedServices when ASP.NET asks you to
            await CoordinatedShutdown.Get(_actorSystem).Run(CoordinatedShutdown.ClrExitReason.Instance);
        }

        public Task<HashReply> Hash(string input, CancellationToken token)
        {
            return RouterActor.Ask<HashReply>(input, token);
        }
    }
}
