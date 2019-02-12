using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using Fody;
using Mono.Collections.Generic;

namespace Fody4Scala.Fody
{
    public class ModuleWeaver : BaseModuleWeaver
    {
        public override void Execute()
        {
            foreach (var (caseClassesFactory, factoryMethods) in GetAllCaseClassFactoryMethodsGroupedByOwningClass())
            {
                var caseClassBuilder = new CaseClassBuilder(caseClassesFactory, ModuleDefinition, TypeSystem, FindType);
                foreach (var factoryMethod in factoryMethods)
                {
                    var caseClassTypeDefinition = caseClassBuilder.BuildCaseClass(factoryMethod);
                    ModuleDefinition.Types.Add(caseClassTypeDefinition);
                    AdjustFactoryMethod(factoryMethod, caseClassTypeDefinition);
                }
            }
        }

        public override IEnumerable<string> GetAssembliesForScanning() =>
            new[] { "mscorlib", "Fody4Scala" };

        private IEnumerable<(TypeDefinition CaseClassesFactory, MethodDefinition[] FactoryMethods)> 
            GetAllCaseClassFactoryMethodsGroupedByOwningClass() 
        =>
            from @class in ModuleDefinition.GetTypes().Where(type => type.IsClass)
            let factoryMethods = @class.Methods
                                    .Where(method => MethodHasCustomAttribute(method, typeof(CaseClassAttribute)))
                                    .ToArray()
            where factoryMethods.Any()
            select (@class, factoryMethods);

        private bool MethodHasCustomAttribute(MethodDefinition method, Type attributeType) =>
            method.CustomAttributes.Any(attribute => attribute.AttributeType.FullName == attributeType.FullName);

        private void AdjustFactoryMethod(MethodDefinition caseClassFactoryMethod, TypeDefinition caseClassTypeDefinition)
        {
            GenerateCaseClassConstructionCode(caseClassFactoryMethod, caseClassTypeDefinition);
            RemoveCaseClassAttribute(caseClassFactoryMethod.CustomAttributes);
        }

        private static void GenerateCaseClassConstructionCode(MethodDefinition caseClassFactoryMethod, TypeDefinition caseClassTypeDefinition)
        {
            MethodReference constructor;
            if (!caseClassFactoryMethod.GenericParameters.Any())
            {
                caseClassFactoryMethod.ReturnType = caseClassTypeDefinition;
                constructor = caseClassTypeDefinition.GetConstructors().Single();
            }
            else
            {
                var genericInstanceType = caseClassTypeDefinition.MakeGenericInstanceType(caseClassFactoryMethod.GenericParameters.ToArray());
                caseClassFactoryMethod.ReturnType = genericInstanceType;
                constructor = genericInstanceType.MakeGenericInstanceConstructor();
            }

            caseClassFactoryMethod.Body.Instructions.Clear();
            var factoryBodyEmitter = caseClassFactoryMethod.Body.GetILProcessor();

            for (var i = 0; i != caseClassFactoryMethod.Parameters.Count; ++i)
            {
                factoryBodyEmitter.EmitLoadNthArgument(i, caseClassFactoryMethod.Parameters, caseClassFactoryMethod.IsStatic);
            }

            factoryBodyEmitter.Emit(OpCodes.Newobj, constructor);
            factoryBodyEmitter.Emit(OpCodes.Ret);
        }

        private void RemoveCaseClassAttribute(ICollection<CustomAttribute> customAttributes)
        {
            customAttributes.Remove(
                customAttributes.Single(attr => attr.AttributeType.Name == typeof(CaseClassAttribute).Name));
        }

        private class CaseClassBuilder
        {
            public CaseClassBuilder(
                TypeDefinition caseClassesFactory, 
                ModuleDefinition moduleDefinition,
                global::Fody.TypeSystem typeSystem,
                Func<string, TypeDefinition> findType)
            {
                _factoryType = caseClassesFactory;
                _moduleDefinition = moduleDefinition;
                _typeSystem = typeSystem;
                _findType = findType;
            }

