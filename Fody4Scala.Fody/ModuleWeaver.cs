using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using Fody;

namespace Fody4Scala.Fody
{
    public class ModuleWeaver : BaseModuleWeaver
    {
        public override void Execute()
        {
            foreach (var (caseClassesFactory, factoryMethods) in GetAllCaseClassFactoryMethodsGroupedByOwningClass())
            {
                foreach (var caseClassFactoryMethod in factoryMethods)
                {
                    var caseClassTypeDefinition = GenerateCaseClass(caseClassesFactory, caseClassFactoryMethod);
                    ModuleDefinition.Types.Add(caseClassTypeDefinition);
                    AdjustFactoryMethod(caseClassFactoryMethod, caseClassTypeDefinition);
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

        private TypeDefinition GenerateCaseClass(TypeDefinition caseClassesFactory, MethodDefinition caseClassFactoryMethod)
        {
            var className = caseClassFactoryMethod.GenericParameters.Any()
                ? caseClassFactoryMethod.Name + "`" + caseClassFactoryMethod.GenericParameters.Count
                : caseClassFactoryMethod.Name;

            var caseClassTypeDefinition = new TypeDefinition(
                caseClassesFactory.Namespace,
                className,
                caseClassesFactory.Attributes & (TypeAttributes.VisibilityMask | TypeAttributes.LayoutMask) | TypeAttributes.Sealed,
                caseClassesFactory);

            caseClassTypeDefinition.GenericParameters.AddRange(caseClassFactoryMethod.GenericParameters
                .Select(genericParameter => CloneGenericClassParameter(genericParameter, caseClassTypeDefinition)));

            var ctor = GenerateConstructor(caseClassFactoryMethod, caseClassesFactory, caseClassTypeDefinition.GenericParameters);
            caseClassTypeDefinition.Methods.Add(ctor);

            var ctorBodyEmitter = ctor.Body.GetILProcessor();

            // call base constructor
            ctorBodyEmitter.Emit(OpCodes.Ldarg_0);
            ctorBodyEmitter.Emit(
                OpCodes.Call,
                ModuleDefinition.ImportReference(caseClassesFactory.GetConstructors().Single()));

            var i = 0;

            var genericInstanceType = caseClassFactoryMethod.GenericParameters.Any() 
                    ? caseClassTypeDefinition.MakeGenericInstanceType(caseClassTypeDefinition.GenericParameters.ToArray())
                    : null;
            foreach (var (backingFieldDefinition, backingFieldReference, propertyGetMethod, propertyDefinition) in 
                ctor.Parameters.Select(constructorParameter => GenerateProperty(constructorParameter, genericInstanceType)))
            {
                caseClassTypeDefinition.Fields.Add(backingFieldDefinition);
                caseClassTypeDefinition.Methods.Add(propertyGetMethod);
                caseClassTypeDefinition.Properties.Add(propertyDefinition);

                // set field from parameter
                ctorBodyEmitter.Emit(OpCodes.Ldarg_0);
                ctorBodyEmitter.EmitLoadNthArgument(i, ctor.Parameters, fromStaticMethod: false);
                ctorBodyEmitter.Emit(OpCodes.Stfld, backingFieldReference);

                ++i;
            }

            ctorBodyEmitter.Emit(OpCodes.Ret);

            return caseClassTypeDefinition;
        }

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

        private MethodDefinition GenerateConstructor(
            MethodDefinition caseClassFactoryMethod, 
            TypeDefinition caseClassesFactory,
            Mono.Collections.Generic.Collection<GenericParameter> genericParameters)
        {
            var ctor = new MethodDefinition(
                ".ctor", 
                MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName, 
                TypeSystem.VoidReference);

            ctor.Parameters.AddRange(caseClassFactoryMethod.Parameters.Select(parameter => CloneMethodParameterDefinition(parameter, genericParameters)));
            return ctor;
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

        private ParameterDefinition CloneMethodParameterDefinition(
            ParameterDefinition parameter,
            Mono.Collections.Generic.Collection<GenericParameter> genericParameters)
        {
            if (!parameter.ParameterType.IsGenericParameter)
            {
                return parameter;
            }

            var genericParameterType = genericParameters.Single(p => p.FullName == parameter.ParameterType.FullName);
            var result = new ParameterDefinition(parameter.Name, parameter.Attributes, genericParameterType)
                        {
                            IsOptional = parameter.IsOptional,
                            IsLcid = parameter.IsLcid,
                            IsIn = parameter.IsIn,
                            MarshalInfo = parameter.MarshalInfo,
                            HasDefault = parameter.HasDefault,
                            HasFieldMarshal = parameter.HasFieldMarshal,
                            MetadataToken = parameter.MetadataToken
                        };
            if (parameter.Constant != null)
            {
                result.Constant = parameter.Constant;
            }

            result.CustomAttributes.AddRange(parameter.CustomAttributes);
            return result;
        }

        private (FieldDefinition BackingFieldDefinition, FieldReference BackingFieldReference, MethodDefinition PropertyGetMethod, PropertyDefinition PropertyDefinition) 
            GenerateProperty(ParameterDefinition constructorParameter, GenericInstanceType genericInstanceType)
        {
            var backingFieldDefinition = new FieldDefinition(
                $"<{constructorParameter.Name}>k_BackingField", 
                FieldAttributes.Private, 
                constructorParameter.ParameterType);

            var backingFieldReference = genericInstanceType == null
                ? backingFieldDefinition
                : new FieldReference(backingFieldDefinition.Name, backingFieldDefinition.FieldType, genericInstanceType);

            var propertyName = constructorParameter.Name.PascalCase();

            var propertyGetMethod = new MethodDefinition(
                "get_" + propertyName, 
                MethodAttributes.FamANDAssem | MethodAttributes.Family | MethodAttributes.HideBySig | MethodAttributes.SpecialName, 
                constructorParameter.ParameterType)
            {
                IsGetter = true,
                SemanticsAttributes = MethodSemanticsAttributes.Getter
            };

            var getter = propertyGetMethod.Body.GetILProcessor();
            getter.Emit(OpCodes.Ldarg_0);
            getter.Emit(OpCodes.Ldfld, backingFieldReference);
            getter.Emit(OpCodes.Ret);

            var propertyDefinition = new PropertyDefinition(
                propertyName, 
                PropertyAttributes.None, 
                constructorParameter.ParameterType)
            {
                HasThis = true,
                GetMethod = propertyGetMethod
            };

            return (backingFieldDefinition, backingFieldReference, propertyGetMethod, propertyDefinition);
        }
    }
}