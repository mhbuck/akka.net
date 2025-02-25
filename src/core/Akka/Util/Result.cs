﻿//-----------------------------------------------------------------------
// <copyright file="Result.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;

namespace Akka.Util
{
    //A generic type can't have a explicit layout
    //[StructLayout(LayoutKind.Explicit)]
    /// <summary>
    /// TBD
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    public struct Result<T> : IEquatable<Result<T>>
    {
        //[FieldOffset(0)]
        /// <summary>
        /// TBD
        /// </summary>
        public readonly bool IsSuccess;
        //[FieldOffset(1)]
        /// <summary>
        /// TBD
        /// </summary>
        public readonly T Value;
        //[FieldOffset(1)]
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Exception Exception;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="value">TBD</param>
        public Result(T value) : this()
        {
            IsSuccess = true;
            Value = value;
        }
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="exception">TBD</param>
        public Result(Exception exception) : this()
        {
            IsSuccess = false;
            Exception = exception;
        }

        
        public bool Equals(Result<T> other)
        {
            if (IsSuccess ^ other.IsSuccess) return false;
            return IsSuccess
                ? Equals(Value, other.Value)
                : Equals(Exception, other.Exception);
        }

        
        public override bool Equals(object obj)
        {
            if (obj is Result<T> result) return Equals(result);
            return false;
        }

        
        public override int GetHashCode()
        {
            return IsSuccess
                ? (Value == null ? 0 : Value.GetHashCode())
                : (Exception == null ? 0 : Exception.GetHashCode());
        }

        /// <summary>
        /// Compares two specified <see cref="Result{T}"/> for equality.
        /// </summary>
        /// <param name="left">The first <see cref="Result{T}"/> used for comparison</param>
        /// <param name="right">The second <see cref="Result{T}"/> used for comparison</param>
        /// <returns><c>true</c> if both <see cref="Result{T}"/> are equal; otherwise <c>false</c></returns>
        public static bool operator ==(Result<T> left, Result<T> right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Compares two specified <see cref="Result{T}"/> for inequality.
        /// </summary>
        /// <param name="left">The first <see cref="Result{T}"/> used for comparison</param>
        /// <param name="right">The second <see cref="Result{T}"/> used for comparison</param>
        /// <returns><c>true</c> if both <see cref="Result{T}"/> are not equal; otherwise <c>false</c></returns>
        public static bool operator !=(Result<T> left, Result<T> right)
        {
            return !(left == right);
        }

        public override string ToString() => IsSuccess ? $"Success ({Value})" : $"Failure ({Exception})";
    }

    /// <summary>
    /// TBD
    /// </summary>
    public static class Result
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="value">TBD</param>
        /// <returns>TBD</returns>
        public static Result<T> Success<T>(T value)
        {
            return new Result<T>(value);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="exception">TBD</param>
        /// <returns>TBD</returns>
        public static Result<T> Failure<T>(Exception exception)
        {
            return new Result<T>(exception);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="task">TBD</param>
        /// <returns>TBD</returns>
        public static Result<T> FromTask<T>(Task<T> task)
        {
            if(!task.IsCompleted)
                throw new ArgumentException("Task is not completed. Result.FromTask only accepts completed tasks.", nameof(task));
            
            if(task.Exception is not null)
                return new Result<T>(task.Exception);
            
            if (task.IsCanceled && task.Exception is null)
            {
                try
                {
                    _ = task.GetAwaiter().GetResult();
                }
                catch(Exception e)
                {
                    return new Result<T>(e);
                }

                throw new InvalidOperationException("Should never reach this line!");
            }
            
            if(task.IsFaulted && task.Exception is null)
                throw new InvalidOperationException("Should never happen! something is wrong with .NET Task code!");
            
            return new Result<T>(task.Result);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="func">TBD</param>
        /// <returns>TBD</returns>
        public static Result<T> From<T>(Func<T> func)
        {
            try
            {
                var value = func();
                return new Result<T>(value);
            }
            catch (Exception e)
            {
                return new Result<T>(e);
            }
        }
        
        

    }
}
