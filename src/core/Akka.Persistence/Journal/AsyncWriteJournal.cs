﻿//-----------------------------------------------------------------------
// <copyright file="AsyncWriteJournal.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Pattern;
using Akka.Configuration;

namespace Akka.Persistence.Journal
{
    /// <summary>
    /// Abstract journal, optimized for asynchronous, non-blocking writes.
    /// </summary>
    public abstract class AsyncWriteJournal : WriteJournalBase, IAsyncRecovery
    {
        protected readonly bool CanPublish;
        private readonly CircuitBreaker _breaker;
        private readonly ReplayFilterMode _replayFilterMode;
        private readonly bool _isReplayFilterEnabled;
        private readonly int _replayFilterWindowSize;
        private readonly int _replayFilterMaxOldWriters;
        private readonly bool _replayDebugEnabled;
        private readonly IActorRef _resequencer;

        private long _resequencerCounter = 1L;

        /// <summary>
        /// Initializes a new instance of the <see cref="AsyncWriteJournal"/> class.
        /// </summary>
        /// <exception cref="ArgumentException">
        /// This exception is thrown when the Persistence extension related to this journal has not been used in the current <see cref="ActorSystem"/> context.
        /// </exception>
        /// <exception cref="ConfigurationException">
        /// This exception is thrown when an invalid <c>replay-filter.mode</c> is read from the configuration.
        /// Acceptable <c>replay-filter.mode</c> values include: off | repair-by-discard-old | fail | warn
        /// </exception>
        protected AsyncWriteJournal()
        {
            var extension = Persistence.Instance.Apply(Context.System);
            if (extension == null)
            {
                throw new ArgumentException("Couldn't initialize SyncWriteJournal instance, because associated Persistence extension has not been used in current actor system context.");
            }

            CanPublish = extension.Settings.Internal.PublishPluginCommands;
            var config = extension.ConfigFor(Self);
            _breaker = new CircuitBreaker(
                Context.System.Scheduler,
                config.GetInt("circuit-breaker.max-failures", 0),
                config.GetTimeSpan("circuit-breaker.call-timeout", null),
                config.GetTimeSpan("circuit-breaker.reset-timeout", null));

            var replayFilterMode = config.GetString("replay-filter.mode", "").ToLowerInvariant();
            switch (replayFilterMode)
            {
                case "off":
                    _replayFilterMode = ReplayFilterMode.Disabled;
                    break;
                case "repair-by-discard-old":
                    _replayFilterMode = ReplayFilterMode.RepairByDiscardOld;
                    break;
                case "fail":
                    _replayFilterMode = ReplayFilterMode.Fail;
                    break;
                case "warn":
                    _replayFilterMode = ReplayFilterMode.Warn;
                    break;
                default:
                    throw new ConfigurationException($"Invalid replay-filter.mode [{replayFilterMode}], supported values [off, repair-by-discard-old, fail, warn]");
            }
            _isReplayFilterEnabled = _replayFilterMode != ReplayFilterMode.Disabled;
            _replayFilterWindowSize = config.GetInt("replay-filter.window-size", 0);
            _replayFilterMaxOldWriters = config.GetInt("replay-filter.max-old-writers", 0);
            _replayDebugEnabled = config.GetBoolean("replay-filter.debug", false);

            _resequencer = Context.System.ActorOf(Props.Create(() => new Resequencer()));
        }

        /// <inheritdoc/>
        public abstract Task ReplayMessagesAsync(IActorContext context, string persistenceId, long fromSequenceNr, long toSequenceNr, long max, Action<IPersistentRepresentation> recoveryCallback);

        /// <inheritdoc/>
        public abstract Task<long> ReadHighestSequenceNrAsync(string persistenceId, long fromSequenceNr);

