﻿//-----------------------------------------------------------------------
// <copyright file="ClusterSingletonProxySettings.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Configuration;
using Akka.Util;
using Google.Protobuf.WellKnownTypes;

namespace Akka.Cluster.Tools.Singleton
{
    /// <summary>
    /// Create settings from the default configuration
    /// `akka.cluster.singleton-proxy`.
    /// </summary>
    public sealed class ClusterSingletonProxySettings : INoSerializationVerificationNeeded
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="system">TBD</param>
        /// <exception cref="ConfigurationException">TBD</exception>
        /// <returns>TBD</returns>
        public static ClusterSingletonProxySettings Create(ActorSystem system)
        {
            system.Settings.InjectTopLevelFallback(ClusterSingletonManager.DefaultConfig());
            var config = system.Settings.Config.GetConfig("akka.cluster.singleton-proxy");
            if (config.IsNullOrEmpty())
                throw ConfigurationException.NullOrEmptyConfig<ClusterSingletonProxySettings>("akka.cluster.singleton-proxy");

            var considerAppVersion =
                system.Settings.Config.GetBoolean("akka.cluster.singleton.consider-app-version", false);
            return Create(config, considerAppVersion);
        }

        /// <summary>
        /// Create settings from a configuration with the same layout as
        /// the default configuration `akka.cluster.singleton-proxy`.
        /// </summary>
        /// <param name="config">TBD</param>
        /// <param name="considerAppVersion">TBD</param>
        /// <returns>TBD</returns>
        public static ClusterSingletonProxySettings Create(Config config, bool considerAppVersion)
        {
            if (config.IsNullOrEmpty())
                throw ConfigurationException.NullOrEmptyConfig<ClusterSingletonProxySettings>();

            var role = config.GetString("role", null);
            if (role == string.Empty) role = null;

            return new ClusterSingletonProxySettings(
                singletonName: config.GetString("singleton-name"),
                role: role,
                singletonIdentificationInterval: config.GetTimeSpan("singleton-identification-interval"),
                bufferSize: config.GetInt("buffer-size", 0),
                considerAppVersion: considerAppVersion, 
                logSingletonIdentificationFailure: config.GetBoolean("log-singleton-identification-failure", true),
                singletonIdentificationFailurePeriod: config.GetTimeSpan("singleton-identification-failure-period", TimeSpan.FromSeconds(30)));
        }

        /// <summary>
        /// The actor name of the singleton actor that is started by the <see cref="ClusterSingletonManager"/>.
        /// </summary>
        public string SingletonName { get; }

        /// <summary>
        /// The role of the cluster nodes where the singleton can be deployed.
        /// </summary>
        public string Role { get; }

        /// <summary>
        /// Interval at which the proxy will try to resolve the singleton instance.
        /// </summary>
        public TimeSpan SingletonIdentificationInterval { get; }

        /// <summary>
        /// If the location of the singleton is unknown the proxy will buffer this number of messages and deliver them when the singleton is identified.
        /// </summary>
        public int BufferSize { get; }
        
        /// <summary>
        /// Should <see cref="Member.AppVersion"/> be considered when the cluster singleton instance is being moved to another node.
        /// When set to false, singleton instance will always be created on oldest member.
        /// When set to true, singleton instance will be created on the oldest member with the highest <see cref="Member.AppVersion"/> number.
        /// </summary>
        public bool ConsiderAppVersion { get; }
        
        /// <summary>
        /// Should the singleton proxy publish a warning if no singleton actor were found after a period of time
        /// </summary>
        public bool LogSingletonIdentificationFailure { get; }
        
        /// <summary>
        /// The period the proxy will wait until it logs a missing singleton warning, defaults to 1 minute
        /// </summary>
        public TimeSpan SingletonIdentificationFailurePeriod { get; }

