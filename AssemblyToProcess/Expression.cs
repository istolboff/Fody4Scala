using System;
using System.Collections;
using System.Collections.Generic;
using Fody4Scala;

namespace AssemblyToProcess
{
    public abstract class Expression
    {
        [CaseClass]
        public static Expression Degenerate() { throw new NotImplementedException(); }

        [CaseClass]
        public static Expression Variable(string name) { throw new NotImplementedException(); }

        [CaseClass]
        public static Expression Constant<T>(T value) where T : struct { throw new NotImplementedException(); }

        [CaseClass]
        public static Expression Reference<T>(T value) where T : class { throw new NotImplementedException(); }

        [CaseClass]
        public static Expression Maybe<T>(T? value) where T : struct { throw new NotImplementedException(); }

        [CaseClass]
        public static Expression Money(decimal amount, string currency) { throw new NotImplementedException(); }

        [CaseClass]
        public static Expression UnaryOperator(string @operator, Expression expression) { throw new NotImplementedException(); }

        [CaseClass]
        public static Expression BinaryOperator(string @operator, Expression leftExpression, Expression rightExpression) { throw new NotImplementedException(); }

        [CaseClass]
        public static Expression LargeTuple(string item1, int item2, decimal item3, double? item4, DateTime item5, Guid item6) { throw new NotImplementedException(); }

        [CaseClass]
        public static Expression Func2<TArg1, TArg2, TResult>(TArg1 arg1, TArg2 arg2, TResult result) { throw new NotImplementedException(); }

        [CaseClass]
        public static Expression SimpleCollection(
            IEnumerable<int> ints, 
            ICollection<DateTime> dates, 
            List<string> strings,
            decimal[] decimals,
            IEnumerable someThings,
            ArrayList someOtherThings) { throw new NotImplementedException(); }

        [CaseClass]
        public static Expression OneOf<T, U>(
            IReadOnlyCollection<T> alternatives,
            IEnumerable<U?> anotherSet,
            ICollection<KeyValuePair<T, U?>> complexOne,
            U[] testArraysOfGenericParameters) where U : struct
        { throw new NotImplementedException(); }

        [CaseClass]
        public static Expression TestArraysOfGenericParameter<U, W>(
            U[] elements,
            W?[] maybeElements,
            IEnumerable<U>[] metaElements,
            IEnumerable<KeyValuePair<int, U[]>>[] deeplyNestedGenerics) where W : struct
        { throw new NotImplementedException(); }

        [CaseClass]
        public static Expression TestEquatable<T>(IEquatable<DateTime> nonGenericOne, IEquatable<T> genericOne) { throw new NotImplementedException(); }
    }

    public interface IUnusualCollection<U> : IEnumerable<U>
    {
        int Length { get; }
    }

    public sealed class Something<T> : IEquatable<Something<T>> where T : class
    {
        private T _k_BackingField;

        public T Value => _k_BackingField;

        public Something(T value)
        {
            _k_BackingField = value;
        }

        public bool Equals(Something<T> other)
        {
            if (!DeepEqualityComparer.ReferenceInstancesAreEqual(_k_BackingField, other._k_BackingField))
            {
                return false;
            }
            return true;
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as Something<T>);
        }
    }
}
