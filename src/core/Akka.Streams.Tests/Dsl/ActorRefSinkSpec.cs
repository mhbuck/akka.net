﻿//-----------------------------------------------------------------------
// <copyright file="ActorRefSinkSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Configuration;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.TestKit;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Tests.Dsl
{
    public class ActorRefSinkSpec : AkkaSpec
    {
        private sealed class Fw : ReceiveActor
        {
            public Fw(IActorRef aref)
            {
                ReceiveAny(aref.Forward);
            }
        }

        public ActorMaterializer Materializer { get; }

        public ActorRefSinkSpec(ITestOutputHelper output) : base(output, StreamTestDefaultMailbox.DefaultConfig)
        {
            Materializer = Sys.Materializer();
        }

        [Fact]
        public void ActorRefSink_should_send_elements_to_the_ActorRef()
        {
            Source.From(new[] { 1, 2, 3 }).RunWith(Sink.ActorRef<int>(TestActor, onCompleteMessage: "done", _ => ""), Materializer);

            ExpectMsg(1);
            ExpectMsg(2);
            ExpectMsg(3);
            ExpectMsg("done");
        }

        [Fact]
        public void ActorRefSink_should_cancel_a_stream_when_actor_terminates()
        {
            var fw = Sys.ActorOf(Props.Create(() => new Fw(TestActor)).WithDispatcher("akka.test.stream-dispatcher"));
            var publisher = this.SourceProbe<int>().To(Sink.ActorRef<int>(fw, onCompleteMessage: "done", _ => ""))
                    .Run(Materializer)
                    .SendNext(1)
                    .SendNext(2);

            ExpectMsg(1);
            ExpectMsg(2);
            Sys.Stop(fw);
            publisher.ExpectCancellation();
        }

        [Fact]
        public void ActorRefSink_should_sends_error_message_if_upstream_fails()
        {
            var actorProbe = CreateTestProbe();
            var probe = this.SourceProbe<string>().To(Sink.ActorRef<string>(actorProbe.Ref, "complete", _ => "failure"))
                .Run(Materializer);

            probe.SendError(new Exception("oh dear"));
            actorProbe.ExpectMsg("failure");
        }
    }
}
