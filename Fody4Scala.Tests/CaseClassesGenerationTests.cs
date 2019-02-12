using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Fody;
using Fody4Scala.Fody;
using AssemblyToProcess;
using static Fody4Scala.Tests.Make;

namespace Fody4Scala.Tests
{
    [TestClass]
    public class CaseClassesGenerationTests
    {
        [ClassInitialize]
        public static void ApplyFody(TestContext testContext)
        {
            var weavingTask = new ModuleWeaver();
            var weavingResult = weavingTask.ExecuteTestRun("AssemblyToProcess.dll");
            WeavedAssembly = weavingResult.Assembly;
        }

        [TestMethod]
        public void ValidateGeneratedClassesStructure()
        {
            var factoryClassType = WeavedAssembly.GetTypes().Single(ti => ti.Name == nameof(Expression));
            foreach (var factoryMethodName in new[]
                {
                    nameof(Expression.Variable),
                    nameof(Expression.Constant),
                    nameof(Expression.Reference),
                    nameof(Expression.Money),
                    nameof(Expression.UnaryOperator),
                    nameof(Expression.BinaryOperator),
                    nameof(Expression.LargeTuple),
                    nameof(Expression.Func2),
                    nameof(Expression.Maybe)
                })
            {
                var generatedClassType = WeavedAssembly
                    .GetTypes()
                    .Single(
                        ti => ti.Name == factoryMethodName || 
                        ti.Name.StartsWith(factoryMethodName) && ti.Name.Length > factoryMethodName.Length && ti.Name[factoryMethodName.Length] == '`');
                var validator = new CaseClassValidator(generatedClassType);
                validator.EnsureInheritsFrom(factoryClassType);
                validator.EnsureIsSealed();
                validator.EnsureImplementsIEquatable();
                var factoryMethodParameters = factoryClassType.GetMethod(factoryMethodName).GetParameters();
                validator.EnsureHasExpectedPublicConstructor(factoryMethodParameters);
                validator.EnsureHasExpectedPublicProperties(factoryMethodParameters);
                var factoryMethodInfo = factoryClassType.GetMethod(factoryMethodName);
                validator.EnsureFactoryMethodHasCorrectReturnType(factoryMethodInfo);
                validator.EnsureFactoryMethodDoesNotHaveCaseClassAttribute(factoryMethodInfo);
            }
        }

