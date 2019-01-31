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
                var caseClassBuilder = new CaseClassBuilder(caseClassesFactory, ModuleDefinition, TypeSystem.VoidReference);
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

        private class CaseClassBuilder
        {
            public CaseClassBuilder(
                TypeDefinition caseClassesFactory, 
                ModuleDefinition moduleDefinition,
                TypeReference voidReference)
            {
                _factoryType = caseClassesFactory;
                _voidReference = voidReference;
                _factoryDefaultConstructor = moduleDefinition.ImportReference(caseClassesFactory.GetConstructors().Single());
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

                return caseClassTypeDefinition;
            }

            private MethodDefinition GenerateConstructor(CaseClassProperty[] properties)
            {
                var ctor = new MethodDefinition(
                    ".ctor",
                    MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
                    _voidReference);

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

            private void CallBaseConstructor(ILProcessor ctorBodyEmitter)
            {
                ctorBodyEmitter.Emit(OpCodes.Ldarg_0);
                ctorBodyEmitter.Emit(OpCodes.Call, _factoryDefaultConstructor);
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
            private readonly TypeReference _voidReference;
            private readonly MethodReference _factoryDefaultConstructor;
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

            private readonly ParameterDefinition _factoryMethodParameter;
            private readonly int _parameterIndex;
            private readonly FieldReference _backingFieldReference;
        }
    }
}