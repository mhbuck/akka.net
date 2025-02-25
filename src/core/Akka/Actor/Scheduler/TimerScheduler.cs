﻿//-----------------------------------------------------------------------
// <copyright file="TimerScheduler.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Event;
using Akka.Util.Internal;
using System;
using System.Collections.Generic;

namespace Akka.Actor.Scheduler
{
    /// <summary>
    /// Support for scheduled "Self" messages in an actor.
    ///
    /// Timers are bound to the lifecycle of the actor that owns it,
    /// and thus are cancelled automatically when it is restarted or stopped.
    ///
    /// <see cref="ITimerScheduler"/> is not thread-safe, i.e.it must only be used within
    /// the actor that owns it.
    /// </summary>
    internal class TimerScheduler : ITimerScheduler
    {
        private class Timer
        {
            public object Key { get; }
            public object Msg { get; }
            public bool Repeat { get; }
            public int Generation { get; }
            public ICancelable Task { get; }

            public Timer(object key, object msg, bool repeat, int generation, ICancelable task)
            {
                Key = key;
                Msg = msg;
                Repeat = repeat;
                Generation = generation;
                Task = task;
            }
        }

        public interface ITimerMsg : IWrappedMessage, INoSerializationVerificationNeeded
        {
            object Key { get; }
            int Generation { get; }
            TimerScheduler Owner { get; }
        }

        private class TimerMsg : ITimerMsg
        {
            public object Key { get; }
            public int Generation { get; }
            public TimerScheduler Owner { get; }

            public TimerMsg(object key, int generation, TimerScheduler owner, object message)
            {
                Key = key;
                Generation = generation;
                Owner = owner;
                Message = message;
            }
            
            public object Message { get; }

            public override string ToString()
            {
                return $"TimerMsg(key={Key}, generation={Generation}, owner={Owner}, message={Message})";
            }
        }

        private class TimerMsgNotInfluenceReceiveTimeout : TimerMsg, INotInfluenceReceiveTimeout
        {
            public TimerMsgNotInfluenceReceiveTimeout(object key, int generation, TimerScheduler owner, object message)
                : base(key, generation, owner, message)
            {
            }
        }

        private readonly IActorContext _ctx;
        private readonly Dictionary<object, Timer> _timers = new();
        private readonly AtomicCounter _timerGen = new(0);
        private readonly bool _logDebug;
        private readonly ILoggingAdapter _log;

        public TimerScheduler(IActorContext ctx)
        {
            _ctx = ctx;
            _log = _ctx.System.Log;
            _logDebug = ctx.System.Settings.DebugTimerScheduler;
        }

        /// <summary>
        /// Start a periodic timer that will send <paramref name="msg"/> to the "Self" actor at
        /// a fixed <paramref name="interval"/>.
        ///
        /// Each timer has a key and if a new timer with same key is started
        /// the previous is cancelled and it's guaranteed that a message from the
        /// previous timer is not received, even though it might already be enqueued
        /// in the mailbox when the new timer is started.
        /// </summary>
        /// <param name="key">Name of timer</param>
        /// <param name="msg">Message to schedule</param>
        /// <param name="interval">Interval</param>
        public void StartPeriodicTimer(object key, object msg, TimeSpan interval)
        {
            StartTimer(key, msg, interval, interval, true, ActorRefs.NoSender);
        }

        /// <summary>
        /// Start a periodic timer that will send <paramref name="msg"/> to the "Self" actor at
        /// a fixed <paramref name="interval"/>.
        ///
        /// Each timer has a key and if a new timer with same key is started
        /// the previous is cancelled and it's guaranteed that a message from the
        /// previous timer is not received, even though it might already be enqueued
        /// in the mailbox when the new timer is started.
        /// </summary>
        /// <param name="key">Name of timer</param>
        /// <param name="msg">Message to schedule</param>
        /// <param name="interval">Interval</param>
        /// <param name="sender">The sender override for the timer message</param>
        public void StartPeriodicTimer(object key, object msg, TimeSpan interval, IActorRef sender)
        {
            StartTimer(key, msg, interval, interval, true, sender);
        }

        /// <summary>
        /// Start a periodic timer that will send <paramref name="msg"/> to the "Self" actor at
        /// a fixed <paramref name="interval"/>.
        ///
        /// Each timer has a key and if a new timer with same key is started
        /// the previous is cancelled and it's guaranteed that a message from the
        /// previous timer is not received, even though it might already be enqueued
        /// in the mailbox when the new timer is started.
        /// </summary>
        /// <param name="key">Name of timer</param>
        /// <param name="msg">Message to schedule</param>
        /// <param name="initialDelay">Initial delay</param>
        /// <param name="interval">Interval</param>
        public void StartPeriodicTimer(object key, object msg, TimeSpan initialDelay, TimeSpan interval)
        {
            StartTimer(key, msg, interval, initialDelay, true, ActorRefs.NoSender);
        }

        /// <summary>
        /// Start a periodic timer that will send <paramref name="msg"/> to the "Self" actor at
        /// a fixed <paramref name="interval"/>.
        ///
        /// Each timer has a key and if a new timer with same key is started
        /// the previous is cancelled and it's guaranteed that a message from the
        /// previous timer is not received, even though it might already be enqueued
        /// in the mailbox when the new timer is started.
        /// </summary>
        /// <param name="key">Name of timer</param>
        /// <param name="msg">Message to schedule</param>
        /// <param name="initialDelay">Initial delay</param>
        /// <param name="interval">Interval</param>
        /// <param name="sender">The sender override for the timer message</param>
        public void StartPeriodicTimer(object key, object msg, TimeSpan initialDelay, TimeSpan interval, IActorRef sender)
        {
            StartTimer(key, msg, interval, initialDelay, true, sender);
        }

