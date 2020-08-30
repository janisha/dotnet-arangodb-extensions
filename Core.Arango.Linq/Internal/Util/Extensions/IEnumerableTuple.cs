﻿using System;
using System.Collections.Generic;
using System.Linq;

namespace Core.Arango.Linq.Internal.Util.Extensions
{
    internal static class IEnumerableTupleExtensions
    {
        public static void AddRangeTo<T1, T2>(this IEnumerable<(T1, T2)> src, IDictionary<T1, T2> dict)
        {
            dict.AddRange(src);
        }

        public static IEnumerable<(T1, T2)> ForEachT<T1, T2>(this IEnumerable<(T1, T2)> src, Action<T1, T2> action)
        {
            return src.ForEach(x => action(x.Item1, x.Item2));
        }

        public static IEnumerable<(T1, T2)> ForEachT<T1, T2>(this IEnumerable<(T1, T2)> src, Action<T1, T2, int> action)
        {
            return src.ForEach((x, index) => action(x.Item1, x.Item2, index));
        }

        public static IEnumerable<(T1, T2, T3)> ForEachT<T1, T2, T3>(this IEnumerable<(T1, T2, T3)> src,
            Action<T1, T2, T3> action)
        {
            return src.ForEach(x => action(x.Item1, x.Item2, x.Item3));
        }

        public static IEnumerable<(T1, T2, T3, T4)> ForEachT<T1, T2, T3, T4>(this IEnumerable<(T1, T2, T3, T4)> src,
            Action<T1, T2, T3, T4> action)
        {
            return src.ForEach(x => action(x.Item1, x.Item2, x.Item3, x.Item4));
        }

        public static string Joined<T1, T2>(this IEnumerable<(T1, T2)> src, string delimiter,
            Func<T1, T2, string> selector)
        {
            return src.Joined(delimiter, x => selector(x.Item1, x.Item2));
        }

        public static string Joined<T1, T2>(this IEnumerable<(T1, T2)> src, string delimiter,
            Func<T1, T2, int, string> selector)
        {
            return src.Joined(delimiter, (x, index) => selector(x.Item1, x.Item2, index));
        }

        public static IEnumerable<T2> Item2s<T1, T2>(this IEnumerable<(T1, T2)> src)
        {
            return src.Select(x => x.Item2);
        }

        public static IEnumerable<TResult> SelectT<T1, T2, TResult>(this IEnumerable<ValueTuple<T1, T2>> src,
            Func<T1, T2, TResult> selector)
        {
            return src.Select(x => selector(x.Item1, x.Item2));
        }

        public static IEnumerable<TResult> SelectT<T1, T2, T3, TResult>(this IEnumerable<ValueTuple<T1, T2, T3>> src,
            Func<T1, T2, T3, TResult> selector)
        {
            return src.Select(x => selector(x.Item1, x.Item2, x.Item3));
        }

        public static IEnumerable<TResult> SelectT<T1, T2, T3, T4, TResult>(
            this IEnumerable<ValueTuple<T1, T2, T3, T4>> src, Func<T1, T2, T3, T4, TResult> selector)
        {
            return src.Select(x => selector(x.Item1, x.Item2, x.Item3, x.Item4));
        }

        public static IEnumerable<(T1, T2)> WhereT<T1, T2>(this IEnumerable<(T1, T2)> src, Func<T1, T2, bool> predicate)
        {
            return src.Where(x => predicate(x.Item1, x.Item2));
        }
    }
}