        [TestMethod]
        public void ValidateGeneratedClassesPropertiesValues()
        {
            bool SequencesAreEqual<T>(IEnumerable<T> left, IEnumerable<T> right) =>
                left == null && right == null ||
                left != null && right != null && left.SequenceEqual(right);

            bool UntypedSequencesAreEqual(IEnumerable left, IEnumerable right) =>
                left == null && right == null ||
                left != null && right != null && left.Cast<object>().SequenceEqual(right.Cast<object>());

            Check.NonGenericClasses(
                checkVariable: (variableName, createVariable) => 
                {
                    if (variableName != null)
                    {
                        Assert.AreEqual(typeof(string), createVariable().Name.GetType());
                    }

                    Assert.AreEqual(variableName, (string)createVariable().Name);
                },
                checkMoney: (amount, currencyName, createMoney) =>
                {
                    var money = createMoney();
                    Assert.AreEqual(amount, (decimal)money.Amount);
                    Assert.AreEqual(currencyName, (string)money.Currency);
                },
                checkUnaryOperator: (@operator, expression, createUnaryOperator) => 
                {
                    var unaryOperator = createUnaryOperator();
                    Assert.AreEqual(@operator, (string)unaryOperator.Operator);
                    Assert.AreEqual(expression, unaryOperator.Expression);
                },
                checkBinaryOperator: (@operator, leftExpression, rightExpression, createBinaryOperator) => 
                {
                    var binaryOperator = createBinaryOperator();
                    Assert.AreEqual(@operator, (string)binaryOperator.Operator);
                    Assert.AreEqual(leftExpression, binaryOperator.LeftExpression);
                    Assert.AreEqual(rightExpression, binaryOperator.RightExpression);
                },
                checkLargeTuple: (item1, item2, item3, item4, item5, item6, createLargeTuple) => 
                {
                    var largeTuple = createLargeTuple();
                    Assert.AreEqual(item1, (string)largeTuple.Item1);
                    Assert.AreEqual(item2, (int)largeTuple.Item2);
                    Assert.AreEqual(item3, (decimal)largeTuple.Item3);
                    Assert.AreEqual(item4, (double)largeTuple.Item4);
                    Assert.AreEqual(item5, (DateTime)largeTuple.Item5);
                    Assert.AreEqual(item6, (Guid)largeTuple.Item6);
                },
                checkSimpleCollection: (ints,  dates, strings, decimals, someThings, someOtherThings, createSimpleCollection) => 
                {
                    var simpleCollection = createSimpleCollection();
                    Assert.IsTrue(SequencesAreEqual(ints, (IEnumerable<int>)simpleCollection.Ints));
                    Assert.IsTrue(SequencesAreEqual(dates, (IReadOnlyCollection<DateTime>)simpleCollection.Dates));
                    Assert.IsTrue(SequencesAreEqual(strings, (List<string>)simpleCollection.Strings));
                    Assert.IsTrue(SequencesAreEqual(decimals, (decimal[])simpleCollection.Decimals));
                    Assert.IsTrue(UntypedSequencesAreEqual(someThings, (IEnumerable)simpleCollection.SomeThings));
                    Assert.IsTrue(UntypedSequencesAreEqual(someOtherThings, (ArrayList)simpleCollection.SomeOtherThings));
                });

            var factoryClassType = WeavedAssembly.GetTypes().Single(ti => ti.Name == nameof(Expression));

            // Constant
            var constantMethod = factoryClassType.GetMethod(nameof(Expression.Constant));
            void CheckGenericFactoryMethod<T>(T value)
            {
                dynamic constant = constantMethod.MakeGenericMethod(typeof(T)).Invoke(null, new object[] { value });
                Assert.AreEqual(value, (T)constant.Value);
            }

            CheckGenericFactoryMethod(0);
            CheckGenericFactoryMethod(10L);
            CheckGenericFactoryMethod(100.04M);
            CheckGenericFactoryMethod(DateTime.Now);
            CheckGenericFactoryMethod(Guid.NewGuid());
            CheckGenericFactoryMethod(StringComparison.OrdinalIgnoreCase);

            // Fun2
            dynamic fun2 = factoryClassType.GetMethod(nameof(Expression.Func2))
                .MakeGenericMethod(typeof(decimal), typeof(string), typeof(Dictionary<double, DateTime>))
                .Invoke(null, new object[] { 43.67M, "Meow!", new Dictionary<double, DateTime> { { 1.23, new DateTime(100000) } } });
            Assert.AreEqual(43.67M, (decimal)fun2.Arg1);
            Assert.AreEqual("Meow!", (string)fun2.Arg2);
            Assert.IsTrue(new Dictionary<double, DateTime> { { 1.23, new DateTime(100000) } }.SequenceEqual((Dictionary<double, DateTime>)fun2.Result));
        }

        private abstract class CheckingGenericClassLogic
        {
            public abstract void CheckClassWithSingleParameter<T>(T value, Func<dynamic> makeConstant);

            public abstract void CheckFun2<TArg1, TArg2, TResult>(TArg1 arg1, TArg2 arg2, TResult result, Func<dynamic> makeFun2);
        }

