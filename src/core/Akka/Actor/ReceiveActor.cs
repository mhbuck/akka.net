//-----------------------------------------------------------------------
// <copyright file="ReceiveActor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Akka.Actor.Internal;
using Akka.Dispatch;

namespace Akka.Actor
{
    /// <summary>
    /// TBD
    /// </summary>
    public abstract class ReceiveActor : UntypedActor, IInitializableActor
    {
        private bool _shouldUnhandle = true;
        private readonly Stack<HandlerSet> _handlersStack = new();
        private HandlerSet _currentHandlers = null;
        private bool _hasBeenInitialized;

        
        public class HandlerSet
        {
            public HandlerSet()
            {
                Handlers = new Dictionary<Type, List<TypeHandler>>();
                HandleAny = null;
                HandleObject = new List<TypeHandler>();
            }

            public Dictionary<Type, List<TypeHandler>> Handlers { get; set; }

            public List<TypeHandler> HandleObject { get; set; }
            
            public Action<object> HandleAny { get; set; }
        }

        public class TypeHandler
        {
            public Predicate<object> Predicate { get; set; }
            public Func<object, bool> Handler { get; set; }
        }
        
        /// <summary>
        /// TBD
        /// </summary>
        protected ReceiveActor()
        {
            PrepareConfigureMessageHandlers();
        }

        void IInitializableActor.Init()
        {
            //This might be called directly after the constructor, or when the same actor instance has been returned
            //during recreate. Make sure what happens here is idempotent
            if(!_hasBeenInitialized)	//Do not perform this when "recreating" the same instance
            {
                _currentHandlers = _handlersStack.Pop();
                _hasBeenInitialized = true;
            }
        }

        private void EnsureMayConfigureMessageHandlers()
        {
            if(_handlersStack.Count <= 0) throw new InvalidOperationException("You may only call Receive-methods when constructing the actor and inside Become().");
        }