            public TypeDefinition BuildCaseClass(MethodDefinition factoryMethod)
            {
                var caseClassTypeDefinition = new TypeDefinition(
                    _factoryType.Namespace,
                    BuildCaseClassName(factoryMethod),
                    _factoryType.Attributes & (TypeAttributes.VisibilityMask | TypeAttributes.LayoutMask) | TypeAttributes.Sealed,
                    _factoryType);

                caseClassTypeDefinition.GenericParameters.AddRange(
                    factoryMethod.GenericParameters
                        .Select(genericParameter => CloneGenericClassParameter(genericParameter, caseClassTypeDefinition)));

                var genericInstanceType = factoryMethod.GenericParameters.Any()
                        ? caseClassTypeDefinition.MakeGenericInstanceType(caseClassTypeDefinition.GenericParameters.ToArray())
                        : null;

                var properties = factoryMethod.Parameters.Select((p, i) =>
                    new CaseClassProperty(p, i, genericInstanceType, caseClassTypeDefinition.GenericParameters)).ToArray();

                caseClassTypeDefinition.Fields.AddRange(properties.Select(p => p.BackingField));
                caseClassTypeDefinition.Properties.AddRange(properties.Select(p => p.Property));
                caseClassTypeDefinition.Methods.AddRange(properties.Select(p => p.PropertyGetter));
                caseClassTypeDefinition.Methods.Add(GenerateConstructor(properties));

                ImplementIEquatable(caseClassTypeDefinition, properties);

                return caseClassTypeDefinition;
            }

            private MethodDefinition GenerateConstructor(CaseClassProperty[] properties)
            {
                var ctor = new MethodDefinition(
                    ".ctor",
                    MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
                    _typeSystem.VoidReference);

                ctor.Parameters.AddRange(properties.Select(p => p.CtorParameter));

                var ctorBodyEmitter = ctor.Body.GetILProcessor();
                CallBaseConstructor(ctorBodyEmitter);
                foreach (var p in properties)
                {
                    p.InitializeBackingFieldValue(ctorBodyEmitter, ctor.Parameters);
                }

                ctorBodyEmitter.Emit(OpCodes.Ret);
                return ctor;
            }

            private void ImplementIEquatable(TypeDefinition caseClassTypeDefinition, CaseClassProperty[] properties)
            {
                var genericIEquatable = _moduleDefinition.ImportReference(_findType("System.IEquatable`1"));
                var genericIEnumerable = _moduleDefinition.ImportReference(_findType("System.Collections.Generic.IEnumerable`1"));
                var concreteCaseClassType = caseClassTypeDefinition.AsConcreteTypeReference();
                var concreteIEquatable = genericIEquatable.MakeGenericInstanceType(concreteCaseClassType);
                caseClassTypeDefinition.Interfaces.Add(new InterfaceImplementation(concreteIEquatable));

                var method = new MethodDefinition(
                    "Equals", 
                    MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.Virtual, 
                    _typeSystem.BooleanReference);

                ParameterDefinition otherInstance = new ParameterDefinition("other", ParameterAttributes.None, concreteCaseClassType);
                method.Parameters.Add(otherInstance);
                var compareCodeEmitter = method.Body.GetILProcessor();
                foreach (var property in properties)
                {
                    property.EmitEqualityCheck(compareCodeEmitter, otherInstance, genericIEquatable, genericIEnumerable);
                }

                compareCodeEmitter.Emit(OpCodes.Ldc_I4_1);
                compareCodeEmitter.Emit(OpCodes.Ret);

                caseClassTypeDefinition.Methods.Add(method);
            }

            private void CallBaseConstructor(ILProcessor ctorBodyEmitter)
            {
                var factoryDefaultConstructor = _moduleDefinition.ImportReference(_factoryType.GetConstructors().Single());
                ctorBodyEmitter.Emit(OpCodes.Ldarg_0);
                ctorBodyEmitter.Emit(OpCodes.Call, factoryDefaultConstructor);
            }