        [TestMethod]
        public void ValidateGeneratedClassesEqualityComparison()
        {
            var variableMakers = new List<Func<dynamic>>();
            var moneyMakers = new List<Func<dynamic>>();
            var unaryOperatorMakers = new List<Func<dynamic>>();
            var binaryOperatorMakers = new List<Func<dynamic>>();
            var largeTupleMakers = new List<Func<dynamic>>();
            var simpleCollectionMakers = new List<Func<dynamic>>();

            Check.NonGenericClasses(
                checkVariable: (_, variable) => variableMakers.Add(variable),
                checkMoney: (_, __, money) => moneyMakers.Add(money),
                checkUnaryOperator: (_, __, unaryOperator) => unaryOperatorMakers.Add(unaryOperator),
                checkBinaryOperator: (_, __, ___, binaryOperator) => binaryOperatorMakers.Add(binaryOperator),
                checkLargeTuple: (_, __, ___, ____, _______, _________, largeTuple) => largeTupleMakers.Add(largeTuple),
                checkSimpleCollection: (_, __, ___, ____, _______, _________, simpleCollection) => simpleCollectionMakers.Add(simpleCollection));

            foreach (var instanceMakers in new[] { variableMakers, moneyMakers, unaryOperatorMakers, binaryOperatorMakers, largeTupleMakers, simpleCollectionMakers })
            {
                Assert.IsTrue(instanceMakers.Count > 1, $"Check.NonGenericClasses() should generate several instances of {instanceMakers.Single()().GetType().Name}");
                foreach (var (makeLeft, makeRight) in from makeLeft in instanceMakers
                                              from makeRight in instanceMakers
                                              select (makeLeft, makeRight))
                {
                    var left = makeLeft();
                    Check.Equality(left, makeRight(), ReferenceEquals(makeLeft, makeRight));
                    Check.Equality(left, makeLeft(), true);
                }
            }
        }

        private static Assembly WeavedAssembly;

        private class CaseClassValidator
        {
            public CaseClassValidator(Type caseClassType) => 
                _caseClassType = caseClassType;

            public void EnsureInheritsFrom(Type baseClassType) => 
                Assert.AreEqual(baseClassType, _caseClassType.BaseType, $"{ClassName} is supposed to inhert from {baseClassType.Name}");

            public void EnsureIsSealed() => 
                Assert.IsTrue(_caseClassType.IsSealed, $"{ClassName} is supposed to be sealed.");

            public void EnsureImplementsIEquatable() =>
                Assert.IsTrue(
                    typeof(IEquatable<>).MakeGenericType(_caseClassType).IsAssignableFrom(_caseClassType),
                    $"{ClassName} is supposed to implement IEquatable<{ClassName}>");

            public void EnsureHasExpectedPublicConstructor(ParameterInfo[] factoryMethodParameters)
            {
                var theOnlyConstructor = _caseClassType.GetConstructors().Single();
                Assert.IsTrue(theOnlyConstructor.IsPublic, $"{ClassName} is supposed to have a single public constructor.");
                Assert.IsTrue(
                    factoryMethodParameters.SequenceEqual(
                        theOnlyConstructor.GetParameters(),
                        new PublicPropertiesBasedEqualityComparer<ParameterInfo>(
                            "Name", "Attributes", "IsIn", "IsRetval", "IsLcid", 
                            "IsOptional", "IsOut", "DefaultValue", "HasDefaultValue")
                        .WithCustomChecker(p => p.ParameterType, TypesAreEqual)),
                    $"The constructor of class {_caseClassType.Name} has incorrect set of parameters.");
            }

            public void EnsureHasExpectedPublicProperties(ParameterInfo[] factoryMethodParameters)
            {
                var publicProperties = _caseClassType.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.GetProperty);
                Assert.IsTrue(
                    factoryMethodParameters.Select(parameter => Pair(PascalCase(parameter.Name), parameter.ParameterType))
                        .SequenceEqual(
                            publicProperties.Select(property => Pair(property.Name, property.PropertyType)),
                            new PublicPropertiesBasedEqualityComparer<KeyValuePair<string, Type>>("Key")
                            .WithCustomChecker(kvp => kvp.Value, TypesAreEqual)));
            }