        /// <summary>
        /// Start a timer that will send <paramref name="msg"/> once to the "Self" actor after
        /// the given <paramref name="timeout"/>.
        ///
        /// Each timer has a key and if a new timer with same key is started
        /// the previous is cancelled and it's guaranteed that a message from the
        /// previous timer is not received, even though it might already be enqueued
        /// in the mailbox when the new timer is started.
        /// </summary>
        /// <param name="key">Name of timer</param>
        /// <param name="msg">Message to schedule</param>
        /// <param name="timeout">Interval</param>
        public void StartSingleTimer(object key, object msg, TimeSpan timeout)
        {
            StartTimer(key, msg, timeout, TimeSpan.Zero, false, ActorRefs.NoSender);
        }

        /// <summary>
        /// Start a timer that will send <paramref name="msg"/> once to the "Self" actor after
        /// the given <paramref name="timeout"/>.
        ///
        /// Each timer has a key and if a new timer with same key is started
        /// the previous is cancelled and it's guaranteed that a message from the
        /// previous timer is not received, even though it might already be enqueued
        /// in the mailbox when the new timer is started.
        /// </summary>
        /// <param name="key">Name of timer</param>
        /// <param name="msg">Message to schedule</param>
        /// <param name="timeout">Interval</param>
        /// <param name="sender">The sender override for the timer message</param>
        public void StartSingleTimer(object key, object msg, TimeSpan timeout, IActorRef sender)
        {
            StartTimer(key, msg, timeout, TimeSpan.Zero, false, sender);
        }

        /// <summary>
        /// Check if a timer with a given <paramref name="key"/> is active.
        /// </summary>
        /// <param name="key"></param>
        /// <returns>Name of timer</returns>
        public bool IsTimerActive(object key)
        {
            return _timers.ContainsKey(key);
        }

        /// <summary>
        /// Retrieves all current active timer keys
        /// </summary>
        public IReadOnlyCollection<object> ActiveTimers => _timers.Keys;
        
        /// <summary>
        /// Cancel a timer with a given <paramref name="key"/>.
        /// If canceling a timer that was already canceled, or key never was used to start a timer
        /// this operation will do nothing.
        ///
        /// It is guaranteed that a message from a canceled timer, including its previous incarnation
        /// for the same key, will not be received by the actor, even though the message might already
        /// be enqueued in the mailbox when cancel is called.
        /// </summary>
        /// <param name="key">Name of timer</param>
        public void Cancel(object key)
        {
            if (_timers.TryGetValue(key, out var timer))
                CancelTimer(timer);
        }

        /// <summary>
        /// Cancel all timers.
        /// </summary>
        public void CancelAll()
        {
            if(_logDebug)
                _log.Debug("Cancel all timers");
            foreach (var timer in _timers)
                timer.Value.Task.Cancel();
            _timers.Clear();
        }

        private void CancelTimer(Timer timer)
        {
            if(_logDebug)
                _log.Debug("Cancel timer [{0}] with generation [{1}]", timer.Key, timer.Generation);
            timer.Task.Cancel();
            _timers.Remove(timer.Key);
        }


        private void StartTimer(object key, object msg, TimeSpan timeout, TimeSpan initialDelay, bool repeat, IActorRef sender)
        {
            if (_timers.TryGetValue(key, out var timer))
                CancelTimer(timer);

            var nextGen = _timerGen.Next();

            ITimerMsg timerMsg;
            if (msg is INotInfluenceReceiveTimeout)
                timerMsg = new TimerMsgNotInfluenceReceiveTimeout(key, nextGen, this, msg);
            else
                timerMsg = new TimerMsg(key, nextGen, this, msg);

            ICancelable task;
            if (repeat)
                task = _ctx.System.Scheduler.ScheduleTellRepeatedlyCancelable(initialDelay, timeout, _ctx.Self, timerMsg, sender);
            else
                task = _ctx.System.Scheduler.ScheduleTellOnceCancelable(timeout, _ctx.Self, timerMsg, sender);

            var nextTimer = new Timer(key, msg, repeat, nextGen, task);
            
            if(_logDebug)
                _log.Debug("Start timer [{0}] with generation [{1}]", key, nextGen);
            _timers[key] = nextTimer;
        }

        public object InterceptTimerMsg(ILoggingAdapter log, ITimerMsg timerMsg)
        {
            if (!_timers.TryGetValue(timerMsg.Key, out var timer))
            {
                // it was from canceled timer that was already enqueued in mailbox
                if(_logDebug)
                    log.Debug("Received timer [{0}] that has been removed, discarding", timerMsg.Key);
                return null; // message should be ignored
            }
            if (!ReferenceEquals(timerMsg.Owner, this))
            {
                // after restart, it was from an old instance that was enqueued in mailbox before canceled
                if(_logDebug)
                    log.Debug("Received timer [{0}] from old restarted instance, discarding", timerMsg.Key);
                return null; // message should be ignored
            }

            // N.B. - repeating timers never change their generation, so this check always passes.
            // This means that, in theory, a repeating timer can queue up the same message many times
            // in the actor's mailbox (i.e. when actor is busy) and there's no means of de-duplicating it.
            if (timerMsg.Generation == timer.Generation)
            {
                // valid timer
                if (!timer.Repeat)
                    _timers.Remove(timer.Key);
                return timer.Msg;
            }

            // it was from an old timer that was enqueued in mailbox before canceled
            if(_logDebug)
                log.Debug(
                    "Received timer [{0}] from old generation [{1}], expected generation [{2}], discarding",
                    timerMsg.Key,
                    timerMsg.Generation,
                    timer.Generation);
            return null; // message should be ignored
        }
    }
}
