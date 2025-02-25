﻿//-----------------------------------------------------------------------
// <copyright file="AkkaSpecExtensions.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.TestKit.Xunit2.Internals;
using Akka.Util.Internal;
using Xunit;
using Xunit.Sdk;

// ReSharper disable once CheckNamespace
namespace Akka.TestKit
{
    /// <summary>
    /// TBD
    /// </summary>
    public static class AkkaSpecExtensions
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="self">TBD</param>
        /// <param name="isValid">TBD</param>
        /// <param name="message">TBD</param>
        public static void Should<T>(this T self, Func<T, bool> isValid, string message)
        {
            Assert.True(isValid(self), message ?? "Value did not meet criteria. Value: " + self);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="self">TBD</param>
        /// <param name="expectedCount">TBD</param>
        public static void ShouldHaveCount<T>(this IReadOnlyCollection<T> self, int expectedCount)
        {
            Assert.Equal(expectedCount, self.Count);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="self">TBD</param>
        /// <param name="other">TBD</param>
        public static void ShouldBe<T>(this IEnumerable<T> self, IEnumerable<T> other)
        {
            var otherList = other.ToList();
            var selfList = self.ToList();
            var expected = string.Join(",", otherList.Select(i => $"'{i}'"));
            var actual = string.Join(",", selfList.Select(i => $"'{i}'"));

            Assert.True(selfList.SequenceEqual(otherList), "Expected " + expected + " got " + actual);
        }

        public static async Task ShouldBeAsync<T>(this IAsyncEnumerable<T> self, IEnumerable<T> other)
        {
            if (self is null)
                throw new ArgumentNullException(nameof(self));
            if (other is null)
                throw new ArgumentNullException(nameof(other));
            
            var l1 = new List<string>();
            var l2 = new List<string>();
            var index = 0;

            await using var e1 = self.GetAsyncEnumerator();
            using var e2 = other.GetEnumerator();
            
            var comparer = EqualityComparer<T>.Default;
            while (await e1.MoveNextAsync())
            {
                l1.Add($"'{e1.Current}'");
                if (!e2.MoveNext())
                    throw AkkaEqualException.ForMismatchedValues(
                        l2, l1, $"Input has more elements than expected, differ at index {index}");
                
                l2.Add($"'{e2.Current}'");
                if(!comparer.Equals(e1.Current, e2.Current))
                    throw AkkaEqualException.ForMismatchedValues(
                        l2, l1, $"Input is not equal to expected, differ at index {index}");
                
                index++;
            }

            if (e2.MoveNext())
            {
                l2.Add($"'{e2.Current}'");
                throw AkkaEqualException.ForMismatchedValues(
                    l2, l1, $"Input has less elements than expected, differ at index {index}");
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="self">TBD</param>
        /// <param name="expected">TBD</param>
        /// <param name="message">TBD</param>
        public static void ShouldBe<T>(this T self, T expected, string message = null)
        {
            Assert.Equal(expected, self);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="self">TBD</param>
        /// <param name="expected">TBD</param>
        /// <param name="message">TBD</param>
        public static async Task ShouldBeAsync<T>(this ValueTask<T> self, T expected, string message = null)
        {
            Assert.Equal(expected, await self);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="self">TBD</param>
        /// <param name="expected">TBD</param>
        /// <param name="message">TBD</param>
        public static void ShouldNotBe<T>(this T self, T expected, string message = null)
        {
            Assert.NotEqual(expected, self);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="self">TBD</param>
        /// <param name="expected">TBD</param>
        /// <param name="message">TBD</param>
        public static void ShouldBeSame<T>(this T self, T expected, string message = null)
        {
            Assert.Equal(expected, self);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="self">TBD</param>
        /// <param name="expected">TBD</param>
        /// <param name="message">TBD</param>
        public static void ShouldNotBeSame<T>(this T self, T expected, string message = null)
        {
            Assert.NotEqual(expected, self);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="b">TBD</param>
        /// <param name="message">TBD</param>
        public static void ShouldBeTrue(this bool b, string message = null)
        {
            Assert.True(b, message);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="b">TBD</param>
        /// <param name="message">TBD</param>
        public static void ShouldBeFalse(this bool b, string message = null)
        {
            Assert.False(b, message);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="actual">TBD</param>
        /// <param name="value">TBD</param>
        /// <param name="message">TBD</param>
        public static void ShouldBeLessThan<T>(this T actual, T value, string message = null) where T : IComparable<T>
        {
            var comparisonResult = actual.CompareTo(value);
            Assert.True(comparisonResult < 0, message ?? "Expected Actual: " + actual + " to be less than " + value);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="actual">TBD</param>
        /// <param name="value">TBD</param>
        /// <param name="message">TBD</param>
        public static void ShouldBeLessOrEqualTo<T>(this T actual, T value, string message = null) where T : IComparable<T>
        {
            var comparisonResult = actual.CompareTo(value);
            Assert.True(comparisonResult <= 0, message ?? "Expected Actual: " + actual + " to be less than " + value);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="actual">TBD</param>
        /// <param name="value">TBD</param>
        /// <param name="message">TBD</param>
        public static void ShouldBeGreaterThan<T>(this T actual, T value, string message = null) where T : IComparable<T>
        {
            var comparisonResult = actual.CompareTo(value);
            Assert.True(comparisonResult > 0, message ?? "Expected Actual: " + actual + " to be less than " + value);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="actual">TBD</param>
        /// <param name="value">TBD</param>
        /// <param name="message">TBD</param>
        public static void ShouldBeGreaterOrEqual<T>(this T actual, T value, string message = null) where T : IComparable<T>
        {
            var comparisonResult = actual.CompareTo(value);
            Assert.True(comparisonResult >= 0, message ?? "Expected Actual: " + actual + " to be less than " + value);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="s">TBD</param>
        /// <param name="start">TBD</param>
        /// <param name="message">TBD</param>
        public static void ShouldStartWith(this string s, string start, string message = null)
        {
            Assert.Equal(s.Substring(0, Math.Min(s.Length, start.Length)), start);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="actual">TBD</param>
        /// <param name="expected">TBD</param>
        public static void ShouldOnlyContainInOrder<T>(this IEnumerable<T> actual, params T[] expected)
        {
            ShouldBe(actual, expected);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="actual">TBD</param>
        /// <param name="expected">TBD</param>
        public static async Task ShouldOnlyContainInOrderAsync<T>(this IAsyncEnumerable<T> actual, params T[] expected)
            => await ShouldBeAsync(actual, expected).ConfigureAwait(false);
        
        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="actual">TBD</param>
        /// <param name="expected">TBD</param>
        public static void ShouldOnlyContainInOrder<T>(this IEnumerable<T> actual, IEnumerable<T> expected)
        {
            ShouldBe(actual, expected);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="TException">TBD</typeparam>
        /// <param name="func">TBD</param>
        public static async Task ThrowsAsync<TException>(Func<Task> func)
        {
            var expected = typeof(TException);
            Type actual = null;
            try
            {
                await func();
            }
            catch (Exception e)
            {
                actual = e.GetType();
            }

            Assert.Equal(expected, actual);
        }
    }
}