        /// <summary>
        /// Plugin API: asynchronously writes a batch of persistent messages to the
        /// journal.
        /// 
        /// The batch is only for performance reasons, i.e. all messages don't have to be written
        /// atomically. Higher throughput can typically be achieved by using batch inserts of many
        /// records compared to inserting records one-by-one, but this aspect depends on the
        /// underlying data store and a journal implementation can implement it as efficient as
        /// possible. Journals should aim to persist events in-order for a given `persistenceId`
        /// as otherwise in case of a failure, the persistent state may be end up being inconsistent.
        /// 
        /// Each <see cref="AtomicWrite"/> message contains the single <see cref="Persistent"/>
        /// that corresponds to the event that was passed to the 
        /// <see cref="Eventsourced.Persist{TEvent}(TEvent,Action{TEvent})"/> method of the
        /// <see cref="PersistentActor" />, or it contains several <see cref="Persistent"/>
        /// that correspond to the events that were passed to the
        /// <see cref="Eventsourced.PersistAll{TEvent}(IEnumerable{TEvent},Action{TEvent})"/>
        /// method of the <see cref="PersistentActor"/>. All <see cref="Persistent"/> of the
        /// <see cref="AtomicWrite"/> must be written to the data store atomically, i.e. all or none must
        /// be stored. If the journal (data store) cannot support atomic writes of multiple
        /// events it should reject such writes with a <see cref="NotSupportedException"/>
        /// describing the issue. This limitation should also be documented by the journal plugin.
        /// 
        /// If there are failures when storing any of the messages in the batch the returned
        /// <see cref="Task"/> must be completed with failure. The <see cref="Task"/> must only be completed with
        /// success when all messages in the batch have been confirmed to be stored successfully,
        /// i.e. they will be readable, and visible, in a subsequent replay. If there is
        /// uncertainty about if the messages were stored or not the <see cref="Task"/> must be completed
        /// with failure.
        /// 
        /// Data store connection problems must be signaled by completing the <see cref="Task"/> with
        /// failure.
        /// 
        /// The journal can also signal that it rejects individual messages (<see cref="AtomicWrite"/>) by
        /// the returned <see cref="Task"/>. It is possible but not mandatory to reduce
        /// number of allocations by returning null for the happy path,
        /// i.e. when no messages are rejected. Otherwise the returned list must have as many elements
        /// as the input <paramref name="messages"/>. Each result element signals if the corresponding
        /// <see cref="AtomicWrite"/> is rejected or not, with an exception describing the problem. Rejecting
        /// a message means it was not stored, i.e. it must not be included in a later replay.
        /// Rejecting a message is typically done before attempting to store it, e.g. because of
        /// serialization error.
        /// 
        /// Data store connection problems must not be signaled as rejections.
        /// 
        /// It is possible but not mandatory to reduce number of allocations by returning
        /// null for the happy path, i.e. when no messages are rejected.
        /// 
        /// Calls to this method are serialized by the enclosing journal actor. If you spawn
        /// work in asynchronous tasks it is alright that they complete the futures in any order,
        /// but the actual writes for a specific persistenceId should be serialized to avoid
        /// issues such as events of a later write are visible to consumers (query side, or replay)
        /// before the events of an earlier write are visible.
        /// A <see cref="PersistentActor"/> will not send a new <see cref="WriteMessages"/> request before
        /// the previous one has been completed.
        /// 
        /// Please not that the <see cref="IPersistentRepresentation.Sender"/> of the contained
        /// <see cref="Persistent"/> objects has been nulled out (i.e. set to <see cref="ActorRefs.NoSender"/>
        /// in order to not use space in the journal for a sender reference that will likely be obsolete
        /// during replay.
        /// 
        /// Please also note that requests for the highest sequence number may be made concurrently
        /// to this call executing for the same `persistenceId`, in particular it is possible that
        /// a restarting actor tries to recover before its outstanding writes have completed.
        /// In the latter case it is highly desirable to defer reading the highest sequence number
        /// until all outstanding writes have completed, otherwise the <see cref="PersistentActor"/>
        /// may reuse sequence numbers.
        /// 
        /// This call is protected with a circuit-breaker.
        /// </summary>
        /// <param name="messages">TBD</param>
        /// <returns>TBD</returns>
        protected abstract Task<IImmutableList<Exception>> WriteMessagesAsync(IEnumerable<AtomicWrite> messages);

        /// <summary>
        /// Asynchronously deletes all persistent messages up to inclusive <paramref name="toSequenceNr"/>
        /// bound.
        /// </summary>
        /// <param name="persistenceId">TBD</param>
        /// <param name="toSequenceNr">TBD</param>
        /// <returns>TBD</returns>
        protected abstract Task DeleteMessagesToAsync(string persistenceId, long toSequenceNr);

        /// <summary>
        /// Plugin API: Allows plugin implementers to use f.PipeTo(Self)
        /// and handle additional messages for implementing advanced features
        /// </summary>
        /// <param name="message">TBD</param>
        /// <returns>TBD</returns>
        protected virtual bool ReceivePluginInternal(object message)
        {
            return false;
        }

