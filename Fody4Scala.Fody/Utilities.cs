using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using Mono.Collections.Generic;

namespace Fody4Scala.Fody
{
    internal sealed class WellKnownTypes
    {
        public WellKnownTypes(ModuleDefinition moduleDefinition, Func<string, TypeDefinition> findType)
        {
            GenericIEquatable = moduleDefinition.ImportReference(findType(typeof(IEquatable<>).FullName));
            ValueType = moduleDefinition.ImportReference(findType(typeof(ValueType).FullName));
        }

        public TypeReference GenericIEquatable { get; }

        public TypeReference ValueType { get; }
    }

    internal sealed class GenericIEquatableInterface
    {
        public GenericIEquatableInterface(WellKnownTypes types)
        {
            _types = types;
        }

        public bool IsImplementedBy(TypeReference typeReference)
        {
            return typeReference is GenericParameter genericParameter
                ? new Constraints(genericParameter, _types).Equatable
                : IsImplementedBy(typeReference.Resolve());
        }

        public bool IsImplementedBy(TypeDefinition typeDefinition)
        {
            var concreteInterface = _types.GenericIEquatable.MakeGenericInstanceType(typeDefinition);
            return typeDefinition.Interfaces.Any(i => i.InterfaceType.FullName == concreteInterface.FullName);
        }

        public TypeReference MarkAsImplementedBy(TypeDefinition classDefinition)
        {
            var concreteClassType = classDefinition.AsConcreteTypeReference();
            var concreteInterface = _types.GenericIEquatable.MakeGenericInstanceType(concreteClassType);
            classDefinition.Interfaces.Add(new InterfaceImplementation(concreteInterface));
            return concreteClassType;
        }

        private readonly WellKnownTypes _types;
    }

    internal class Constraints
    {
        public Constraints(GenericParameter genericParameter, WellKnownTypes wellKnownTypes)
        {
            _types = wellKnownTypes;
            _constraints = genericParameter.Constraints;
            ReferenceType = genericParameter.HasReferenceTypeConstraint;
        }

        public bool ReferenceType { get; }

        public bool ValueType
        {
            get => _constraints.Any(c => c.FullName == _types.ValueType.FullName && c.Namespace == _types.ValueType.Namespace);
        }

        public bool Equatable
        {
            get
            {
                var iEquatableInterface = new GenericIEquatableInterface(_types);
                return _constraints.Any(c => iEquatableInterface.IsImplementedBy(c));
            }
        }

        private readonly Collection<TypeReference> _constraints;
        private readonly WellKnownTypes _types;
    }

    internal static class AdhocExtensions
    {
        public static void AddRange<T>(this Collection<T> @this, IEnumerable<T> elements)
        {
            if (@this is List<T> list)
            {
                list.AddRange(elements);
                return;
            }

            foreach (var element in elements)
            {
                @this.Add(element);
            }
        }

        public static string PascalCase(this string @this)
        {
            return char.ToUpper(@this.First()).ToString() + @this.Substring(1);
        }

        public static TypeReference AsConcreteTypeReference(this TypeReference @this)
        {
            return @this.HasGenericParameters
                ? @this.MakeGenericInstanceType(@this.GenericParameters.ToArray())
                : @this;
        }

        public static MethodReference MakeGenericInstanceConstructor(this GenericInstanceType @this)
        {
            var resolvedType = @this.Resolve().GetConstructors().Single();
            var result = new MethodReference(resolvedType.Name, resolvedType.ReturnType, @this)
            {
                HasThis = resolvedType.HasThis,
                ExplicitThis = resolvedType.ExplicitThis,
                CallingConvention = resolvedType.CallingConvention
            };
            result.Parameters.AddRange(resolvedType.Parameters.Select(parameter => new ParameterDefinition(parameter.ParameterType)));
            result.GenericParameters.AddRange(resolvedType.GenericParameters.Select(parameter => new GenericParameter(parameter.Name, result)));

            return result;
        }

