using System;
using System.Collections.Generic;
using System.Linq;
using Fody;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using Mono.Collections.Generic;

namespace Fody4Scala.Fody
{
    public class ModuleWeaver : BaseModuleWeaver
    {
        public override void Execute()
        {
            var fieldVarietyDetector = new FieldVarietyDetector(new WellKnownTypes(ModuleDefinition, FindType));
            foreach (var (caseClassesFactory, factoryMethods) in GetAllCaseClassFactoryMethodsGroupedByOwningClass())
            {
                var caseClassBuilder = new CaseClassBuilder(caseClassesFactory, ModuleDefinition, TypeSystem, FindType);
                foreach (var factoryMethod in factoryMethods)
                {
                    var caseClassTypeDefinition = caseClassBuilder.BuildCaseClass(factoryMethod, fieldVarietyDetector);
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
                _deepEqualityComparer = _moduleDefinition.ImportReference(typeof(DeepEqualityComparer)).Resolve();
            }

            public TypeDefinition BuildCaseClass(MethodDefinition factoryMethod, FieldVarietyDetector fieldVarietyDetector)
            {
                var caseClassTypeDefinition = new TypeDefinition(
                    _factoryType.Namespace,
                    BuildCaseClassName(factoryMethod),
                    _factoryType.Attributes & (TypeAttributes.VisibilityMask | TypeAttributes.LayoutMask) | TypeAttributes.Sealed,
                    _factoryType);

                caseClassTypeDefinition.GenericParameters.AddRange(
                    factoryMethod.GenericParameters
                        .Select(genericParameter => genericParameter.CloneWith(caseClassTypeDefinition)));

                var genericInstanceType = factoryMethod.GenericParameters.Any()
                        ? caseClassTypeDefinition.MakeGenericInstanceType(caseClassTypeDefinition.GenericParameters.ToArray())
                        : null;

                var properties = factoryMethod.Parameters.Select((p, i) =>
                    new CaseClassProperty(
                        p, 
                        i, 
                        genericInstanceType, 
                        caseClassTypeDefinition.GenericParameters,
                        _deepEqualityComparer,
                        _moduleDefinition)).ToArray();

                caseClassTypeDefinition.Fields.AddRange(properties.Select(p => p.BackingField));
                caseClassTypeDefinition.Properties.AddRange(properties.Select(p => p.Property));
                caseClassTypeDefinition.Methods.AddRange(properties.Select(p => p.PropertyGetter));
                caseClassTypeDefinition.Methods.Add(GenerateConstructor(properties));

                var typedEqualsMethod = ImplementIEquatable(caseClassTypeDefinition, properties, fieldVarietyDetector);
                caseClassTypeDefinition.Methods.Add(typedEqualsMethod);
                caseClassTypeDefinition.Methods.Add(OverrideObjectEquals(caseClassTypeDefinition, typedEqualsMethod));

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

            private MethodDefinition ImplementIEquatable(
                TypeDefinition caseClassTypeDefinition, 
                CaseClassProperty[] properties,
                FieldVarietyDetector fieldVarietyDetector)
            {
                var concreteCaseClassType = fieldVarietyDetector.GenericIEquatable.MarkAsImplementedBy(caseClassTypeDefinition);

                var method = new MethodDefinition(
                    "Equals", 
                    MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.Virtual, 
                    _typeSystem.BooleanReference);

                var otherInstance = new ParameterDefinition("other", ParameterAttributes.None, concreteCaseClassType);
                method.Parameters.Add(otherInstance);
                var compareCodeEmitter = method.Body.GetILProcessor();
                foreach (var property in properties)
                {
                    property.EmitEqualityCheck(compareCodeEmitter, otherInstance, fieldVarietyDetector);
                }

                compareCodeEmitter.Emit(OpCodes.Ldc_I4_1);
                compareCodeEmitter.Emit(OpCodes.Ret);

                return method;
            }

            private MethodDefinition OverrideObjectEquals(TypeDefinition caseClassTypeDefinition, MethodDefinition typedEqualsMethod)
            {
                var method = new MethodDefinition(
                    "Equals", 
                    MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.Virtual, 
                    _typeSystem.BooleanReference);

                var otherInstance = new ParameterDefinition("obj", ParameterAttributes.None, _typeSystem.ObjectReference);
                method.Parameters.Add(otherInstance);
                var compareCodeEmitter = method.Body.GetILProcessor();

                compareCodeEmitter.Emit(OpCodes.Ldarg_0);
                compareCodeEmitter.Emit(OpCodes.Ldarg_1);
                compareCodeEmitter.Emit(OpCodes.Isinst, caseClassTypeDefinition.AsConcreteTypeReference());
                compareCodeEmitter.Emit(OpCodes.Call, typedEqualsMethod);
                compareCodeEmitter.Emit(OpCodes.Ret);

                return method;
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

            private readonly TypeDefinition _factoryType;
            private readonly ModuleDefinition _moduleDefinition;
            private readonly global::Fody.TypeSystem _typeSystem;
            private readonly Func<string, TypeDefinition> _findType;
            private readonly TypeDefinition _deepEqualityComparer;
        }

        private class CaseClassProperty
        {
            public CaseClassProperty(
                ParameterDefinition factoryMethodParameter, 
                int parameterIndex, 
                GenericInstanceType genericInstanceType,
                Collection<GenericParameter> genericParameters,
                TypeDefinition deepEqualityComparer,
                ModuleDefinition moduleDefinition)
            {
                _factoryMethodParameter = factoryMethodParameter;
                _parameterIndex = parameterIndex;
                _deepEqualityComparer = deepEqualityComparer;
                _moduleDefinition = moduleDefinition;
                CtorParameter = MakeConstructorParameter(genericParameters, moduleDefinition);

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

            public void InitializeBackingFieldValue(ILProcessor ctorBodyEmitter, Collection<ParameterDefinition> ctorParameters)
            {
                ctorBodyEmitter.Emit(OpCodes.Ldarg_0);
                ctorBodyEmitter.EmitLoadNthArgument(_parameterIndex, ctorParameters, fromStaticMethod: false);
                ctorBodyEmitter.Emit(OpCodes.Stfld, _backingFieldReference);
            }

            public void EmitEqualityCheck(
                ILProcessor compareCodeEmitter,
                ParameterDefinition otherInstance,
                FieldVarietyDetector fieldVarietyDetector)
            {
                switch (fieldVarietyDetector.Detect(_backingFieldReference.FieldType))
                {
                    case ReferenceTypeImplementingIEquatable referenceTypeImplementingIEquatable:
                        EmitFieldEqualityTestingCode(
                            compareCodeEmitter, 
                            nameof(DeepEqualityComparer.EquatableReferencesAreEqual), 
                            referenceTypeImplementingIEquatable.FieldType);
                        break;

                    case TypedCollection typedCollection:
                        EmitFieldEqualityTestingCode(
                            compareCodeEmitter,
                            nameof(DeepEqualityComparer.TypedCollectionsAreEqual),
                            typedCollection.ElementType);
                        break;

                    case UntypedColection untypedColection:
                        EmitFieldEqualityTestingCode(
                            compareCodeEmitter,
                            nameof(DeepEqualityComparer.UntypedCollectionsAreEqual));
                        break;

                    case PlainReferenceType plainReferenceType:
                        EmitFieldEqualityTestingCode(
                            compareCodeEmitter,
                            nameof(DeepEqualityComparer.ReferenceInstancesAreEqual),
                            plainReferenceType.FieldType);
                        break;

                    case ValueTypeImplementingIEquatable valueTypeImplementingIEquatable:
                        EmitFieldEqualityTestingCode(
                            compareCodeEmitter,
                            nameof(DeepEqualityComparer.EquatableValuesAreEqual),
                            valueTypeImplementingIEquatable.FieldType);
                        break;

                    case PlainValueType plainValueType:
                        EmitFieldEqualityTestingCode(
                            compareCodeEmitter,
                            nameof(DeepEqualityComparer.ValueInstancesAreEqual),
                            plainValueType.FieldType);
                        break;

                    case NullableTypeImplementingIEquatable nullableTypeImplementingIEquatable:
                        EmitFieldEqualityTestingCode(
                            compareCodeEmitter,
                            nameof(DeepEqualityComparer.EquatableNullablesAreEqual),
                            nullableTypeImplementingIEquatable.UnderlyingType);
                        break;

                    case PlainNullableType plainNullableType:
                        EmitFieldEqualityTestingCode(
                            compareCodeEmitter,
                            nameof(DeepEqualityComparer.NullablesAreEqual),
                            plainNullableType.UnderlyingType);
                        break;

                    case GenericTypeImplementingIEquatable genericTypeImplementingIEquatable:
                        EmitFieldEqualityTestingCode(
                            compareCodeEmitter,
                            nameof(DeepEqualityComparer.EquatableGenericsAreEqual),
                            genericTypeImplementingIEquatable.GenericParameter);
                        break;

                    case PlainGenericType plainGenericType:
                        EmitFieldEqualityTestingCode(
                            compareCodeEmitter,
                            nameof(DeepEqualityComparer.GenericsAreEqual),
                            plainGenericType.GenericParameter);
                        break;

                    case FieldVariety fieldVariety:
                        throw new InvalidOperationException($"Program logic exception: no code to handle {fieldVariety.GetType()} field variety.");
                }
            }

            private void EmitFieldEqualityTestingCode(
                ILProcessor compareCodeEmitter, 
                string deepEqualityComparingMethodName, 
                TypeReference methodGenericArgument)
            {
                EmitFieldEqualityTestingCodeCore(
                    compareCodeEmitter, 
                    deepEqualityComparingMethodName, 
                    methodToCall => 
                    {
                        var genericMethod = new GenericInstanceMethod(methodToCall);
                        var referencedGenericArgument = methodGenericArgument.IsGenericParameter 
                            ? methodGenericArgument 
                            : _moduleDefinition.ImportReference(methodGenericArgument);
                        genericMethod.GenericArguments.Add(referencedGenericArgument);
                        return genericMethod;
                    });
            }

            private void EmitFieldEqualityTestingCode(
                ILProcessor compareCodeEmitter,
                string deepEqualityComparingMethodName)
            {
                EmitFieldEqualityTestingCodeCore(compareCodeEmitter, deepEqualityComparingMethodName, _ => _);
            }

            private void EmitFieldEqualityTestingCodeCore(
                ILProcessor compareCodeEmitter,
                string deepEqualityComparingMethodName,
                Func<MethodReference, MethodReference> adjustMethod)
            {
                var keepAnalyzingProperties = Instruction.Create(OpCodes.Nop);
                compareCodeEmitter.Emit(OpCodes.Ldarg_0);
                compareCodeEmitter.Emit(OpCodes.Ldfld, _backingFieldReference);
                compareCodeEmitter.Emit(OpCodes.Ldarg_1);
                compareCodeEmitter.Emit(OpCodes.Ldfld, _backingFieldReference);
                var methodToCall = _deepEqualityComparer.GetMethods().Single(m => m.Name == deepEqualityComparingMethodName);
                compareCodeEmitter.Emit(OpCodes.Call, adjustMethod(_moduleDefinition.ImportReference(methodToCall)));
                compareCodeEmitter.Emit(OpCodes.Ldc_I4_0);
                compareCodeEmitter.Emit(OpCodes.Ceq);
                compareCodeEmitter.Emit(OpCodes.Brfalse_S, keepAnalyzingProperties);
                compareCodeEmitter.Emit(OpCodes.Ldc_I4_0);
                compareCodeEmitter.Emit(OpCodes.Ret);
                compareCodeEmitter.Append(keepAnalyzingProperties);
            }

            private ParameterDefinition MakeConstructorParameter(
                ICollection<GenericParameter> genericParameters,
                ModuleDefinition moduleDefinition)
            {
                if (_factoryMethodParameter.ParameterType.IsGenericParameter)
                {
                    return _factoryMethodParameter.ChangeType(
                        genericParameters.Single(p => p.FullName == _factoryMethodParameter.ParameterType.FullName));
                }

                if (_factoryMethodParameter.ParameterType is ArrayType arrayType &&
                    (arrayType.ElementType.IsGenericParameter || arrayType.ElementType is GenericInstanceType genericElementType))
                {
                    return _factoryMethodParameter.ChangeType(
                        arrayType.SubstituteGenericParameters(genericParameters, moduleDefinition));
                }

                if (_factoryMethodParameter.ParameterType is GenericInstanceType genericInstanceType &&
                    genericInstanceType.HasGenericArguments &&
                    genericParameters.Any())
                {
                    return _factoryMethodParameter.ChangeType(
                        genericInstanceType.SubstituteGenericParameters(genericParameters, moduleDefinition));
                }

                return _factoryMethodParameter;
            }

            private readonly ParameterDefinition _factoryMethodParameter;
            private readonly int _parameterIndex;
            private readonly TypeDefinition _deepEqualityComparer;
            private readonly ModuleDefinition _moduleDefinition;
            private readonly FieldReference _backingFieldReference;
        }
    }
}