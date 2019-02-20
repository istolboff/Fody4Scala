using System.Linq;
using Mono.Cecil;

namespace Fody4Scala.Fody
{
    internal sealed class FieldVarietyDetector
    {
        public FieldVarietyDetector(WellKnownTypes types)
        {
            GenericIEquatable = new GenericIEquatableInterface(types);
            _types = types;
        }

        public GenericIEquatableInterface GenericIEquatable { get; }

        public FieldVariety Detect(TypeReference fieldType)
        {
            return fieldType.IsGenericParameter && fieldType is GenericParameter genericParameter
                ? DetectGeneric(genericParameter)
                : DetectNonGeneric(fieldType);
        }

        private FieldVariety DetectGeneric(GenericParameter genericParameter)
        {
            var only = new Constraints(genericParameter, _types);
            if (only.ReferenceType)
            {
                return only.Equatable 
                    ? (FieldVariety)new ReferenceTypeImplementingIEquatable(genericParameter) 
                    : new PlainReferenceType(genericParameter);
            }

            if (only.ValueType)
            {
                return only.Equatable
                    ? (FieldVariety)new ValueTypeImplementingIEquatable(genericParameter)
                    : new PlainValueType(genericParameter);
            }

            return only.Equatable 
                ? (FieldVariety)new GenericTypeImplementingIEquatable(genericParameter) 
                : new PlainGenericType(genericParameter);
        }

        private FieldVariety DetectNonGeneric(TypeReference fieldType)
        {
            if (fieldType is ArrayType arrayType)
            {
                return new TypedCollection(arrayType.ElementType);
            }

            var resolvedFieldType = fieldType.Resolve();
            var fieldImplementsIEquatable = GenericIEquatable.IsImplementedBy(resolvedFieldType);

            switch (GetTypeKind(resolvedFieldType))
            {
                case TypeKind.ReferenceType:
                    if (fieldImplementsIEquatable)
                    {
                        return new ReferenceTypeImplementingIEquatable(resolvedFieldType);
                    }

                    var fieldIsTypedCollection = IsGenericIEnumerable(resolvedFieldType) ||
                                                 resolvedFieldType.Interfaces.Any(i => IsGenericIEnumerable(i.InterfaceType));
                    if (fieldIsTypedCollection)
                    {

                        TypeReference elementType = IsGenericIEnumerable(resolvedFieldType)
                            ? GetEnumeratedType(fieldType)
                            : GetEnumeratedType(resolvedFieldType.Interfaces.First(i => IsGenericIEnumerable(i.InterfaceType)).InterfaceType);

                        if (elementType.IsGenericParameter && fieldType is GenericInstanceType genericInstanceType)
                        {
                            var parameterIndex = resolvedFieldType.GenericParameters.Select((p, i) => (p.FullName, i)).Single(it => it.FullName == elementType.FullName).i;
                            elementType = genericInstanceType.GenericArguments[parameterIndex];
                        }

                        return new TypedCollection(elementType);
                    }

                    var fieldIsUntypedCollection = !fieldIsTypedCollection &&
                                                   (IsIEnumerable(resolvedFieldType) ||
                                                    resolvedFieldType.Interfaces.Any(i => IsIEnumerable(i.InterfaceType)));
                    if (fieldIsUntypedCollection)
                    {
                        return new UntypedColection();
                    }

                    return new PlainReferenceType(fieldType);

                case TypeKind.ValueType:
                    return fieldImplementsIEquatable
                        ? new ValueTypeImplementingIEquatable(resolvedFieldType)
                        : (FieldVariety)new PlainValueType(fieldType);

                default: // TypeKind.NullableType:
                    var underlyingType = ((GenericInstanceType)fieldType).GenericArguments.Single();
                    return GenericIEquatable.IsImplementedBy(underlyingType)
                        ? new NullableTypeImplementingIEquatable(underlyingType)
                        : (FieldVariety)new PlainNullableType(underlyingType);
            }
        }

        private static TypeKind GetTypeKind(TypeReference typeReference)
        {
            return typeReference.IsValueType
                    ? typeReference.FullName.StartsWith("System.Nullable`1")
                        ? TypeKind.NullableType
                        : TypeKind.ValueType
                    : TypeKind.ReferenceType;
        }

        private static bool IsGenericIEnumerable(TypeReference typeReference) =>
            typeReference.Namespace == "System.Collections.Generic" && typeReference.Name == "IEnumerable`1";

        private static bool IsIEnumerable(TypeReference typeReference) =>
            typeReference.Namespace == "System.Collections" && typeReference.Name == "IEnumerable";

        private static TypeReference GetEnumeratedType(TypeReference typeReference) =>
            ((GenericInstanceType)typeReference).GenericArguments.Single();

        private readonly WellKnownTypes _types;

        private enum TypeKind { ReferenceType, ValueType, NullableType }
    }

    internal abstract class FieldVariety {}

    internal sealed class ReferenceTypeImplementingIEquatable : FieldVariety
    {
        public ReferenceTypeImplementingIEquatable(TypeReference fieldType)
        {
            FieldType = fieldType;
        }

        public TypeReference FieldType { get; }
    }

    internal sealed class TypedCollection : FieldVariety
    {
        public TypedCollection(TypeReference elementType)
        {
            ElementType = elementType;
        }

        public TypeReference ElementType { get; }
    }

    internal sealed class UntypedColection : FieldVariety { }

    internal sealed class PlainReferenceType : FieldVariety
    {
        public PlainReferenceType(TypeReference fieldType)
        {
            FieldType = fieldType;
        }

        public TypeReference FieldType { get; }
    }

    internal sealed class ValueTypeImplementingIEquatable : FieldVariety
    {
        public ValueTypeImplementingIEquatable(TypeReference fieldType)
        {
            FieldType = fieldType;
        }

        public TypeReference FieldType { get; }
    }
    internal sealed class PlainValueType : FieldVariety
    {
        public PlainValueType(TypeReference fieldType)
        {
            FieldType = fieldType;
        }

        public TypeReference FieldType { get; }
    }

    internal sealed class NullableTypeImplementingIEquatable : FieldVariety
    {
        public NullableTypeImplementingIEquatable(TypeReference underlyingType)
        {
            UnderlyingType = underlyingType;
        }

        public TypeReference UnderlyingType { get; }
    }

    internal sealed class PlainNullableType : FieldVariety
    {
        public PlainNullableType(TypeReference underlyingType)
        {
            UnderlyingType = underlyingType;
        }

        public TypeReference UnderlyingType { get; }
    }

    internal sealed class GenericTypeImplementingIEquatable : FieldVariety
    {
        public GenericTypeImplementingIEquatable(GenericParameter genericParameter)
        {
            GenericParameter = genericParameter;
        }

        public GenericParameter GenericParameter { get; }
    }

    internal sealed class PlainGenericType : FieldVariety
    {
        public PlainGenericType(GenericParameter genericParameter)
        {
            GenericParameter = genericParameter;
        }

        public GenericParameter GenericParameter { get; }
    }
}
