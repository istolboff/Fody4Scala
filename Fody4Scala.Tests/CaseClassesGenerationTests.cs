using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
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
            var weavingResult = weavingTask.ExecuteTestRun("AssemblyToProcess.dll", afterExecuteCallback: moduleDefinition =>
            {
                const string fody4ScalaDll = "Fody4Scala.dll";
                string binaryFolderPath = Path.GetDirectoryName(moduleDefinition.FileName);
                var fody4ScalaAssemblyPath = Path.Combine(binaryFolderPath, fody4ScalaDll);
                var tempFodyFolderPath = Path.Combine(binaryFolderPath, "fodytemp");
                File.Copy(fody4ScalaAssemblyPath, Path.Combine(tempFodyFolderPath, fody4ScalaDll));
            });
            _weavedAssembly = weavingResult.Assembly;
        }

        [TestMethod]
        public void ValidateGeneratedClassesStructure()
        {
            var factoryClassType = _weavedAssembly.GetTypes().Single(ti => ti.Name == nameof(Expression));
            foreach (var factoryMethodName in new[]
                {
                    nameof(Expression.Degenerate),
                    nameof(Expression.Variable),
                    nameof(Expression.Constant),
                    nameof(Expression.Reference),
                    nameof(Expression.Money),
                    nameof(Expression.UnaryOperator),
                    nameof(Expression.BinaryOperator),
                    nameof(Expression.LargeTuple),
                    nameof(Expression.Func2),
                    nameof(Expression.Maybe),
                    nameof(Expression.OneOf),
                    nameof(Expression.SimpleCollection),
                    nameof(Expression.TestArraysOfGenericParameter),
                    nameof(Expression.TestEquatable)
                })
            {
                var generatedClassType = _weavedAssembly
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
                checkDegenerate: (createDegenerate) => {},
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

            Check.GenericClasses(new CheckingGenericClassProperties());
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
                checkDegenerate: _ => { },
                checkVariable: (_, variable) => variableMakers.Add(variable),
                checkMoney: (_, __, money) => moneyMakers.Add(money),
                checkUnaryOperator: (_, __, unaryOperator) => unaryOperatorMakers.Add(unaryOperator),
                checkBinaryOperator: (_, __, ___, binaryOperator) => binaryOperatorMakers.Add(binaryOperator),
                checkLargeTuple: (_, __, ___, ____, _______, _________, largeTuple) => largeTupleMakers.Add(largeTuple),
                checkSimpleCollection: (_, __, ___, ____, _______, _________, simpleCollection) => simpleCollectionMakers.Add(simpleCollection));

            foreach (var instanceMakers in new[] { variableMakers, moneyMakers, unaryOperatorMakers, binaryOperatorMakers, largeTupleMakers, simpleCollectionMakers })
            {
                Assert.IsTrue(instanceMakers.Count > 1, $"Check.NonGenericClasses() should generate several instances of {instanceMakers.First()().GetType().Name}");
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

        [TestMethod]
        public void ValidateGeneratedClassesToString()
        {
            Check.NonGenericClasses(
                checkDegenerate: (createDegenerate) => 
                    Assert.AreEqual("Degenerate()", createDegenerate().ToString()),
                checkVariable: (variableName, createVariable) =>
                    Assert.AreEqual($"Variable(name: {variableName})", createVariable().ToString()),
                checkMoney: (amount, currencyName, createMoney) =>
                    Assert.AreEqual($"Money(amount: {amount}, currency: {currencyName})", createMoney().ToString()),
                checkUnaryOperator: (@operator, expression, createUnaryOperator) =>
                    Assert.AreEqual($"UnaryOperator(operator: {@operator}, expression: {expression})", createUnaryOperator().ToString()),
                checkBinaryOperator: (@operator, leftExpression, rightExpression, createBinaryOperator) =>
                    Assert.AreEqual(
                        $"BinaryOperator(operator: {@operator}, leftExpression: {leftExpression}, rightExpression: {rightExpression})", 
                        createBinaryOperator().ToString()),
                checkLargeTuple: (item1, item2, item3, item4, item5, item6, createLargeTuple) =>
                    Assert.AreEqual(
                        $"LargeTuple(item1: {item1}, item2: {item2}, item3: {item3}, item4: {item4}, item5: {item5}, item6: {item6})",
                        createLargeTuple().ToString()),
                checkSimpleCollection: (ints, dates, strings, decimals, someThings, someOtherThings, createSimpleCollection) =>
                    Assert.AreEqual(
                        $"SimpleCollection(ints: {ints}, dates: {dates}, strings: {strings}, decimals: {decimals}, someThings: {someThings}, someOtherThings: {someOtherThings})",
                        createSimpleCollection().ToString()));

            Check.GenericClasses(new CheckingGenericClassToString());
        }

        private static Assembly _weavedAssembly;

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
                if (t1.IsGenericParameter)
                {
                    return t2.IsGenericParameter && t1.Name == t2.Name;
                }

                if (t1.IsArray)
                {
                    return t2.IsArray 
                        ? TypesAreEqual(t1.GetElementType(), t2.GetElementType())
                        : false; 
                }

                if (t1.IsConstructedGenericType)
                {
                    return ReferenceEquals(t1, t2) ||
                           t2.IsConstructedGenericType &&
                           t1.Name == t2.Name &&
                           t1.Namespace == t2.Namespace &&
                           t1.Assembly == t2.Assembly &&
                           t1.GenericTypeArguments.Select(ga => ga.FullName).SequenceEqual(
                               t2.GenericTypeArguments.Select(ga => ga.FullName));
                }

                return null;
            }

            private readonly Type _caseClassType;
        }

        private static class Check
        {
            public static void NonGenericClasses(
                Action<Func<dynamic>> checkDegenerate,
                Action<string, Func<dynamic>> checkVariable,
                Action<decimal, string, Func<dynamic>> checkMoney,
                Action<string, dynamic, Func<dynamic>> checkUnaryOperator,
                Action<string, dynamic, dynamic, Func<dynamic>> checkBinaryOperator,
                Action<string, int, decimal, double?, DateTime, Guid, Func<dynamic>> checkLargeTuple,
                Action<IEnumerable<int>, IReadOnlyCollection<DateTime>, List<string>, decimal[], IEnumerable, ArrayList, Func<dynamic>> checkSimpleCollection)
            {
                var factoryClassType = _weavedAssembly.GetTypes().Single(ti => ti.Name == nameof(Expression));

                // Degenerate
                var degenerateFactory = factoryClassType.GetMethod(nameof(Expression.Degenerate));
                checkDegenerate(() => degenerateFactory.Invoke(null, new object[0]));
                checkDegenerate(() => degenerateFactory.Invoke(null, new object[0]));

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
                            from amount in new[] { 0, 100, 345.678M, -30000, -4576.67M, decimal.MaxValue, decimal.MinValue }
                            from currency in new[] { null, string.Empty, "USD", "EUR" }
                            select (amount, currency))
                {
                    checkMoney(amount, currency, () => moneyFactory.Invoke(null, new object[] { amount, currency }));
                }

                // UnaryOperator & BinaryOperator
                var unaryOperatorFactory = factoryClassType.GetMethod(nameof(Expression.UnaryOperator));
                var binaryOperatorFactory = factoryClassType.GetMethod(nameof(Expression.BinaryOperator));
                var constantMethod = factoryClassType.GetMethod(nameof(Expression.Constant));
                foreach (var (@operator, leftExpression) in
                                          from @operator in stringValues
                                          from leftExpression in new dynamic[]
                                                             {
                                                              null,
                                                              factoryClassType.GetMethod(nameof(Expression.Variable)).Invoke(null, new object[] { "oddName" }),
                                                              constantMethod.MakeGenericMethod(typeof(DateTime)).Invoke(null, new object[] { DateTime.Now })
                                                             }
                                          select (@operator, leftExpression))
                {
                    checkUnaryOperator(@operator, leftExpression, new Func<dynamic>(() => unaryOperatorFactory.Invoke(null, new object[] { @operator, leftExpression })));
                }

                foreach (var (@operator, leftExpression, rightExpression) in 
                                          from @operator in stringValues
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
                    checkBinaryOperator(@operator, leftExpression, rightExpression, new Func<dynamic>(() => binaryOperatorFactory.Invoke(null, new object[] { @operator, leftExpression, rightExpression })));
                }

                // LargeTuple
                var largeTupleFactory = factoryClassType.GetMethod(nameof(Expression.LargeTuple));
                var (item1, item2, item3, item4, item5, item6) = ("Hello!", 42, 146.73M, -256.7454, new DateTime(1000000L), Guid.NewGuid());
                var createdValue1 = largeTupleFactory.Invoke(null, new object[] { item1, item2, item3, item4, item5, item6 });
                checkLargeTuple(item1, item2, item3, item4, item5, item6, () => createdValue1);
                (item1, item2, item3, item4, item5, item6) = ("World?", 2, 456456.98M, -997.1233, new DateTime(3000000L), Guid.NewGuid());
                var createdValue2 = largeTupleFactory.Invoke(null, new object[] { item1, item2, item3, item4, item5, item6 });
                checkLargeTuple(item1, item2, item3, item4, item5, item6, () => createdValue2);

                // Simple Collection
                var simpleCollectionFactory = factoryClassType.GetMethod(nameof(Expression.SimpleCollection));
                var rand = new Random(935630);
                foreach (var (ints, dates, strings, decimals, someThings, someOtherThings) in
                                        from ints in Enumerable.Range(0, 5).Select(_ => Enumerable.Range(0, rand.Next(4, 10)).Select(__ => rand.Next()).ToArray())
                                        let dates = ints.Take(rand.Next(3, ints.Length)).Select(v => DateTime.MinValue + TimeSpan.FromDays(v % 100)).ToArray()
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
                var factoryClassType = _weavedAssembly.GetTypes().Single(ti => ti.Name == nameof(Expression));

                void CheckGenericFactoryMethod<T, TU>(MethodInfo factoryMethod, params TU[] values)
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
                CheckGenericFactoryMethod<int, int?>(maybeMethod, 0, 1, -100, int.MaxValue, int.MinValue, null);
                CheckGenericFactoryMethod<DateTime, DateTime?>(maybeMethod, DateTime.Now, DateTime.MinValue, DateTime.MinValue, null);

                // Fun2
                dynamic fun2 = factoryClassType.GetMethod(nameof(Expression.Func2))
                    .MakeGenericMethod(typeof(decimal), typeof(string), typeof(Dictionary<double, DateTime>));
                foreach (var (arg1, arg2, result) in new[] 
                                        {
                                            (43.67M, "Meow!", new Dictionary<double, DateTime> { { 1.23, new DateTime(100000) } }),
                                            (-1534.104M, "Arf?", new Dictionary<double, DateTime> { { 6.45, new DateTime(200000) } })
                                        })
                {
                    checkingGenericClassLogic.CheckFun2(arg1, arg2, result, () => fun2.Invoke(null, new object[] { arg1, arg2, result }));
                }
            }

            public static void Equality(dynamic left, dynamic right, bool expectedToBeEqual)
            {
                Assert.AreEqual(left.GetType(), right.GetType());
                var genericMethodInfo = typeof(Check)
                    .GetMethod(
                        expectedToBeEqual ? nameof(CheckEqualityCore) : nameof(CheckInequalityCore),
                        BindingFlags.NonPublic | BindingFlags.Static);
                Assert.IsNotNull(genericMethodInfo, "Make ReSharper happy.");
                var concreteMethodInfo = genericMethodInfo.MakeGenericMethod(left.GetType());
                concreteMethodInfo.Invoke(null, new[] { left, right });
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
                var operatorMethod = typeof(T)
                    .GetMethod(equality ? "op_Equality" : "op_Inequality", BindingFlags.Static | BindingFlags.Public);
                Assert.IsNotNull(operatorMethod, $"{typeof(T).Name}.operator{(equality ? "==" : "!=")} should be implemented.");
                return (bool)operatorMethod.Invoke(null, new object[] { left, right });
            }
        }

        private abstract class CheckingGenericClassLogic
        {
            public abstract void CheckClassWithSingleParameter<T>(T value, Func<dynamic> makeInstance);

            public abstract void CheckFun2<TArg1, TArg2, TResult>(TArg1 arg1, TArg2 arg2, TResult result, Func<dynamic> makeFun2);
        }

        private sealed class CheckingGenericClassProperties : CheckingGenericClassLogic
        {
            public override void CheckClassWithSingleParameter<T>(T value, Func<dynamic> makeInstance)
            {
                var instance = makeInstance();
                Assert.AreEqual(value, instance.Value);
            }

            public override void CheckFun2<TArg1, TArg2, TResult>(TArg1 arg1, TArg2 arg2, TResult result, Func<dynamic> makeFun2)
            {
                var fun2 = makeFun2();
                Assert.AreEqual(arg1, fun2.Arg1);
                Assert.AreEqual(arg2, fun2.Arg2);
                Assert.IsTrue(DeepEqualityComparer.Default<TResult>().Equals(result, fun2.Result));
            }
        }

        private sealed class CheckingGenericClassToString : CheckingGenericClassLogic
        {
            public override void CheckClassWithSingleParameter<T>(T value, Func<dynamic> makeInstance)
            {
                var instance = makeInstance();
                Assert.AreEqual($"{instance.GetType().Name}(value: {value})", instance.ToString());
            }

            public override void CheckFun2<TArg1, TArg2, TResult>(TArg1 arg1, TArg2 arg2, TResult result, Func<dynamic> makeFun2)
            {
                Assert.AreEqual($"Func2`3(arg1: {arg1}, arg2: {arg2}, result: {result})", makeFun2().ToString());
            }
        }
    }
}