        /// <inheritdoc/>
        protected sealed override bool Receive(object message)
        {
            return ReceiveWriteJournal(message) || ReceivePluginInternal(message);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        /// <returns>TBD</returns>
        protected bool ReceiveWriteJournal(object message)
        {
            switch (message)
            {
                case WriteMessages writeMessages:
                    HandleWriteMessages(writeMessages);
                    return true;
                case ReplayMessages replayMessages:
                    HandleReplayMessages(replayMessages);
                    return true;
                case DeleteMessagesTo deleteMessagesTo:
                    HandleDeleteMessagesTo(deleteMessagesTo);
                    return true;
                default:
                    return false;
            }
        }

        private void HandleDeleteMessagesTo(DeleteMessagesTo message)
        {
            var eventStream = Context.System.EventStream;
            var self = Context.Self;

            async Task ProcessDelete()
            {
                try
                {
                    await _breaker.WithCircuitBreaker((message, awj: this), state =>
                        state.awj.DeleteMessagesToAsync(state.message.PersistenceId, state.message.ToSequenceNr));

                    message.PersistentActor.Tell(new DeleteMessagesSuccess(message.ToSequenceNr), self);

                    if (CanPublish)
                    {
                        eventStream.Publish(message);
                    }
                }
                catch (Exception ex)
                {
                    message.PersistentActor.Tell(
                        new DeleteMessagesFailure(TryUnwrapException(ex), message.ToSequenceNr), self);
                }
            }

            // instead of ContinueWith
#pragma warning disable CS4014
            ProcessDelete();
#pragma warning restore CS4014
        }

        private void HandleReplayMessages(ReplayMessages message)
        {
            var replyTo = _isReplayFilterEnabled
                ? Context.ActorOf(ReplayFilter.Props(message.PersistentActor, _replayFilterMode, _replayFilterWindowSize,
                    _replayFilterMaxOldWriters, _replayDebugEnabled))
                : message.PersistentActor;

            var context = Context;
            var eventStream = Context.System.EventStream;

            var readHighestSequenceNrFrom = Math.Max(0L, message.FromSequenceNr - 1);

            async Task ExecuteHighestSequenceNr()
            {
                void CompleteHighSeqNo(long highSeqNo)
                {
                    replyTo.Tell(new RecoverySuccess(highSeqNo));

                    if (CanPublish)
                    {
                        eventStream.Publish(message);
                    }
                }
                
                try
                {
                    var highSequenceNr = await _breaker.WithCircuitBreaker((message, readHighestSequenceNrFrom, awj: this), state =>
                        state.awj.ReadHighestSequenceNrAsync(state.message.PersistenceId, state.readHighestSequenceNrFrom));
                    var toSequenceNr = Math.Min(message.ToSequenceNr, highSequenceNr);
                    if (toSequenceNr <= 0L || message.FromSequenceNr > toSequenceNr)
                    {
                        CompleteHighSeqNo(highSequenceNr);
                    }
                    else
                    {
                        // Send replayed messages and replay result to persistentActor directly. No need
                        // to resequence replayed messages relative to written and looped messages.
                        // not possible to use circuit breaker here
                        await ReplayMessagesAsync(context, message.PersistenceId, message.FromSequenceNr, toSequenceNr,
                            message.Max, p =>
                            {
                                if (!p.IsDeleted) // old records from pre 1.0.7 may still have the IsDeleted flag
                                {
                                    foreach (var adaptedRepresentation in AdaptFromJournal(p))
                                    {
                                        replyTo.Tell(new ReplayedMessage(adaptedRepresentation), ActorRefs.NoSender);
                                    }
                                }
                            });

                        CompleteHighSeqNo(highSequenceNr);
                    }
                }
                catch (OperationCanceledException cx)
                {
                    // operation failed because a CancellationToken was invoked
                    // wrap the original exception and throw it, with some additional callsite context
                    var newEx = new OperationCanceledException("ReplayMessagesAsync canceled, possibly due to timing out.", cx);
                    replyTo.Tell(new ReplayMessagesFailure(newEx));
                }
                catch (Exception ex)
                {
                    replyTo.Tell(new ReplayMessagesFailure(TryUnwrapException(ex)));
                }
            }
            
            // instead of ContinueWith
#pragma warning disable CS4014
            ExecuteHighestSequenceNr();
#pragma warning restore CS4014
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="e">TBD</param>
        /// <returns>TBD</returns>
        protected static Exception TryUnwrapException(Exception e)
        {
            if (e is not AggregateException aggregateException) return e;
            aggregateException = aggregateException.Flatten();
            return aggregateException.InnerExceptions.Count == 1 ? aggregateException.InnerExceptions[0] : e;
        }

        private void HandleWriteMessages(WriteMessages message)
        {
            var counter = _resequencerCounter;

            /*
             * Self MUST BE CLOSED OVER here, or the code below will be subject to race conditions which may result
             * in failure, as the `IActorContext` needed for resolving Context.Self will be done outside the current
             * execution context.
             */
            var self = Self;
            _resequencerCounter += message.Messages.Aggregate(1, (acc, m) => acc + m.Size);
            var atomicWriteCount = message.Messages.Count(x => x is AtomicWrite);
            
            // Using an async local function instead of ContinueWith
#pragma warning disable CS4014
            ExecuteBatch(message, atomicWriteCount, self, counter);
#pragma warning restore CS4014
        }

        private async Task ExecuteBatch(WriteMessages message, int atomicWriteCount, IActorRef self, long resequencerCounter)
        {
            try
            {
                var prepared = PreparePersistentBatch(message.Messages);
                // try in case AsyncWriteMessages throws
                try
                {
                    var writeResult =
                        await _breaker.WithCircuitBreaker((prepared, awj: this), state => state.awj.WriteMessagesAsync(state.prepared)).ConfigureAwait(false);

                    ProcessResults(writeResult, atomicWriteCount, message, _resequencer, resequencerCounter, self);
                }
                catch (Exception e) // this is the old writeMessagesAsyncException
                {
                    _resequencer.Tell(new Desequenced(new WriteMessagesFailed(e, atomicWriteCount), resequencerCounter, message.PersistentActor, self), self);
                    Resequence((x, _) => new WriteMessageFailure(x, e, message.ActorInstanceId), null, resequencerCounter, message, _resequencer, self);
                }
            }
            catch (Exception ex)
            {
                // exception from PreparePersistentBatch => rejected
                ProcessResults(Enumerable.Repeat(ex, atomicWriteCount).ToImmutableList(), atomicWriteCount, message, _resequencer, resequencerCounter, self);
            }
        }

        private void ProcessResults(IImmutableList<Exception> results, int atomicWriteCount, WriteMessages writeMessage, IActorRef resequencer,
            long resequencerCounter, IActorRef writeJournal)
        {
            // there should be no circumstances under which `writeResult` can be `null`
            if (results != null && results.Count != atomicWriteCount)
                throw new IllegalStateException($"AsyncWriteMessages return invalid number or results. " +
                                                $"Expected [{atomicWriteCount}], but got [{results.Count}].");

            resequencer.Tell(new Desequenced(WriteMessagesSuccessful.Instance, resequencerCounter, writeMessage.PersistentActor, writeJournal), writeJournal);
            Resequence((x, exception) => exception == null
                ? new WriteMessageSuccess(x, writeMessage.ActorInstanceId)
                : new WriteMessageRejected(x, exception, writeMessage.ActorInstanceId), results, resequencerCounter, writeMessage, resequencer, writeJournal);
        }
        
        private void Resequence(Func<IPersistentRepresentation, Exception, object> mapper,
            IImmutableList<Exception> results, long resequencerCounter, WriteMessages msg, IActorRef resequencer, IActorRef writeJournal)
        {
            var i = 0;
            var enumerator = results?.GetEnumerator();
            foreach (var resequencable in msg.Messages)
            {
                if (resequencable is AtomicWrite aw)
                {
                    Exception exception = null;
                    if (enumerator != null)
                    {
                        enumerator.MoveNext();
                        exception = enumerator.Current;
                    }

                    foreach (var p in (IEnumerable<IPersistentRepresentation>)aw.Payload)
                    {
                        resequencer.Tell(new Desequenced(mapper(p, exception), resequencerCounter + i + 1, msg.PersistentActor, p.Sender), writeJournal);
                        i++;
                    }
                }
                else
                {
                    var loopMsg = new LoopMessageSuccess(resequencable.Payload, msg.ActorInstanceId);
                    resequencer.Tell(new Desequenced(loopMsg, resequencerCounter + i + 1, msg.PersistentActor, resequencable.Sender), writeJournal);
                    i++;
                }
            }
        }

        internal sealed class Desequenced
        {
            public Desequenced(object message, long sequenceNr, IActorRef target, IActorRef sender)
            {
                Message = message;
                SequenceNr = sequenceNr;
                Target = target;
                Sender = sender;
            }

            public object Message { get; }

            public long SequenceNr { get; }

            public IActorRef Target { get; }

            public IActorRef Sender { get; }
        }

        internal class Resequencer : ActorBase
        {
            private readonly IDictionary<long, Desequenced> _delayed = new Dictionary<long, Desequenced>();
            private long _delivered = 0L;

            protected override bool Receive(object message)
            {
                Desequenced d;
                if ((d = message as Desequenced) != null)
                {
                    do
                    {
                        d = Resequence(d);
                    } while (d != null);
                    return true;
                }
                return false;
            }

            private Desequenced Resequence(Desequenced desequenced)
            {
                if (desequenced.SequenceNr == _delivered + 1)
                {
                    _delivered = desequenced.SequenceNr;
                    desequenced.Target.Tell(desequenced.Message, desequenced.Sender);
                }
                else
                {
                    _delayed.Add(desequenced.SequenceNr, desequenced);
                }

                var delivered = _delivered + 1;
                if (_delayed.TryGetValue(delivered, out var d))
                {
                    _delayed.Remove(delivered);
                    return d;
                }

                return null;
            }
        }
    }
}
