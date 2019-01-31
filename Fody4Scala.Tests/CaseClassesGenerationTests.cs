using System;
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
                    nameof(Expression.UnaryOperator),
                    nameof(Expression.BinaryOperator)
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
                var factoryMethodParameters = factoryClassType.GetMethod(factoryMethodName).GetParameters();
                validator.EnsureHasExpectedPublicConstructor(factoryMethodParameters);
                validator.EnsureHasExpectedPublicProperties(factoryMethodParameters);
                validator.EnsureFactoryMethodHasCorrectReturnType(factoryClassType.GetMethod(factoryMethodName));
            }
        }

        [TestMethod]
        public void ValidateGeneratedClassesBehavior()
        {
            var stringValues = new[] { null, string.Empty, " ", "oddName", "Very long string with a lot of text" };
            var factoryClassType = WeavedAssembly.GetTypes().Single(ti => ti.Name == nameof(Expression));

            foreach (var variableName in stringValues)
            {
                dynamic variable = factoryClassType.GetMethod(nameof(Expression.Variable)).Invoke(null, new object[] { variableName });
                if (variable.Name != null)
                {
                    Assert.AreEqual(typeof(string), variable.Name.GetType());
                }
                Assert.AreEqual(variableName, (string)variable.Name);
            }

            dynamic money = factoryClassType.GetMethod(nameof(Expression.Money)).Invoke(null, new object[] { 100M, "USD" });
            Assert.AreEqual(100M, (decimal)money.Amount);
            Assert.AreEqual("USD", (string)money.Currency);

            var constantMethod = factoryClassType.GetMethod(nameof(Expression.Constant));

            foreach (var arguments in from @operator in stringValues
                                   from expression in new dynamic[] 
                                                      {
                                                          null,
                                                          factoryClassType.GetMethod(nameof(Expression.Variable)).Invoke(null, new object[] { "oddName" }),
                                                          constantMethod.MakeGenericMethod(typeof(DateTime)).Invoke(null, new object[] { DateTime.Now })
                                                      }
                                   from secondExpression in new dynamic[] 
                                                      {
                                                          null,
                                                          factoryClassType.GetMethod(nameof(Expression.Variable)).Invoke(null, new object[] { "evenName" }),
                                                          constantMethod.MakeGenericMethod(typeof(Guid)).Invoke(null, new object[] { Guid.NewGuid() })
                                                      }
                                   select new object[] { @operator, expression, secondExpression })
            {
                dynamic unaryOperator = factoryClassType.GetMethod(nameof(Expression.UnaryOperator)).Invoke(null, arguments.Take(2).ToArray());
                Assert.AreEqual((string)arguments[0], (string)unaryOperator.Operator);
                Assert.AreEqual(arguments[1], unaryOperator.Expression);

                dynamic binaryOperator = factoryClassType.GetMethod(nameof(Expression.BinaryOperator)).Invoke(null, arguments);
                Assert.AreEqual((string)arguments[0], (string)binaryOperator.Operator);
                Assert.AreEqual(arguments[1], binaryOperator.LeftExpression);
                Assert.AreEqual(arguments[2], binaryOperator.RightExpression);
            }

            dynamic largeTuple = factoryClassType.GetMethod(nameof(Expression.LargeTuple)).Invoke(null, new object[] { "Hello!", 42, 146.73M, -256.7454, new DateTime(1000000L), Guid.Empty });
            Assert.AreEqual("Hello!", (string)largeTuple.Item1);
            Assert.AreEqual(42, (int)largeTuple.Item2);
            Assert.AreEqual(146.73M, (decimal)largeTuple.Item3);
            Assert.AreEqual(-256.7454, (double)largeTuple.Item4);
            Assert.AreEqual(new DateTime(1000000L), (DateTime)largeTuple.Item5);
            Assert.AreEqual(Guid.Empty, (Guid)largeTuple.Item6);

            CheckGenericFactoryMethod(constantMethod, 0);
            CheckGenericFactoryMethod(constantMethod, 10L);
            CheckGenericFactoryMethod(constantMethod, 100.04M);
            CheckGenericFactoryMethod(constantMethod, DateTime.Now);
            CheckGenericFactoryMethod(constantMethod, Guid.NewGuid());
            CheckGenericFactoryMethod(constantMethod, StringComparison.OrdinalIgnoreCase);
        }

        private static void CheckGenericFactoryMethod<T>(MethodInfo genericFactoryMethod, T value)
        {
            dynamic constant = genericFactoryMethod.MakeGenericMethod(typeof(T)).Invoke(null, new object[] { value });
            Assert.AreEqual(value, (T)constant.Value);
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
    }
}