            public void EnsureFactoryMethodHasCorrectReturnType(MethodInfo methodInfo)
            {
                if (!methodInfo.IsGenericMethod)
                {
                    Assert.AreEqual(
                        methodInfo.ReturnType,
                        _caseClassType,
                        $"Method {methodInfo} should have return type '{_caseClassType.Name}'");
                }
                else
                {
                    Assert.IsTrue(
                        methodInfo.ReturnType.Name == _caseClassType.Name,
                        $"Method {methodInfo} should have return type '{_caseClassType.Name}'");
                    Assert.IsTrue(
                        methodInfo.ReturnType.GetGenericArguments().Select(t => t.Name).SequenceEqual(_caseClassType.GetGenericArguments().Select(t => t.Name)),
                        $"Method {methodInfo}'s generic arguments should be the same with gtheirs of its return type '{_caseClassType.Name}'");
                }
            }

            public void EnsureFactoryMethodDoesNotHaveCaseClassAttribute(MethodInfo methodInfo)
            {
                Assert.IsNull(
                    methodInfo.GetCustomAttribute(typeof(CaseClassAttribute)), 
                    $"{methodInfo.DeclaringType.Name}.{methodInfo.Name} factory method still has '{typeof(CaseClassAttribute).Name}' attribute");
            }

            private string ClassName => _caseClassType.Name;

            private static string PascalCase(string name)
            {
                return char.ToUpper(name.First()).ToString() + name.Substring(1);
            }

            private static bool? TypesAreEqual(Type t1, Type t2)
            {
                return t1.IsGenericParameter ? t2.IsGenericParameter && t1.Name == t2.Name : (bool?)null;
            }

            private readonly Type _caseClassType;
        }

