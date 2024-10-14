namespace Codibre.GrpcSqlProxy.Common
{
    public static class DictionaryExtensions
    {
        public static V GetOrSet<K, V>(this Dictionary<K, V> dictionary, K key, Func<V> create)
        where K : notnull
        {
            if (!dictionary.TryGetValue(key, out var result))
            {
                result = create();
                dictionary[key] = result;
            }

            return result;
        }
    }
}