            private static string BuildCaseClassName(MethodDefinition caseClassFactoryMethod)
            {
                return caseClassFactoryMethod.GenericParameters.Any()
                                ? caseClassFactoryMethod.Name + "`" + caseClassFactoryMethod.GenericParameters.Count
                                : caseClassFactoryMethod.Name;
            }

            private static GenericParameter CloneGenericClassParameter(GenericParameter source, IGenericParameterProvider genericParameterProvider)
            {
                var result = new GenericParameter(source.Name, genericParameterProvider)
                {
                    HasReferenceTypeConstraint = source.HasReferenceTypeConstraint,
                    IsContravariant = source.IsContravariant,
                    IsCovariant = source.IsCovariant,
                    IsNonVariant = source.IsNonVariant,
                    IsValueType = source.IsValueType,
                    Attributes = source.Attributes,
                    HasNotNullableValueTypeConstraint = source.HasNotNullableValueTypeConstraint,
                    HasDefaultConstructorConstraint = source.HasDefaultConstructorConstraint
                };
                result.CustomAttributes.AddRange(source.CustomAttributes);
                result.Constraints.AddRange(source.Constraints);
                return result;
            }

            private readonly TypeDefinition _factoryType;
            private readonly ModuleDefinition _moduleDefinition;
            private readonly global::Fody.TypeSystem _typeSystem;
            private readonly Func<string, TypeDefinition> _findType;
        }

        private class CaseClassProperty
        {
            public CaseClassProperty(
                ParameterDefinition factoryMethodParameter, 
                int parameterIndex, 
                GenericInstanceType genericInstanceType,
                Collection<GenericParameter> genericParameters)
            {
                _factoryMethodParameter = factoryMethodParameter;
                _parameterIndex = parameterIndex;

                CtorParameter = MakeConstructorParameter(genericParameters);

                BackingField = new FieldDefinition(
                    $"<{factoryMethodParameter.Name}>k_BackingField",
                    FieldAttributes.Private,
                    CtorParameter.ParameterType);

                _backingFieldReference = genericInstanceType == null
                    ? BackingField
                    : new FieldReference(BackingField.Name, BackingField.FieldType, genericInstanceType);

                var propertyName = factoryMethodParameter.Name.PascalCase();

                PropertyGetter = new MethodDefinition(
                    "get_" + propertyName,
                    MethodAttributes.FamANDAssem | MethodAttributes.Family | MethodAttributes.HideBySig | MethodAttributes.SpecialName,
                    CtorParameter.ParameterType)
                {
                    IsGetter = true,
                    SemanticsAttributes = MethodSemanticsAttributes.Getter
                };

                var getter = PropertyGetter.Body.GetILProcessor();
                getter.Emit(OpCodes.Ldarg_0);
                getter.Emit(OpCodes.Ldfld, _backingFieldReference);
                getter.Emit(OpCodes.Ret);

                Property = new PropertyDefinition(propertyName, PropertyAttributes.None, CtorParameter.ParameterType)
                {
                    HasThis = true,
                    GetMethod = PropertyGetter
                };
            }

            public ParameterDefinition CtorParameter { get; }

            public FieldDefinition BackingField { get; }

            public MethodDefinition PropertyGetter { get; }

            public PropertyDefinition Property { get; }

            private ParameterDefinition MakeConstructorParameter(IEnumerable<GenericParameter> genericParameters)
            {
                if (!_factoryMethodParameter.ParameterType.IsGenericParameter)
                {
                    return _factoryMethodParameter;
                }

                var genericParameterType = genericParameters.Single(p => p.FullName == _factoryMethodParameter.ParameterType.FullName);
                var result = new ParameterDefinition(_factoryMethodParameter.Name, _factoryMethodParameter.Attributes, genericParameterType)
                {
                    IsOptional = _factoryMethodParameter.IsOptional,
                    IsLcid = _factoryMethodParameter.IsLcid,
                    IsIn = _factoryMethodParameter.IsIn,
                    MarshalInfo = _factoryMethodParameter.MarshalInfo,
                    HasDefault = _factoryMethodParameter.HasDefault,
                    HasFieldMarshal = _factoryMethodParameter.HasFieldMarshal,
                    MetadataToken = _factoryMethodParameter.MetadataToken
                };
                if (_factoryMethodParameter.Constant != null)
                {
                    result.Constant = _factoryMethodParameter.Constant;
                }

                result.CustomAttributes.AddRange(_factoryMethodParameter.CustomAttributes);
                return result;
            }

