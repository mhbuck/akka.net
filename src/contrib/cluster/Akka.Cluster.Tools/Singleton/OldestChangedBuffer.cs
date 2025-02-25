﻿//-----------------------------------------------------------------------
// <copyright file="OldestChangedBuffer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;

namespace Akka.Cluster.Tools.Singleton
{
    /// <summary>
    /// Notifications of member events that track oldest member is tunneled
    /// via this actor (child of ClusterSingletonManager) to be able to deliver
    /// one change at a time. Avoiding simultaneous changes simplifies
    /// the process in ClusterSingletonManager. ClusterSingletonManager requests
    /// next event with <see cref="GetNext"/> when it is ready for it. Only one outstanding
    /// <see cref="GetNext"/> request is allowed. Incoming events are buffered and delivered
    /// upon <see cref="GetNext"/> request.
    /// </summary>
    internal sealed class OldestChangedBuffer : UntypedActor
    {
        #region Internal messages

        /// <summary>
        /// Request to deliver one more event.
        /// </summary>
        [Serializable]
        public sealed class GetNext
        {
            /// <summary>
            /// TBD
            /// </summary>
            public static GetNext Instance { get; } = new();
            private GetNext() { }
        }

        /// <summary>
        /// TBD
        /// </summary>
        [Serializable]
        public sealed class InitialOldestState
        {
            /// <summary>
            /// The first event, corresponding to CurrentClusterState.
            /// </summary>
            public ImmutableList<UniqueAddress> Oldest { get; }

            /// <summary>
            /// TBD
            /// </summary>
            public bool SafeToBeOldest { get; }

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="oldest">TBD</param>
            /// <param name="safeToBeOldest">TBD</param>
            public InitialOldestState(ImmutableList<UniqueAddress> oldest, bool safeToBeOldest)
            {
                Oldest = oldest;
                SafeToBeOldest = safeToBeOldest;
            }
        }

        /// <summary>
        /// Message propagated once the previous oldest member is exiting / downed / removed.
        /// </summary>
        [Serializable]
        public sealed class OldestChanged
        {
            /// <summary>
            /// The new "oldest" - this node will become the new singleton manager.
            /// </summary>
            /// <remarks>
            /// Can be <c>null</c> if we're the last node in the cluster.
            /// </remarks>
            public UniqueAddress? NewOldest { get; }

            /// <summary>
            /// The previous oldest - will be `null` if this is the first oldest.
            /// </summary>
            public UniqueAddress? PreviousOldest { get; }
            
            public OldestChanged(UniqueAddress? newOldest, UniqueAddress? previousOldest)
            {
                NewOldest = newOldest;
                PreviousOldest = previousOldest;
            }
        }

        #endregion

        private readonly IComparer<Member> _memberAgeComparer;
        private readonly CoordinatedShutdown _coordShutdown = CoordinatedShutdown.Get(Context.System);

        /// <summary>
        /// Creates a new instance of the <see cref="OldestChangedBuffer"/>.
        /// </summary>
        /// <param name="role">The role for which we're watching for membership changes.</param>
        /// <param name="considerAppVersion">Should cluster AppVersion be considered when sorting member age</param>
        public OldestChangedBuffer(string role, bool considerAppVersion)
        {
            _role = role;
            _memberAgeComparer = Member.AgeOrdering;
            _membersByAge = ImmutableSortedSet<Member>.Empty.WithComparer(_memberAgeComparer);
            
            SetupCoordinatedShutdown();
        }

        /// <summary>
        /// It's a delicate difference between <see cref="CoordinatedShutdown.PhaseClusterExiting"/> and <see cref="ClusterEvent.MemberExited"/>.
        ///
        /// MemberExited event is published immediately (leader may have performed that transition on other node),
        /// and that will trigger run of <see cref="CoordinatedShutdown"/>, while PhaseClusterExiting will happen later.
        /// Using PhaseClusterExiting in the singleton because the graceful shutdown of sharding region
        /// should preferably complete before stopping the singleton sharding coordinator on same node.
        /// </summary>
        private void SetupCoordinatedShutdown()
        {
            var self = Self;
            _coordShutdown.AddTask(CoordinatedShutdown.PhaseClusterExiting, "singleton-exiting-1", () =>
            {
                if (_cluster.IsTerminated || _cluster.SelfMember.Status == MemberStatus.Down)
                {
                    return Task.FromResult(Done.Instance);
                }
                else
                {
                    var timeout = _coordShutdown.Timeout(CoordinatedShutdown.PhaseClusterExiting);
                    return self.Ask(SelfExiting.Instance, timeout).ContinueWith(_ => Done.Instance);
                }
            });
        }

        private readonly string _role;
        private ImmutableSortedSet<Member> _membersByAge;
        private ImmutableQueue<object> _changes = ImmutableQueue<object>.Empty;

        private readonly Cluster _cluster = Cluster.Get(Context.System);

