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

    public sealed class Something<T> : Expression, IEquatable<Something<T>> where T : struct
    {
        private T k_BackingField;
        private int k_BackingField1;
        private Guid k_BackingField2;

        public T Value => k_BackingField;

        public Something(T value)
        {
            k_BackingField = value;
        }

        public bool Equals(Something<T> other)
        {
            if (ReferenceEquals(other, null))
            {
                return false;
            }
            if (!DeepEqualityComparer.ValueInstancesAreEqual(k_BackingField, other.k_BackingField))
            {
                return false;
            }
            return true;
        }

        public override string ToString()
        {
            return $"Something(f1: {k_BackingField}, f2:{k_BackingField1}, f3:{k_BackingField2}, f4:{100}, f5:{400M}, f6:{4.565765}, f7: {455}, f8:{-566}, f9:{5555})";
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as Something<T>);
        }

        public static bool operator ==(Something<T> left, Something<T> right)
        {
            return DeepEqualityComparer.EquatableReferencesAreEqual(left, right);
        }

        public static bool operator !=(Something<T> left, Something<T> right)
        {
            if (!DeepEqualityComparer.EquatableReferencesAreEqual(left, right))
            {
                return true;
            }
            return false;
        }
    }
}
