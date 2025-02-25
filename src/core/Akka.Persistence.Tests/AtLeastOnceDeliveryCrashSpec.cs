﻿//-----------------------------------------------------------------------
// <copyright file="AtLeastOnceDeliveryCrashSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Event;
using Akka.TestKit;
using Akka.TestKit.Xunit2.Attributes;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Persistence.Tests
{
    public class AtLeastOnceDeliveryCrashSpec : AkkaSpec
    {

        #region internal test classes

        internal class StoppingStrategySupervisor : ActorBase
        {
            private readonly IActorRef _crashingActor;

            public StoppingStrategySupervisor(IActorRef testProbe)
            {
                _crashingActor = Context.ActorOf(Props.Create(() => new CrashingActor(testProbe)), "CrashingActor");
            }

            protected override bool Receive(object message)
            {
                _crashingActor.Forward(message);
                return true;
            }

            protected override SupervisorStrategy SupervisorStrategy()
            {
                return new OneForOneStrategy(10, TimeSpan.FromSeconds(10), reason =>
                {
                    if (reason is IllegalActorStateException) return Directive.Stop;
                    return Actor.SupervisorStrategy.DefaultDecider.Decide(reason);
                });
            }
        }

        internal class Message
        {
            public static readonly Message Instance = new();
            private Message() { }
        }

        internal class CrashMessage
        {
            public static readonly CrashMessage Instance = new();
            private CrashMessage() { }
        }

        internal class SendingMessage
        {
            public SendingMessage(long deliveryId, bool isRecovering)
            {
                IsRecovering = isRecovering;
                DeliveryId = deliveryId;
            }

            public long DeliveryId { get; private set; }
            public bool IsRecovering { get; private set; }
        }

        internal class CrashingActor : AtLeastOnceDeliveryActor
        {
            private readonly IActorRef _testProbe;
            private ILoggingAdapter _adapter;

            ILoggingAdapter Log { get { return _adapter ??= Context.GetLogger(); } }

            public CrashingActor(IActorRef testProbe)
            {
                _testProbe = testProbe;
            }

            public override string PersistenceId
            {
                get { return Self.Path.Name; }
            }

            protected override bool ReceiveRecover(object message)
            {
                if (message is Message) Send();
                else if (message is CrashMessage)
                {
                    Log.Debug("Crash it!");
                    throw new IllegalActorStateException("Intentionally crashed");
                }
                else
                {
                    Log.Debug("Recover message: {0}", message);
                }

                return true;
            }

            protected override bool ReceiveCommand(object message)
            {
                if (message is Message message1) Persist(message1, _ => Send());
                else if (message is CrashMessage crashMessage) Persist(crashMessage, _ => { });
                else return false;
                return true;
            }

            private void Send()
            {
                Deliver(_testProbe.Path, id => new SendingMessage(id, false));
            }
        }

        #endregion

        public AtLeastOnceDeliveryCrashSpec(ITestOutputHelper output)
            : base(PersistenceSpec.Configuration("AtLeastOnceDeliveryCrashSpec", serialization: "off"), output)
        {
        }

        [LocalFact(SkipLocal = "Racy on Azure DevOps")]
        public void AtLeastOnceDelivery_should_not_send_when_actor_crashes()
        {
            var testProbe = CreateTestProbe();
            var supervisor = Sys.ActorOf(Props.Create(() => new StoppingStrategySupervisor(testProbe.Ref)), "supervisor");

            supervisor.Tell(Message.Instance);
            testProbe.ExpectMsg<SendingMessage>();

            supervisor.Tell(CrashMessage.Instance);
            var deathProbe = CreateTestProbe();
            deathProbe.Watch(supervisor);
            Sys.Stop(supervisor);
            deathProbe.ExpectTerminated(supervisor);

            testProbe.ExpectNoMsg(TimeSpan.FromMilliseconds(250));
            Sys.ActorOf(Props.Create(() => new StoppingStrategySupervisor(testProbe.Ref)), "supervisor");
            testProbe.ExpectNoMsg(TimeSpan.FromSeconds(1));
        }
    }
}
