﻿//-----------------------------------------------------------------------
// <copyright file="ITimerScheduler.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace Akka.Actor
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
    public interface ITimerScheduler
    {
        /// <summary>
        /// Start a periodic timer that will send <paramref name="msg"/> to the "Self" actor at
        /// a fixed <paramref name="interval"/>.
        ///
        ///Each timer has a key and if a new timer with same key is started
        /// the previous is cancelled and it's guaranteed that a message from the
        /// previous timer is not received, even though it might already be enqueued
        /// in the mailbox when the new timer is started.
        /// </summary>
        /// <param name="key">Name of timer</param>
        /// <param name="msg">Message to schedule</param>
        /// <param name="interval">Interval</param>
        void StartPeriodicTimer(object key, object msg, TimeSpan interval);

        /// <summary>
        /// Start a periodic timer that will send <paramref name="msg"/> to the "Self" actor at
        /// a fixed <paramref name="interval"/>.
        ///
        ///Each timer has a key and if a new timer with same key is started
        /// the previous is cancelled and it's guaranteed that a message from the
        /// previous timer is not received, even though it might already be enqueued
        /// in the mailbox when the new timer is started.
        /// </summary>
        /// <param name="key">Name of timer</param>
        /// <param name="msg">Message to schedule</param>
        /// <param name="interval">Interval</param>
        /// <param name="sender">The sender override for the timer message</param>
        void StartPeriodicTimer(object key, object msg, TimeSpan interval, IActorRef sender);

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
        void StartPeriodicTimer(object key, object msg, TimeSpan initialDelay, TimeSpan interval);

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
        void StartPeriodicTimer(object key, object msg, TimeSpan initialDelay, TimeSpan interval, IActorRef sender);

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
        void StartSingleTimer(object key, object msg, TimeSpan timeout);

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
        void StartSingleTimer(object key, object msg, TimeSpan timeout, IActorRef sender);

        /// <summary>
        /// Check if a timer with a given <paramref name="key"/> is active.
        /// </summary>
        /// <param name="key"></param>
        /// <returns>Name of timer</returns>
        bool IsTimerActive(object key);

        /// <summary>
        /// Retrieves all current active timer keys
        /// </summary>
        public IReadOnlyCollection<object> ActiveTimers { get; }
        
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
        void Cancel(object key);

        /// <summary>
        /// Cancel all timers.
        /// </summary>
        void CancelAll();
    }
}
