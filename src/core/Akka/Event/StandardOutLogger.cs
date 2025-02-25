﻿//-----------------------------------------------------------------------
// <copyright file="StandardOutLogger.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Util;
using System.Text;

namespace Akka.Event
{
    public abstract class MinimalLogger : MinimalActorRef
    {
        public LogFilterEvaluator Filter { get; internal set; } = LogFilterEvaluator.NoFilters;
        
        /// <summary>
        /// N/A
        /// </summary>
        /// <exception cref="NotImplementedException">This exception is automatically thrown since <see cref="MinimalLogger"/> does not support this property.</exception>
        public sealed override IActorRefProvider Provider
        {
            get { throw new NotSupportedException("This logger does not provide."); }
        }

        /// <summary>
        /// The path where this logger currently resides.
        /// </summary>
        public sealed override ActorPath Path { get; } = new RootActorPath(Address.AllSystems, "/StandardOutLogger");
        
        protected sealed override void TellInternal(object message, IActorRef sender)
        {
            switch (message)
            {
                case null:
                    throw new ArgumentNullException(nameof(message), "The message to log must not be null.");
                
                default:
                    Log(message);
                    break;
            }
        }

        protected abstract void Log(object message);
    }
    
    /// <summary>
    /// This class represents an event logger that logs its messages to standard output (e.g. the console).
    /// 
    /// <remarks>
    /// This logger is always attached first in order to be able to log failures during application start-up,
    /// even before normal logging is started.
    /// </remarks>
    /// </summary>
    public class StandardOutLogger : MinimalLogger
    {
        /// <summary>
        /// Initializes the <see cref="StandardOutLogger"/> class.
        /// </summary>
        static StandardOutLogger()
        {
            DebugColor = ConsoleColor.Gray;
            InfoColor = ConsoleColor.White;
            WarningColor = ConsoleColor.Yellow;
            ErrorColor = ConsoleColor.Red;
            UseColors = true;
        }

        /// <summary>
        /// Handles incoming log events by printing them to the console.
        /// </summary>
        /// <param name="message">The message to print</param>
        /// <exception cref="ArgumentNullException">
        /// This exception is thrown if the given <paramref name="message"/> is undefined.
        /// </exception>
        protected override void Log(object message)
        {
            switch (message)
            {
                case LogEvent logEvent:
                    PrintLogEvent(logEvent, Filter);
                    break;
                
                default:
                    Console.WriteLine(message);
                    break;
            }
        }

        /// <summary>
        /// The foreground color to use when printing Debug events to the console.
        /// </summary>
        public static ConsoleColor DebugColor { get; set; }

        /// <summary>
        /// The foreground color to use when printing Info events to the console.
        /// </summary>
        public static ConsoleColor InfoColor { get; set; }

        /// <summary>
        /// The foreground color to use when printing Warning events to the console.
        /// </summary>
        public static ConsoleColor WarningColor { get; set; }

        /// <summary>
        /// The foreground color to use when printing Error events to the console.
        /// </summary>
        public static ConsoleColor ErrorColor { get; set; }

        /// <summary>
        /// Determines whether colors are used when printing events to the console. 
        /// </summary>
        public static bool UseColors { get; set; }

        /// <summary>
        /// Prints a specified event to the console.
        /// </summary>
        /// <param name="logEvent">The event to print</param>
        /// <param name="filter"></param>
        internal static void PrintLogEvent(LogEvent logEvent, LogFilterEvaluator filter)
        {
            try
            {
                // short circuit if we're not going to print this message
                if (!filter.ShouldTryKeepMessage(logEvent, out var expandedLogMessage))
                    return;
                
                ConsoleColor? color = null;

                if (UseColors)
                {
                    var logLevel = logEvent.LogLevel();
                    switch (logLevel)
                    {
                        case LogLevel.DebugLevel:
                            color = DebugColor;
                            break;
                        case LogLevel.InfoLevel:
                            color = InfoColor;
                            break;
                        case LogLevel.WarningLevel:
                            color = WarningColor;
                            break;
                        case LogLevel.ErrorLevel:
                            color = ErrorColor;
                            break;
                    }
                }

                StandardOutWriter.WriteLine(expandedLogMessage, color);
            }
            catch (FormatException ex)
            {
                /*
                 * If we've reached this point, the `logEvent` itself is formatted incorrectly. 
                 * Therefore we have to treat the data inside the `logEvent` as suspicious and avoid throwing
                 * a second FormatException.
                 */
                var sb = new StringBuilder();
                sb.AppendFormat("[ERROR][{0}]", logEvent.Timestamp)
                    .AppendFormat("[Thread {0}]", logEvent.Thread.ManagedThreadId.ToString().PadLeft(4, '0'))
                    .AppendFormat("[{0}] ", nameof(StandardOutLogger))
                    .AppendFormat("Encountered System.FormatException while recording log: [{0}]", logEvent.LogLevel().PrettyNameFor())
                    .AppendFormat("[{0}]. ", logEvent.LogSource)
                    .Append(ex.Message);

                string msg;
                switch (logEvent.Message)
                {
                    case LogMessage formatted: // a parameterized log
                        msg = " str=[" + formatted.Format + "], args=["+ formatted.Unformatted() +"]";
                        break;
                    case string unformatted: // pre-formatted or non-parameterized log
                        msg = unformatted;
                        break;
                    default: // surprise!
                        msg = logEvent.Message.ToString(); 
                        break;
                }

                sb.Append(msg)
                    .Append(" Please take a look at the logging call where this occurred and fix your format string.");

                StandardOutWriter.WriteLine(sb.ToString(), ErrorColor);
            }
        }
    }
}
