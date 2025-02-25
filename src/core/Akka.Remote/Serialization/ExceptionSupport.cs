﻿//-----------------------------------------------------------------------
// <copyright file="ExceptionSupport.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Akka.Actor;
using Akka.Util;
using Akka.Util.Internal;
using Google.Protobuf;
using System.Runtime.Serialization;
using Akka.Remote.Serialization.Proto.Msg;

namespace Akka.Remote.Serialization
{
    internal sealed class ExceptionSupport
    {
        private readonly WrappedPayloadSupport _wrappedPayloadSupport;
        private const BindingFlags All = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        private readonly HashSet<string> _defaultProperties = new()
        {
            "ClassName",
            "Message",
            "StackTraceString",
            "Source",
            "InnerException",
            "HelpURL",
            "RemoteStackTraceString",
            "RemoteStackIndex",
            "ExceptionMethod",
            "HResult",
            "Data",
            "TargetSite",
            "HelpLink",
            "StackTrace",
            "WatsonBuckets"
        };

        public ExceptionSupport(ExtendedActorSystem system)
        {
            _wrappedPayloadSupport = new WrappedPayloadSupport(system);
        }

        public byte[] SerializeException(Exception exception)
        {
            return ExceptionToProto(exception).ToByteArray();
        }

        internal Proto.Msg.ExceptionData ExceptionToProto(Exception exception)
        {
            return ExceptionToProtoNet(exception);
        }

        public Exception DeserializeException(byte[] bytes)
        {
            var proto = Proto.Msg.ExceptionData.Parser.ParseFrom(bytes);
            return ExceptionFromProto(proto);
        }

        internal Exception ExceptionFromProto(Proto.Msg.ExceptionData proto)
        {
            return ExceptionFromProtoNet(proto);
        }

        private readonly FormatterConverter _defaultFormatterConverter = new();

        public Proto.Msg.ExceptionData ExceptionToProtoNet(Exception exception)
        {
            var message = new Proto.Msg.ExceptionData();

            if (exception == null)
                return message;

            var exceptionType = exception.GetType();

            message.TypeName = exceptionType.TypeQualifiedName();
            message.Message = exception.Message;
            message.StackTrace = exception.StackTrace ?? "";
            message.Source = exception.Source ?? "";
            message.InnerException = ExceptionToProto(exception.InnerException);
            
            var forwardedFrom = exceptionType.GetCustomAttribute<TypeForwardedFromAttribute>();
            message.TypeForwardedFrom = forwardedFrom is not null 
                ? forwardedFrom.AssemblyFullName[..forwardedFrom.AssemblyFullName.IndexOf(',')] 
                : string.Empty;

            var serializable = exception as ISerializable;
            var serializationInfo = new SerializationInfo(exceptionType, _defaultFormatterConverter);
            serializable.GetObjectData(serializationInfo, new StreamingContext());

            foreach (var info in serializationInfo)
            {
                if (_defaultProperties.Contains(info.Name)) continue;
                if (info.Value is Exception exceptionValue)
                {
                    var exceptionPayload = ExceptionToProto(exceptionValue);
                    var preparedValue = _wrappedPayloadSupport.PayloadToProto(exceptionPayload);
                    message.CustomFields.Add(info.Name, preparedValue);
                } else
                {
                    var preparedValue = _wrappedPayloadSupport.PayloadToProto(info.Value);
                    message.CustomFields.Add(info.Name, preparedValue);
                }
            }

            return message;
        }

        public Exception ExceptionFromProtoNet(Proto.Msg.ExceptionData proto)
        {
            if (string.IsNullOrEmpty(proto.TypeName))
                return null;

            var exceptionType = Type.GetType(proto.TypeName);
            
            // If type loading failed and type was forwarded from an older assembly,
            // retry by loading the type from the older assembly name
            if (exceptionType is null && proto.TypeForwardedFrom != string.Empty)
            {
                var typeName = $"{proto.TypeName[..proto.TypeName.IndexOf(',')]}, {proto.TypeForwardedFrom}";
                exceptionType = Type.GetType(typeName);
            }

            // If we still fail, throw.
            if (exceptionType is null)
                throw new SerializationException($"Failed to deserialize ExceptionData. Could not load {proto.TypeName}. {proto}");

            var serializationInfo = new SerializationInfo(exceptionType, _defaultFormatterConverter);

            serializationInfo.AddValue("ClassName", proto.TypeName);
            serializationInfo.AddValue("Message", ValueOrNull(proto.Message));
            serializationInfo.AddValue("StackTraceString", ValueOrNull(proto.StackTrace));
            serializationInfo.AddValue("Source", ValueOrNull(proto.Source));
            serializationInfo.AddValue("InnerException", ExceptionFromProto(proto.InnerException));
            serializationInfo.AddValue("HelpURL", string.Empty);
            serializationInfo.AddValue("RemoteStackTraceString", null);
            serializationInfo.AddValue("RemoteStackIndex", 0);
            serializationInfo.AddValue("ExceptionMethod", null);
            serializationInfo.AddValue("HResult", int.MinValue);

            foreach (var field in proto.CustomFields)
            {
                var payload = _wrappedPayloadSupport.PayloadFrom(field.Value);
                if (payload is ExceptionData exception)
                    payload = ExceptionFromProto(exception);
                serializationInfo.AddValue(field.Key, payload);
            }

            Exception obj = null;
            ConstructorInfo constructorInfo = exceptionType.GetConstructor(
                All,
                null,
                new[] { typeof(SerializationInfo), typeof(StreamingContext) },
                null);

            if (constructorInfo != null)
            {
                object[] args = { serializationInfo, new StreamingContext() };
                obj = constructorInfo.Invoke(args).AsInstanceOf<Exception>();
            }

            return obj;
        }

        private static string ValueOrNull(string value)
            => string.IsNullOrEmpty(value) ? null : value;
    }
}