        private static class Check
        {
            public static void NonGenericClasses(
                Action<string, Func<dynamic>> checkVariable,
                Action<decimal, string, Func<dynamic>> checkMoney,
                Action<string, dynamic, Func<dynamic>> checkUnaryOperator,
                Action<string, dynamic, dynamic, Func<dynamic>> checkBinaryOperator,
                Action<string, int, decimal, double?, DateTime, Guid, Func<dynamic>> checkLargeTuple,
                Action<IEnumerable<int>, IReadOnlyCollection<DateTime>, List<string>, decimal[], IEnumerable, ArrayList, Func<dynamic>> checkSimpleCollection)
            {
                var factoryClassType = WeavedAssembly.GetTypes().Single(ti => ti.Name == nameof(Expression));

                // Variable
                var stringValues = new[] { null, string.Empty, " ", "oddName", "Very long string with a lot of text" };
                var variableFactory = factoryClassType.GetMethod(nameof(Expression.Variable));
                foreach (var variableName in stringValues)
                {
                    checkVariable(variableName, () => variableFactory.Invoke(null, new object[] { variableName }));
                }

                // Money
                var moneyFactory = factoryClassType.GetMethod(nameof(Expression.Money));
                foreach (var (amount, currency) in 
                            from amount in new decimal[] { 0, 100, 345.678M, -30000, -4576.67M, decimal.MaxValue, decimal.MinValue }
                            from currency in new[] { null, string.Empty, "USD", "EUR" }
                            select (amount, currency))
                {
                    checkMoney(amount, currency, () => moneyFactory.Invoke(null, new object[] { amount, currency }));
                }

                // UnaryOperator & BinaryOperator
                var unaryOperatorFactory = factoryClassType.GetMethod(nameof(Expression.UnaryOperator));
                var binaryOperatorFactory = factoryClassType.GetMethod(nameof(Expression.BinaryOperator));
                var constantMethod = factoryClassType.GetMethod(nameof(Expression.Constant));
                foreach (var (@operator, leftExpression, rightExpression) in from @operator in stringValues
                                          from leftExpression in new dynamic[]
                                                             {
                                                              null,
                                                              factoryClassType.GetMethod(nameof(Expression.Variable)).Invoke(null, new object[] { "oddName" }),
                                                              constantMethod.MakeGenericMethod(typeof(DateTime)).Invoke(null, new object[] { DateTime.Now })
                                                             }
                                          from rightExpression in new dynamic[]
                                                             {
                                                              null,
                                                              factoryClassType.GetMethod(nameof(Expression.Variable)).Invoke(null, new object[] { "evenName" }),
                                                              constantMethod.MakeGenericMethod(typeof(Guid)).Invoke(null, new object[] { Guid.NewGuid() })
                                                             }
                                          select (@operator, leftExpression, rightExpression))
                {
                    checkUnaryOperator(@operator, leftExpression, new Func<dynamic>(() => unaryOperatorFactory.Invoke(null, new object[] { @operator, leftExpression })));
                    checkBinaryOperator(@operator, leftExpression, rightExpression, new Func<dynamic>(() => binaryOperatorFactory.Invoke(null, new object[] { @operator, leftExpression, rightExpression })));
                }

                // LargeTuple
                var largeTupleFactory = factoryClassType.GetMethod(nameof(Expression.LargeTuple));
                var (item1, item2, item3, item4, item5, item6) = ("Hello!", 42, 146.73M, -256.7454, new DateTime(1000000L), Guid.NewGuid());
                checkLargeTuple(item1, item2, item3, item4, item5, item6, () => largeTupleFactory.Invoke(null, new object[] { item1, item2, item3, item4, item5, item6 }));
                (item1, item2, item3, item4, item5, item6) = ("World?", 2, 456456.98M, -997.1233, new DateTime(3000000L), Guid.NewGuid());
                checkLargeTuple(item1, item2, item3, item4, item5, item6, () => largeTupleFactory.Invoke(null, new object[] { item1, item2, item3, item4, item5, item6 }));

                // Simple Collection
                var simpleCollectionFactory = factoryClassType.GetMethod(nameof(Expression.SimpleCollection));
                var rand = new Random(935630);
                foreach (var (ints, dates, strings, decimals, someThings, someOtherThings) in 
                                        from ints in Enumerable.Range(0, rand.Next(4, 10)).Select(n => Enumerable.Range(0, n).Select(_ => rand.Next()).ToArray())
                                        let dates = ints.Take(rand.Next(3, ints.Length)).Select(v => DateTime.MinValue + TimeSpan.FromDays(v)).ToArray()
                                        let strings = ints.Take(rand.Next(3, ints.Length)).Select(v => $"item-{v}").ToList()
                                        let decimals = ints.Take(rand.Next(3, ints.Length)).Select((v, i) => ((decimal)v - 1)/ ((decimal)v + i + 1)).ToArray()
                                        let someThings = (IEnumerable)ints.Take(rand.Next(3, ints.Length)).Select((v, i) => (object)Pair(Math.PI * v, i))
                                        let someOtherThings = new ArrayList(ints.Take(rand.Next(3, ints.Length)).Select((v, i) => (object)Pair(v, Math.E * i)).ToList())
                                        select (ints, dates, strings, decimals, someThings, someOtherThings))
                {
                    checkSimpleCollection(ints, dates, strings, decimals, someThings, someOtherThings, () => simpleCollectionFactory.Invoke(null, new object[] { ints, dates, strings, decimals, someThings, someOtherThings }));
                }
            }

