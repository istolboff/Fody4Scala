using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Fody4Scala
{
    public static class DeepEqualityComparer
    {
        public static IEqualityComparer<T> Default<T>()
        {
            var enumerableOfT = ImplementsGenericInterface(typeof(T), typeof(IEnumerable<>));
            return enumerableOfT == null
                ? EqualityComparer<T>.Default
                : (IEqualityComparer<T>)typeof(DeepEqualityComparer)
                    .GetMethod("ForEnumerables", BindingFlags.Public | BindingFlags.Static)
                    .MakeGenericMethod(enumerableOfT.Single())
                    .Invoke(null, new object[] { });
        }

        public static IEqualityComparer<IEnumerable<T>> ForEnumerables<T>() =>
            new EnumerablesComparer<T>();

        public static bool EquatableReferencesAreEqual<T>(T left, T right) 
            where T : class, IEquatable<T> 
            =>
            ReferenceEquals(left, null) || ReferenceEquals(right, null) 
                ? ReferenceEquals(left, right) 
                : left.Equals(right);

        public static bool TypedCollectionsAreEqual<T>(IEnumerable<T> left, IEnumerable<T> right) 
            =>
            ReferenceEquals(left, null) || ReferenceEquals(right, null)
                ? ReferenceEquals(left, right)
                : left.SequenceEqual(right, Default<T>());

        public static bool UntypedCollectionsAreEqual(IEnumerable left, IEnumerable right) 
            =>
            ReferenceEquals(left, null) || ReferenceEquals(right, null)
                ? ReferenceEquals(left, right)
                : left.Cast<object>().SequenceEqual(right.Cast<object>());

        public static bool ReferenceInstancesAreEqual<T>(T left, T right)
            where T : class
            =>
            ReferenceEquals(left, null) || ReferenceEquals(right, null)
                ? ReferenceEquals(left, right)
                : left.Equals(right);

        public static bool EquatableValuesAreEqual<T>(T left, T right)
            where T : struct, IEquatable<T>
            =>
            left.Equals(right);

        public static bool ValueInstancesAreEqual<T>(T left, T right)
            where T : struct
            =>
            left.Equals(right);

        public static bool EquatableNullablesAreEqual<T>(T? left, T? right)
            where T : struct, IEquatable<T>
            =>
            left == null || right == null
                ? left == null && right == null
                : left.Value.Equals(right.Value);

        public static bool NullablesAreEqual<T>(T? left, T? right)
            where T : struct
            =>
            left == null || right == null
                ? left == null && right == null
                : left.Value.Equals(right.Value);

        public static bool EquatableGenericsAreEqual<T>(T left, T right) 
            where T : IEquatable<T>
        {
            IEquatable<T> eLeft = left;
            IEquatable<T> eRight = right;
            return ReferenceEquals(eLeft, null) || ReferenceEquals(eRight, null)
                ? ReferenceEquals(eLeft, eRight)
                : left.Equals(right);
        }

        public static bool GenericsAreEqual<T>(T left, T right)
        {
            object eLeft = left;
            object eRight = right;
            return ReferenceEquals(eLeft, null) || ReferenceEquals(eRight, null)
                ? ReferenceEquals(eLeft, eRight)
                : eLeft.Equals(eRight);
        }

        private static Type[] ImplementsGenericInterface(Type type, Type interfaceType) =>
            type
                .GetInterfaces()
                .Where(i => i.IsGenericType)
                .Select(i => new { t = i, gt = i.GetGenericTypeDefinition() })
                .FirstOrDefault(item => item.gt == typeof(IEnumerable<>))
                ?.t.GetGenericArguments();

        private class EnumerablesComparer<T> : IEqualityComparer<IEnumerable<T>>
        {
            public bool Equals(IEnumerable<T> x, IEnumerable<T> y) => 
                x == null || y == null
                    ? ReferenceEquals(x, y)
                    : x.SequenceEqual(y, Default<T>());

            public int GetHashCode(IEnumerable<T> obj) =>
                obj?.GetHashCode() ?? 0;
        }
    }
}
