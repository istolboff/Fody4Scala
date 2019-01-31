using System;

namespace Fody4Scala
{
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class CaseClassAttribute : Attribute
    {
    }
}