            public static void GenericClasses(CheckingGenericClassLogic checkingGenericClassLogic)
            {
                var factoryClassType = WeavedAssembly.GetTypes().Single(ti => ti.Name == nameof(Expression));

                void CheckGenericFactoryMethod<T, U>(MethodInfo factoryMethod, params U[] values)
                {
                    MethodInfo concreteFactoryMethod = factoryMethod.MakeGenericMethod(typeof(T));
                    foreach (var value in values)
                    {
                        checkingGenericClassLogic.CheckClassWithSingleParameter(value, () => concreteFactoryMethod.Invoke(null, new object[] { value }));
                    }
                }

                // Constant
                var constantMethod = factoryClassType.GetMethod(nameof(Expression.Constant));

                CheckGenericFactoryMethod<int, int>(constantMethod, 0, 1, -100, int.MaxValue, int.MinValue);
                CheckGenericFactoryMethod<long, long>(constantMethod, 0L, 10L, -10000L, int.MaxValue, int.MinValue, long.MaxValue, long.MinValue);
                CheckGenericFactoryMethod<decimal, decimal>(constantMethod, 0M, 100.04M, -18846.454561M, decimal.MaxValue, decimal.MinValue);
                CheckGenericFactoryMethod<DateTime, DateTime>(constantMethod, DateTime.Now, DateTime.MinValue, DateTime.MaxValue);
                CheckGenericFactoryMethod<Guid, Guid>(constantMethod, Guid.NewGuid(), Guid.Empty);
                CheckGenericFactoryMethod<StringComparison, StringComparison>(constantMethod, StringComparison.OrdinalIgnoreCase, StringComparison.CurrentCulture, StringComparison.InvariantCultureIgnoreCase);

                // Reference
                var referenceMethod = factoryClassType.GetMethod(nameof(Expression.Reference));
                CheckGenericFactoryMethod<string, string>(referenceMethod, string.Empty, "1", "two", null);
                CheckGenericFactoryMethod<ArrayList, ArrayList>(referenceMethod, new ArrayList(), new ArrayList(new[] { 100, 200, 300 }), null);

                // Maybe
                var maybeMethod = factoryClassType.GetMethod(nameof(Expression.Maybe));
                CheckGenericFactoryMethod<int, int?>(referenceMethod, 0, 1, -100, int.MaxValue, int.MinValue, null);
                CheckGenericFactoryMethod<DateTime, DateTime?>(referenceMethod, DateTime.Now, DateTime.MinValue, DateTime.MinValue, null);

                // Fun2
                dynamic fun2 = factoryClassType.GetMethod(nameof(Expression.Func2))
                    .MakeGenericMethod(typeof(decimal), typeof(string), typeof(Dictionary<double, DateTime>))
                    .Invoke(null, new object[] { 43.67M, "Meow!", new Dictionary<double, DateTime> { { 1.23, new DateTime(100000) } } });
                Assert.AreEqual(43.67M, (decimal)fun2.Arg1);
                Assert.AreEqual("Meow!", (string)fun2.Arg2);
                Assert.IsTrue(new Dictionary<double, DateTime> { { 1.23, new DateTime(100000) } }.SequenceEqual((Dictionary<double, DateTime>)fun2.Result));
            }

            public static void Equality(dynamic left, dynamic right, bool expectedToBeEqual)
            {
                Assert.AreEqual(left.GetType(), right.GetType());
                var methodInfo = typeof(Check)
                    .GetMethod(
                        expectedToBeEqual ? "CheckEqualityCore" : "CheckInequalityCore", 
                        BindingFlags.NonPublic | BindingFlags.Static)
                    .MakeGenericMethod(left.GetType());
                methodInfo.Invoke(null, new[] { left, right });
            }

            private static void CheckEqualityCore<T>(T left, T right) where T : IEquatable<T>
            {
                Assert.IsTrue(left.Equals(right));
                Assert.IsTrue(((object)left).Equals(right));
                Assert.IsTrue(CallEqualityOperator(left, right, true));
                Assert.IsFalse(CallEqualityOperator(left, right, false));
            }

            private static void CheckInequalityCore<T>(T left, T right) where T : IEquatable<T>
            {
                Assert.IsFalse(left.Equals(right));
                Assert.IsFalse(((object)left).Equals(right));
                Assert.IsFalse(CallEqualityOperator(left, right, true));
                Assert.IsTrue(CallEqualityOperator(left, right, false));
            }

            private static bool CallEqualityOperator<T>(T left, T right, bool equality)
            {
                return (bool)typeof(T)
                    .GetMethod(equality ? "op_Equality" : "op_Inequality", BindingFlags.Static | BindingFlags.Public)
                    .Invoke(null, new object[] { left, right });
            }
        }
    }
}