        /// <summary>
        /// Creates and pushes a new MatchBuilder
        /// </summary>
        private void PrepareConfigureMessageHandlers()
        {
            _handlersStack.Push(new HandlerSet());
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        protected sealed override void OnReceive(object message)
        {
            //Seal the method so that implementors cannot use it. They should only use Receive and Become

            ExecuteMessageHandler(message, _currentHandlers);
        }

        private void ExecuteMessageHandler(object message, HandlerSet handlers)
        {
            var messageType = message.GetType();
            var currentHandler = handlers;
            if (currentHandler.Handlers.TryGetValue(messageType, out var handler))
            {
                foreach (var subItem in handler)
                {
                    if (subItem.Predicate == null || subItem.Predicate(message))
                    {
                        var handled = subItem.Handler(message);

                        if (handled)
                        {
                            return;
                        }
                    }
                }
            }

            if (currentHandler.HandleObject.Count > 0)
            {
                foreach (var subItem in currentHandler.HandleObject)
                {
                    if (subItem.Predicate == null || subItem.Predicate(message))
                    {
                        var handled = subItem.Handler(message);

                        if (handled)
                        {
                            return;
                        }
                    }
                }
            }

            if (currentHandler.HandleAny != null)
            {
                currentHandler.HandleAny(message);
            }

            if (_shouldUnhandle)
            {
                Unhandled(message);
            }
        }

        /// <summary>
        /// Changes the actor's behavior and replaces the current receive handler with the specified handler.
        /// </summary>
        /// <param name="configure">Configures the new handler by calling the different Receive overloads.</param>
        protected void Become(Action configure)
        {
            var newHandlers = CreateNewHandlers(configure);
            base.Become(m => ExecuteMessageHandler(m, newHandlers));
        }

        /// <summary>
        /// Changes the actor's behavior and replaces the current receive handler with the specified handler.
        /// The current handler is stored on a stack, and you can revert to it by calling <see cref="ActorBase.UnbecomeStacked"/>
        /// <remarks>Please note, that in order to not leak memory, make sure every call to <see cref="BecomeStacked"/>
        /// is matched with a call to <see cref="ActorBase.UnbecomeStacked"/>.</remarks>
        /// </summary>
        /// <param name="configure">Configures the new handler by calling the different Receive overloads.</param>
        protected void BecomeStacked(Action configure)
        {
            var newHandlers = CreateNewHandlers(configure);
            base.BecomeStacked(m => ExecuteMessageHandler(m, newHandlers));
        }

        private HandlerSet CreateNewHandlers(Action configure)
        {
            PrepareConfigureMessageHandlers();
            configure();
            return _handlersStack.Pop();
        }

        private static Action<T> WrapAsyncHandler<T>(Func<T, Task> asyncHandler)
        {
            return m =>
            {
                Task Wrap() => asyncHandler(m);
                ActorTaskScheduler.RunTask(Wrap);
            };
        }

        /// <summary>
        /// Registers an asynchronous handler for incoming messages of the specified type <typeparamref name="T"/>.
        /// If <paramref name="shouldHandle"/>!=<c>null</c> then it must return true before a message is passed to <paramref name="handler"/>.
        /// <remarks>The actor will be suspended until the task returned by <paramref name="handler"/> completes.</remarks>
        /// <remarks>This method may only be called when constructing the actor or from <see cref="Become(System.Action)"/> or <see cref="BecomeStacked"/>.</remarks>
        /// <remarks>Note that handlers registered prior to this may have handled the message already. 
        /// In that case, this handler will not be invoked.</remarks>
        /// </summary>
        /// <typeparam name="T">The type of the message</typeparam>
        /// <param name="handler">The message handler that is invoked for incoming messages of the specified type <typeparamref name="T"/></param>
        /// <param name="shouldHandle">When not <c>null</c> it is used to determine if the message matches.</param>
        protected void ReceiveAsync<T>(Func<T,Task> handler, Predicate<T> shouldHandle = null)
        {
            Receive(WrapAsyncHandler(handler), shouldHandle);
        }

        /// <summary>
        /// Registers an asynchronous handler for incoming messages of the specified type <typeparamref name="T"/>.
        /// If <paramref name="shouldHandle"/>!=<c>null</c> then it must return true before a message is passed to <paramref name="handler"/>.
        /// <remarks>The actor will be suspended until the task returned by <paramref name="handler"/> completes.</remarks>
        /// <remarks>This method may only be called when constructing the actor or from <see cref="Become(System.Action)"/> or <see cref="BecomeStacked"/>.</remarks>
        /// <remarks>Note that handlers registered prior to this may have handled the message already. 
        /// In that case, this handler will not be invoked.</remarks>
        /// </summary>
        /// <typeparam name="T">The type of the message</typeparam>
        /// <param name="shouldHandle">When not <c>null</c> it is used to determine if the message matches.</param>
        /// <param name="handler">The message handler that is invoked for incoming messages of the specified type <typeparamref name="T"/></param>
        protected void ReceiveAsync<T>(Predicate<T> shouldHandle, Func<T, Task> handler)
        {
            Receive(WrapAsyncHandler(handler), shouldHandle);
        }

        /// <summary>
        /// Registers an asynchronous handler for incoming messages of the specified <paramref name="messageType"/>.
        /// If <paramref name="shouldHandle"/>!=<c>null</c> then it must return true before a message is passed to <paramref name="handler"/>.
        /// <remarks>The actor will be suspended until the task returned by <paramref name="handler"/> completes.</remarks>
        /// <remarks>This method may only be called when constructing the actor or from <see cref="Become(Action)"/> or <see cref="BecomeStacked"/>.</remarks>
        /// <remarks>Note that handlers registered prior to this may have handled the message already. 
        /// In that case, this handler will not be invoked.</remarks>
        /// </summary>
        /// <param name="messageType">The type of the message</param>
        /// <param name="handler">The message handler that is invoked for incoming messages of the specified <paramref name="messageType"/></param>
        /// <param name="shouldHandle">When not <c>null</c> it is used to determine if the message matches.</param>
        protected void ReceiveAsync(Type messageType, Func<object, Task> handler, Predicate<object> shouldHandle = null)
        {
            Receive(messageType, WrapAsyncHandler(handler), shouldHandle);
        }

        /// <summary>
        /// Registers an asynchronous handler for incoming messages of the specified <paramref name="messageType"/>.
        /// If <paramref name="shouldHandle"/>!=<c>null</c> then it must return true before a message is passed to <paramref name="handler"/>.
        /// <remarks>The actor will be suspended until the task returned by <paramref name="handler"/> completes.</remarks>
        /// <remarks>This method may only be called when constructing the actor or from <see cref="Become(Action)"/> or <see cref="BecomeStacked"/>.</remarks>
        /// <remarks>Note that handlers registered prior to this may have handled the message already. 
        /// In that case, this handler will not be invoked.</remarks>
        /// </summary>
        /// <param name="messageType">The type of the message</param>
        /// <param name="shouldHandle">When not <c>null</c> it is used to determine if the message matches.</param>
        /// <param name="handler">The message handler that is invoked for incoming messages of the specified <paramref name="messageType"/></param>
        protected void ReceiveAsync(Type messageType, Predicate<object> shouldHandle, Func<object, Task> handler)
        {
            Receive(messageType, WrapAsyncHandler(handler), shouldHandle);
        }

        /// <summary>
        /// Registers an asynchronous handler for incoming messages of any type.
        /// <remarks>The actor will be suspended until the task returned by <paramref name="handler"/> completes.</remarks>
        /// <remarks>This method may only be called when constructing the actor or from <see cref="Become(Action)"/> or <see cref="BecomeStacked"/>.</remarks>
        /// <remarks>Note that handlers registered prior to this may have handled the message already. 
        /// In that case, this handler will not be invoked.</remarks>
        /// </summary>
        /// <param name="handler">The message handler that is invoked for all</param>
        protected void ReceiveAnyAsync(Func<object, Task> handler)
        {
            ReceiveAny(WrapAsyncHandler(handler));
        }

        /// <summary>
        /// Registers a handler for incoming messages of the specified type <typeparamref name="T"/>.
        /// If <paramref name="shouldHandle"/>!=<c>null</c> then it must return true before a message is passed to <paramref name="handler"/>.
        /// <remarks>This method may only be called when constructing the actor or from <see cref="Become(Action)"/> or <see cref="BecomeStacked"/>.</remarks>
        /// <remarks>Note that handlers registered prior to this may have handled the message already. 
        /// In that case, this handler will not be invoked.</remarks>
        /// </summary>
        /// <typeparam name="T">The type of the message</typeparam>
        /// <param name="handler">The message handler that is invoked for incoming messages of the specified type <typeparamref name="T"/></param>
        /// <param name="shouldHandle">When not <c>null</c> it is used to determine if the message matches.</param>
        /// <exception cref="InvalidOperationException">This exception is thrown if this method is called outside of the actor's constructor or from <see cref="Become(Action)"/>.</exception>
        protected void Receive<T>(Action<T> handler, Predicate<T> shouldHandle = null)
        {
            
            Receive(typeof(T), o =>
            {
                handler((T)o);
                return true;
            }, shouldHandle == null ? (Predicate<object>)null : o => shouldHandle((T)o));
        }

        /// <summary>
        /// Registers a handler for incoming messages of the specified type <typeparamref name="T"/>.
        /// If <paramref name="shouldHandle"/>!=<c>null</c> then it must return true before a message is passed to <paramref name="handler"/>.
        /// <remarks>This method may only be called when constructing the actor or from <see cref="Become(Action)"/> or <see cref="BecomeStacked"/>.</remarks>
        /// <remarks>Note that handlers registered prior to this may have handled the message already. 
        /// In that case, this handler will not be invoked.</remarks>
        /// </summary>
        /// <typeparam name="T">The type of the message</typeparam>
        /// <param name="handler">The message handler that is invoked for incoming messages of the specified type <typeparamref name="T"/></param>
        /// <param name="shouldHandle">When not <c>null</c> it is used to determine if the message matches.</param>
        /// <exception cref="InvalidOperationException">This exception is thrown if this method is called outside of the actor's constructor or from <see cref="Become(Action)"/>.</exception>
        protected void Receive<T>(Predicate<T> shouldHandle, Action<T> handler)
        {
            Receive(handler, shouldHandle);
        }

        /// <summary>
        /// Registers a handler for incoming messages of the specified <paramref name="messageType"/>.
        /// If <paramref name="shouldHandle"/>!=<c>null</c> then it must return true before a message is passed to <paramref name="handler"/>.
        /// <remarks>This method may only be called when constructing the actor or from <see cref="Become(Action)"/> or <see cref="BecomeStacked"/>.</remarks>
        /// <remarks>Note that handlers registered prior to this may have handled the message already. 
        /// In that case, this handler will not be invoked.</remarks>
        /// </summary>
        /// <param name="messageType">The type of the message</param>
        /// <param name="handler">The message handler that is invoked for incoming messages of the specified <paramref name="messageType"/></param>
        /// <param name="shouldHandle">When not <c>null</c> it is used to determine if the message matches.</param>
        /// <exception cref="InvalidOperationException">This exception is thrown if this method is called outside of the actor's constructor or from <see cref="Become(Action)"/>.</exception>
        protected void Receive(Type messageType, Action<object> handler, Predicate<object> shouldHandle = null)
        {
            Receive(messageType, o =>
            {
                handler(o);
                return true;
            }, shouldHandle);
        }

        /// <summary>
        /// Registers a handler for incoming messages of the specified <paramref name="messageType"/>.
        /// If <paramref name="shouldHandle"/>!=<c>null</c> then it must return true before a message is passed to <paramref name="handler"/>.
        /// <remarks>This method may only be called when constructing the actor or from <see cref="Become(Action)"/> or <see cref="BecomeStacked"/>.</remarks>
        /// <remarks>Note that handlers registered prior to this may have handled the message already. 
        /// In that case, this handler will not be invoked.</remarks>
        /// </summary>
        /// <param name="messageType">The type of the message</param>
        /// <param name="handler">The message handler that is invoked for incoming messages of the specified <paramref name="messageType"/></param>
        /// <param name="shouldHandle">When not <c>null</c> it is used to determine if the message matches.</param>
        /// <exception cref="InvalidOperationException">This exception is thrown if this method is called outside of the actor's constructor or from <see cref="Become(Action)"/>.</exception>
        protected void Receive(Type messageType, Predicate<object> shouldHandle, Action<object> handler)
        {
            Receive(messageType, o =>
            {
                handler(o);
                return true;
            }, shouldHandle);
        }

        /// <summary>
        /// Registers a handler for incoming messages of the specified type <typeparamref name="T"/>.
        /// The handler should return <c>true</c> if it has handled the message. 
        /// If the handler returns true no more handlers will be tried; otherwise the next registered handler will be tried.
        /// <remarks>This method may only be called when constructing the actor or from <see cref="Become(Action)"/> or <see cref="BecomeStacked"/>.</remarks>
        /// <remarks>Note that handlers registered prior to this may have handled the message already. 
        /// In that case, this handler will not be invoked.</remarks>
        /// </summary>
        /// <typeparam name="T">The type of the message</typeparam>
        /// <param name="handler">The message handler that is invoked for incoming messages of the 
        /// specified type <typeparamref name="T"/>. It should return <c>true</c>if it handled/matched 
        /// the message; <c>false</c> otherwise.</param>
        /// <exception cref="InvalidOperationException">This exception is thrown if this method is called outside of the actor's constructor or from <see cref="Become(Action)"/>.</exception>
        protected void Receive<T>(Func<T, bool> handler)
        {
            
            Receive(typeof(T), o => handler((T)o), null);
        }

        /// <summary>
        /// Registers a handler for incoming messages of the specified <paramref name="messageType"/>.
        /// The handler should return <c>true</c> if it has handled the message. 
        /// If the handler returns true no more handlers will be tried; otherwise the next registered handler will be tried.
        /// <remarks>This method may only be called when constructing the actor or from <see cref="Become(Action)"/> or <see cref="BecomeStacked"/>.</remarks>
        /// <remarks>Note that handlers registered prior to this may have handled the message already. 
        /// In that case, this handler will not be invoked.</remarks>
        /// </summary>
        /// <param name="messageType">The type of the message</param>
        /// <param name="handler">The message handler that is invoked for incoming messages of the 
        /// specified type <paramref name="messageType"/>. It should return <c>true</c>if it handled/matched 
        /// the message; <c>false</c> otherwise.</param>
        /// <exception cref="InvalidOperationException">This exception is thrown if this method is called outside of the actor's constructor or from <see cref="Become(Action)"/>.</exception>
        protected void Receive(Type messageType, Func<object, bool> handler)
        {
            Receive(messageType, handler, null);
        }
        
        protected void Receive(Type messageType, Func<object, bool> handler, Predicate<object> shouldHandle)
        {
            EnsureMayConfigureMessageHandlers();
            var handlers = _handlersStack.Peek();
            var subHandler = new TypeHandler
            {
                Predicate = shouldHandle,
                Handler = handler
            };

            if (messageType == typeof(object))
            {
                handlers.HandleObject.Add(subHandler);
            }
            else
            {
                if (!handlers.Handlers.TryGetValue(messageType, out var handlerList))
                {
                    handlerList = new List<TypeHandler>();
                    handlers.Handlers[messageType] = handlerList;
                }
                handlerList.Add(subHandler);
            }
        }

        /// <summary>
        /// Registers a handler for incoming messages of any type.
        /// <remarks>This method may only be called when constructing the actor or from <see cref="Become(Action)"/> or <see cref="BecomeStacked"/>.</remarks>
        /// <remarks>Note that handlers registered prior to this may have handled the message already. 
        /// In that case, this handler will not be invoked.</remarks>
        /// </summary>
        /// <param name="handler">The message handler that is invoked for all</param>
        /// <exception cref="InvalidOperationException">This exception is thrown if this method is called outside of the actor's constructor or from <see cref="Become(Action)"/>.</exception>
        protected void ReceiveAny(Action<object> handler)
        {
            EnsureMayConfigureMessageHandlers();
            var handlers = _handlersStack.Peek();
            handlers.HandleAny = handler;
        }
    }
}