        public static GenericParameter CloneWith(this GenericParameter @this, IGenericParameterProvider genericParameterProvider)
        {
            var result = new GenericParameter(@this.Name, genericParameterProvider)
            {
                HasReferenceTypeConstraint = @this.HasReferenceTypeConstraint,
                IsContravariant = @this.IsContravariant,
                IsCovariant = @this.IsCovariant,
                IsNonVariant = @this.IsNonVariant,
                IsValueType = @this.IsValueType,
                Attributes = @this.Attributes,
                HasNotNullableValueTypeConstraint = @this.HasNotNullableValueTypeConstraint,
                HasDefaultConstructorConstraint = @this.HasDefaultConstructorConstraint
            };
            result.CustomAttributes.AddRange(@this.CustomAttributes);
            result.Constraints.AddRange(@this.Constraints);
            return result;
        }

        public static TypeReference SubstituteGenericParameters(
            this GenericInstanceType @this,
            IEnumerable<GenericParameter> genericParameters,
            ModuleDefinition moduleDefinition)
        {
            var genericType = moduleDefinition.ImportReference(@this.Resolve());
            Debug.Assert(genericType.GenericParameters.Count == @this.GenericArguments.Count);
            var substitutedGenericParameters = Enumerable
                .Range(0, genericType.GenericParameters.Count)
                .Select(i =>
                {
                    var argument = @this.GenericArguments[i];
                    var parameter = genericType.GenericParameters[i];

                    if (argument.IsGenericParameter)
                    {
                        Debug.Assert(genericParameters.Any(gp => gp.FullName == argument.FullName));
                        return genericParameters.Single(gp => gp.FullName == argument.FullName);
                    }

                    if (argument is ArrayType arrayType)
                    {
                        return arrayType.SubstituteGenericParameters(genericParameters, moduleDefinition);
                    }

                    if (argument.IsGenericInstance && argument is GenericInstanceType genericInstanceType)
                    {
                        return SubstituteGenericParameters(genericInstanceType, genericParameters, moduleDefinition);
                    }

                    return argument;
                })
                .ToArray();

            return genericType.MakeGenericInstanceType(substitutedGenericParameters);
        }

        public static ArrayType SubstituteGenericParameters(
            this ArrayType @this,
            IEnumerable<GenericParameter> genericParameters,
            ModuleDefinition moduleDefinition)
        {
            if (@this.ElementType.IsGenericParameter)
            {
                return new ArrayType(genericParameters.Single(gp => gp.FullName == @this.ElementType.FullName));
            }

            if (@this.ElementType is ArrayType arrayType)
            {
                return new ArrayType(arrayType.SubstituteGenericParameters(genericParameters, moduleDefinition));
            }

            if (@this.ElementType is GenericInstanceType genericInstanceType)
            {
                return new ArrayType(genericInstanceType.SubstituteGenericParameters(genericParameters, moduleDefinition));
            }

            return @this;
        }

        public static ParameterDefinition ChangeType(this ParameterDefinition @this, TypeReference typeReference)
        {
            var result = new ParameterDefinition(@this.Name, @this.Attributes, typeReference)
            {
                IsOptional = @this.IsOptional,
                IsLcid = @this.IsLcid,
                IsIn = @this.IsIn,
                MarshalInfo = @this.MarshalInfo,
                HasDefault = @this.HasDefault,
                HasFieldMarshal = @this.HasFieldMarshal,
                MetadataToken = @this.MetadataToken
            };
            if (@this.Constant != null)
            {
                result.Constant = @this.Constant;
            }

            result.CustomAttributes.AddRange(@this.CustomAttributes);
            return result;
        }

        public static void EmitLoadNthArgument(this ILProcessor @this, int i, IList<ParameterDefinition> arguments, bool fromStaticMethod)
        {
            var actualArgumentIndex = fromStaticMethod ? i : i + 1;
            if (actualArgumentIndex < FirstArguments.Length)
            {
                @this.Emit(FirstArguments[actualArgumentIndex]);
            }
            else
            {
                @this.Emit(OpCodes.Ldarg_S, arguments[i]);
            }
        }

        private static readonly OpCode[] FirstArguments = new[] { OpCodes.Ldarg_0, OpCodes.Ldarg_1, OpCodes.Ldarg_2, OpCodes.Ldarg_3 };
    }
}