        /// <summary>
        /// Creates new instance of the <see cref="ClusterSingletonProxySettings"/>.
        /// </summary>
        /// <param name="singletonName">The actor name of the singleton actor that is started by the <see cref="ClusterSingletonManager"/>.</param>
        /// <param name="role">The role of the cluster nodes where the singleton can be deployed. If None, then any node will do.</param>
        /// <param name="singletonIdentificationInterval">Interval at which the proxy will try to resolve the singleton instance.</param>
        /// <param name="bufferSize">
        /// If the location of the singleton is unknown the proxy will buffer this number of messages and deliver them
        /// when the singleton is identified.When the buffer is full old messages will be dropped when new messages
        /// are sent via the proxy. Use 0 to disable buffering, i.e.messages will be dropped immediately if the location
        /// of the singleton is unknown.
        /// </param>
        /// <param name="considerAppVersion">
        /// Should <see cref="Member.AppVersion"/> be considered when the cluster singleton instance is being moved to another node.
        /// When set to false, singleton instance will always be created on oldest member.
        /// When set to true, singleton instance will be created on the oldest member with the highest <see cref="Member.AppVersion"/> number.
        /// </param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown when either the specified <paramref name="singletonIdentificationInterval"/>
        /// or <paramref name="bufferSize"/> is less than or equal to zero.
        /// </exception>
        /// <exception cref="ArgumentNullException"></exception>
        [Obsolete("Use constructor with logSingletonIdentificationFailure and logSingletonIdentificationInterval parameter instead. Since v1.5.30")]
        public ClusterSingletonProxySettings(
            string singletonName,
            string role,
            TimeSpan singletonIdentificationInterval,
            int bufferSize,
            bool considerAppVersion)
            : this(singletonName, role, singletonIdentificationInterval, bufferSize, considerAppVersion, true, TimeSpan.FromSeconds(30))
        {
        }

        /// <summary>
        /// Creates new instance of the <see cref="ClusterSingletonProxySettings"/>.
        /// </summary>
        /// <param name="singletonName">The actor name of the singleton actor that is started by the <see cref="ClusterSingletonManager"/>.</param>
        /// <param name="role">The role of the cluster nodes where the singleton can be deployed. If None, then any node will do.</param>
        /// <param name="singletonIdentificationInterval">Interval at which the proxy will try to resolve the singleton instance.</param>
        /// <param name="bufferSize">
        /// If the location of the singleton is unknown the proxy will buffer this number of messages and deliver them
        /// when the singleton is identified.When the buffer is full old messages will be dropped when new messages
        /// are sent via the proxy. Use 0 to disable buffering, i.e.messages will be dropped immediately if the location
        /// of the singleton is unknown.
        /// </param>
        /// <param name="considerAppVersion">
        /// Should <see cref="Member.AppVersion"/> be considered when the cluster singleton instance is being moved to another node.
        /// When set to false, singleton instance will always be created on oldest member.
        /// When set to true, singleton instance will be created on the oldest member with the highest <see cref="Member.AppVersion"/> number.
        /// </param>
        /// <param name="logSingletonIdentificationFailure">
        /// Should the singleton proxy log a warning if no singleton actor were found after a period of time
        /// </param>
        /// <param name="singletonIdentificationFailurePeriod">
        /// The period the proxy will wait until it logs a missing singleton warning, defaults to 1 minute
        /// </param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown when either the specified <paramref name="singletonIdentificationInterval"/>
        /// or <paramref name="bufferSize"/> is less than or equal to zero.
        /// </exception>
        /// <exception cref="ArgumentNullException"></exception>
        public ClusterSingletonProxySettings(
            string singletonName,
            string role,
            TimeSpan singletonIdentificationInterval, 
            int bufferSize, 
            bool considerAppVersion,
            bool logSingletonIdentificationFailure,
            TimeSpan singletonIdentificationFailurePeriod)
        {
            if (string.IsNullOrEmpty(singletonName))
                throw new ArgumentNullException(nameof(singletonName));
            if (singletonIdentificationInterval == TimeSpan.Zero)
                throw new ArgumentException("singletonIdentificationInterval must be positive", nameof(singletonIdentificationInterval));
            if (bufferSize < 0)
                throw new ArgumentException("bufferSize must not be negative", nameof(bufferSize));

            SingletonName = singletonName;
            Role = role;
            SingletonIdentificationInterval = singletonIdentificationInterval;
            BufferSize = bufferSize;
            ConsiderAppVersion = considerAppVersion;
            LogSingletonIdentificationFailure = logSingletonIdentificationFailure;
            SingletonIdentificationFailurePeriod = singletonIdentificationFailurePeriod;
        }

