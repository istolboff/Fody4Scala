using System;
using Fody4Scala;

namespace AssemblyToProcess
{
    public abstract class Expression
    {
        [CaseClass]
        public static Expression Variable(string name) { throw new NotImplementedException(); }

        [CaseClass]
        public static Expression Constant<T>(T value) where T : struct { throw new NotImplementedException(); }

        [CaseClass]
        public static Expression Money(decimal amount, string currency) { throw new NotImplementedException(); }

        [CaseClass]
        public static Expression UnaryOperator(string @operator, Expression expression) { throw new NotImplementedException(); }

        [CaseClass]
        public static Expression BinaryOperator(string @operator, Expression leftExpression, Expression rightExpression) { throw new NotImplementedException(); }

        [CaseClass]
        public static Expression LargeTuple(string item1, int item2, decimal item3, double item4, DateTime item5, Guid item6) { throw new NotImplementedException(); }
    }

    public static class Foo
    {
        public static Bar Bar(string item1, int item2, decimal item3, double item4, DateTime item5, Guid item6)
        {
            return new Bar(item1, item2, item3, item4, item5, item6);
        }

        public static Something<T> Something<T>(T value) where T : struct
        {
            return new Something<T>(value);
        }
    }

    public sealed class Bar
    {
        private readonly string item1;
        private readonly int item2;
        private readonly decimal item3;
        private readonly double item4;
        private readonly DateTime item5;
        private readonly Guid item6;

        public DateTime Item5 => item5;

        public Bar(string item1, int item2, decimal item3, double item4, DateTime item5, Guid item6)
        {
            this.item1 = item1;
            this.item2 = item2;
            this.item3 = item3;
            this.item4 = item4;
            this.item5 = item5;
            this.item6 = item6;
        }
    }

    public sealed class Something<T> where T : struct
    {
        private T _backingField;

        public T Value => _backingField;

        public Something(T value)
        {
            _backingField = value;
        }
    }

}