            public void InitializeBackingFieldValue(ILProcessor ctorBodyEmitter, Collection<ParameterDefinition> ctorParameters)
            {
                ctorBodyEmitter.Emit(OpCodes.Ldarg_0);
                ctorBodyEmitter.EmitLoadNthArgument(_parameterIndex, ctorParameters, fromStaticMethod: false);
                ctorBodyEmitter.Emit(OpCodes.Stfld, _backingFieldReference);
            }

            public void EmitEqualityCheck(
                ILProcessor compareCodeEmitter, 
                ParameterDefinition otherInstance, 
                TypeReference genericIEquatable, 
                TypeReference genericIEnumerable)
            {
                if (!_backingFieldReference.FieldType.IsGenericParameter)
                {
                    bool IsGenericIEnumerable(TypeReference typeReference) =>
                        typeReference.Namespace == "System.Collections.Generic" && typeReference.Name == "IEnumerable`1";

                    bool IsIEnumerable(TypeReference typeReference) =>
                        typeReference.Namespace == "System.Collections" && typeReference.Name == "IEnumerable";

                    var fieldType = _backingFieldReference.FieldType.Resolve();
                    var equatableFieldType = genericIEquatable.MakeGenericInstanceType(fieldType).FullName;
                    var fieldImplementsIEquatable = fieldType.Interfaces.Any(i => i.InterfaceType.FullName == equatableFieldType);
                    var fieldIsTypedCollection = IsGenericIEnumerable(fieldType) || 
                                                 fieldType.Interfaces.Any(i => IsGenericIEnumerable(i.InterfaceType));
                    var fieldIsUntypedCollection = !fieldIsTypedCollection && 
                                                (IsIEnumerable(fieldType) || 
                                                 fieldType.Interfaces.Any(i => IsIEnumerable(i.InterfaceType)));

                    switch (_backingFieldReference.FieldType.GetTypeKind())
                    {
                        case TypeKind.ReferenceType:
                            if (fieldImplementsIEquatable)
                            {
                                // if (!DeepEqualityComparer.EquatableReferencesAreEqual(this, other)) return false;
                            }
                            else if (fieldIsTypedCollection)
                            {
                                // if (!DeepEqualityComparer.TypedCollectionsAreEqual(this, other)) return false;
                            }
                            else if (fieldIsUntypedCollection)
                            {
                                // if (!DeepEqualityComparer.UntypedCollectionsAreEqual(this, other)) return false;
                            }
                            else
                            {
                                // if (!DeepEqualityComparer.ReferenceInstancesAreEqual(this, other)) return false;
                            }

                            break;

                        case TypeKind.ValueType:
                            if (fieldImplementsIEquatable)
                            {
                                // if (!((IEquatable<T>)this).Equals(other)) return false;
                            }
                            else
                            {
                                // if (!this.ValueType::Equals(other)) return false;
                            }

                            break;

                        default: // TypeKind.NullableType:
                            if (fieldImplementsIEquatable)
                            {
                                // if (!DeepEqualityComparer.EquatableNullablesAreEqual(this, other)) return false;
                            }
                            else
                            {
                                // if (!DeepEqualityComparer.NullablesAreEqual(this, other)) return false;
                            }

                            break;
                    }
                }
            }

            private readonly ParameterDefinition _factoryMethodParameter;
            private readonly int _parameterIndex;
            private readonly FieldReference _backingFieldReference;
        }
    }
}