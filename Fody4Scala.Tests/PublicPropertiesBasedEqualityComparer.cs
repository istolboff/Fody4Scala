using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Fody4Scala.Tests
{
    internal sealed class PublicPropertiesBasedEqualityComparer<T> : EqualityComparer<T>
    {
        public PublicPropertiesBasedEqualityComparer(params string[] propertyNames)
        {
            _comparableProperties = typeof(T)
                .GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.GetProperty)
                .Where(pi => !propertyNames.Any() || propertyNames.Contains(pi.Name))
                .ToArray();

            if (propertyNames.Any())
            {
                Assert.AreEqual(propertyNames.Length, _comparableProperties.Length, "Wrong property name(s) specified.");
            }
        }

        public IEqualityComparer<T> WithCustomChecker<TProperty>(Func<T, TProperty> getProperty, Func<TProperty, TProperty, bool?> compare)
        {
            _customCheckers.Add((v1, v2) => compare(getProperty(v1), getProperty(v2)) ?? ValuesAreEqual(getProperty(v1), getProperty(v2)));
            return this;
        }

        public override bool Equals(T x, T y) =>
            _comparableProperties.All(pi => ValuesAreEqual(pi.GetValue(x), pi.GetValue(y))) && 
            _customCheckers.All(checker => checker(x, y));

        public override int GetHashCode(T obj) => 
            obj?.GetHashCode() ?? 0;

        private static bool ValuesAreEqual(object v1, object v2) =>
            ReferenceEquals(v1, null) ? ReferenceEquals(v2, null) : v1.Equals(v2);

        private readonly PropertyInfo[] _comparableProperties;
        private readonly List<Func<T, T, bool>> _customCheckers = new List<Func<T, T, bool>>();
    }
}