        /// <summary>
        /// Creates a new <see cref="ClusterSingletonProxySettings" /> setting with the specified <paramref name="singletonName"/>.
        /// <note>
        /// This method is immutable and returns a new instance of the setting.
        /// </note>
        /// </summary>
        /// <param name="singletonName">The name of the singleton actor used by the <see cref="ClusterSingletonManager"/>.</param>
        /// <returns>A new setting with the provided <paramref name="singletonName" />.</returns>
        public ClusterSingletonProxySettings WithSingletonName(string singletonName)
        {
            return Copy(singletonName: singletonName);
        }

        /// <summary>
        /// Creates a new <see cref="ClusterSingletonProxySettings" /> setting with the specified <paramref name="role"/>
        /// from the <c>akka.cluster.role</c> in the configuration used. The <paramref name="role"/> specifies the nodes
        /// on which the targeted singleton can run.
        /// <note>
        /// This method is immutable and returns a new instance of the setting.
        /// </note>
        /// </summary>
        /// <param name="role">The role of the singleton proxy.</param>
        /// <returns>A new setting with the provided <paramref name="role" />.</returns>
        public ClusterSingletonProxySettings WithRole(string role)
        {
            return Copy(role: role);
        }

        /// <summary>
        /// Creates a new <see cref="ClusterSingletonProxySettings" /> setting with the specified <paramref name="singletonIdentificationInterval"/>.
        /// <note>
        /// This method is immutable and returns a new instance of the setting.
        /// </note>
        /// </summary>
        /// <param name="singletonIdentificationInterval">The identification level of the singleton proxy.</param>
        /// <returns>A new setting with the provided <paramref name="singletonIdentificationInterval" />.</returns>
        public ClusterSingletonProxySettings WithSingletonIdentificationInterval(TimeSpan singletonIdentificationInterval)
        {
            return Copy(singletonIdentificationInterval: singletonIdentificationInterval);
        }

        /// <summary>
        /// Creates a new <see cref="ClusterSingletonProxySettings" /> setting with the specified <paramref name="bufferSize"/>.
        /// <note>
        /// This method is immutable and returns a new instance of the setting.
        /// </note>
        /// </summary>
        /// <param name="bufferSize">The buffer size of the singleton proxy.</param>
        /// <returns>A new setting with the provided <paramref name="bufferSize" />.</returns>
        public ClusterSingletonProxySettings WithBufferSize(int bufferSize)
        {
            return Copy(bufferSize: bufferSize);
        }

        public ClusterSingletonProxySettings WithLogSingletonIdentificationFailure(bool logSingletonIdentificationFailure)
        {
            return Copy(logSingletonIdentificationFailure: logSingletonIdentificationFailure);
        }

        public ClusterSingletonProxySettings WithSingletonIdentificationFailureDuration(TimeSpan singletonIdentificationFailurePeriod)
        {
            return Copy(singletonIdentificationFailurePeriod: singletonIdentificationFailurePeriod);
        }

        private ClusterSingletonProxySettings Copy(
            string singletonName = null,
            Option<string> role = default,
            TimeSpan? singletonIdentificationInterval = null,
            int? bufferSize = null,
            bool? considerAppVersion = null,
            bool? logSingletonIdentificationFailure = null,
            TimeSpan? singletonIdentificationFailurePeriod = null)
        {
            return new ClusterSingletonProxySettings(
                singletonName: singletonName ?? SingletonName,
                role: role.HasValue ? role.Value : Role,
                singletonIdentificationInterval: singletonIdentificationInterval ?? SingletonIdentificationInterval,
                bufferSize: bufferSize ?? BufferSize,
                considerAppVersion: considerAppVersion ?? ConsiderAppVersion,
                logSingletonIdentificationFailure: logSingletonIdentificationFailure ?? LogSingletonIdentificationFailure,
                singletonIdentificationFailurePeriod: singletonIdentificationFailurePeriod ?? SingletonIdentificationFailurePeriod);
        }
    }
}