        private void TrackChanges(Action block)
        {
            var before = _membersByAge.FirstOrDefault();
            block();
            var after = _membersByAge.FirstOrDefault();
            
            if (!Equals(before, after))
                _changes = _changes.Enqueue(new OldestChanged(after?.UniqueAddress, before?.UniqueAddress));
        }

        private bool MatchingRole(Member member)
        {
            return string.IsNullOrEmpty(_role) || member.HasRole(_role);
        }

        private void HandleInitial(ClusterEvent.CurrentClusterState state)
        {
            // all members except Joining and WeaklyUp
            _membersByAge = state.Members
                .Where(m => m.UpNumber != int.MaxValue && MatchingRole(m))
                .ToImmutableSortedSet(_memberAgeComparer);

            // If there is some removal in progress of an older node it's not safe to immediately become oldest,
            // removal of younger nodes doesn't matter. Note that it can also be started via restart after
            // ClusterSingletonManagerIsStuck.
            var selfUpNumber = state.Members
                .Where(m => m.UniqueAddress == _cluster.SelfUniqueAddress)
                .Select(m => (int?)m.UpNumber)
                .FirstOrDefault() ?? int.MaxValue;

            var oldest = _membersByAge.TakeWhile(m => m.UpNumber <= selfUpNumber).ToList();
            var safeToBeOldest = !oldest.Any(m => m.Status is MemberStatus.Down or MemberStatus.Exiting or MemberStatus.Leaving);
            var initial = new InitialOldestState(oldest.Select(m => m.UniqueAddress).ToImmutableList(), safeToBeOldest);
            _changes = _changes.Enqueue(initial);
        }

        private void Add(Member member)
        {
            if (MatchingRole(member))
                TrackChanges(() =>
                {
                    // replace, it's possible that the upNumber is changed
                    _membersByAge = _membersByAge.Remove(member);
                    _membersByAge = _membersByAge.Add(member);
                });
        }

        private void Remove(Member member)
        {
            if (MatchingRole(member))
                TrackChanges(() => _membersByAge = _membersByAge.Remove(member));
        }

        private void SendFirstChange()
        {
            // don't send cluster change events if this node is shutting its self down, just wait for SelfExiting
            if (!_cluster.IsTerminated)
            {
                _changes = _changes.Dequeue(out var change);
                Context.Parent.Tell(change);
            }
        }

        /// <inheritdoc cref="ActorBase.PreStart"/>
        protected override void PreStart()
        {
            _cluster.Subscribe(Self, typeof(ClusterEvent.IMemberEvent));
        }

        /// <inheritdoc cref="ActorBase.PostStop"/>
        protected override void PostStop()
        {
            _cluster.Unsubscribe(Self);
        }

        /// <inheritdoc cref="UntypedActor.OnReceive"/>
        protected override void OnReceive(object message)
        {
            switch (message)
            {
                case ClusterEvent.CurrentClusterState state:
                    HandleInitial(state);
                    break;
                case ClusterEvent.MemberUp up:
                    Add(up.Member);
                    break;
                case ClusterEvent.MemberRemoved removed:
                    Remove(removed.Member);
                    break;
                case ClusterEvent.MemberExited exited when exited.Member.UniqueAddress != _cluster.SelfUniqueAddress:
                    Remove(exited.Member);
                    break;
                case SelfExiting:
                    Remove(_cluster.ReadView.Self);
                    Sender.Tell(Done.Instance); // reply to ask
                    break;
                case GetNext when _changes.IsEmpty:
                    Context.BecomeStacked(OnDeliverNext);
                    break;
                case GetNext:
                    SendFirstChange();
                    break;
                default:
                    Unhandled(message);
                    break;
            }
        }

        /// <summary>
        /// The buffer was empty when GetNext was received, deliver next event immediately.
        /// </summary>
        /// <param name="message">The message to handle.</param>
        private void OnDeliverNext(object message)
        {
            if (message is ClusterEvent.CurrentClusterState state)
            {
                HandleInitial(state);
                SendFirstChange();
                Context.UnbecomeStacked();
            }
            else if (message is ClusterEvent.MemberUp up)
            {
                Add(up.Member);
                DeliverChanges();
            }
            else if (message is ClusterEvent.MemberRemoved removed)
            {
                Remove(removed.Member);
                DeliverChanges();
            }
            else if (message is ClusterEvent.MemberExited exited && exited.Member.UniqueAddress != _cluster.SelfUniqueAddress)
            {
                Remove(exited.Member);
                DeliverChanges();
            }
            else if (message is SelfExiting)
            {
                Remove(_cluster.ReadView.Self);
                DeliverChanges();
                Sender.Tell(Done.Instance); // reply to ask
            }
            else
            {
                Unhandled(message);
            }
        }

        private void DeliverChanges()
        {
            if (!_changes.IsEmpty)
            {
                SendFirstChange();
                Context.UnbecomeStacked();
            }
        }

        /// <inheritdoc cref="ActorBase.Unhandled"/>
        protected override void Unhandled(object message)
        {
            if (message is ClusterEvent.IMemberEvent)
            {
                // ok, silence
            }
            else
            {
                base.Unhandled(message);
            }
        }
    }
}
