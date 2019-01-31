using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using Mono.Collections.Generic;

namespace Fody4Scala.Fody
{
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
