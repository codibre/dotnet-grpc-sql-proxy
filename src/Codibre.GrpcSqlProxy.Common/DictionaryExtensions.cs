namespace Codibre.GrpcSqlProxy.Common;

public static class DictionaryExtensions
{
    public static V GetOrSet<K, V>(this Dictionary<K, V> dictionary, K key, Func<V> create)
    where K : notnull
    {
        if (!dictionary.TryGetValue(key, out var result))
        {
            lock (dictionary)
            {
                if (!dictionary.TryGetValue(key, out result))
                {
                    result = create();
                    dictionary[key] = result;
                }
            }
        }

        return result;
    }
}