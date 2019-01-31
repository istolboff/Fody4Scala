using System.Collections.Generic;

namespace Fody4Scala.Tests
{
    public static class Make
    {
        public static KeyValuePair<TKey, TValue> Pair<TKey, TValue>(TKey key, TValue value)
        {
            return new KeyValuePair<TKey, TValue>(key, value);
        }
    }
}
