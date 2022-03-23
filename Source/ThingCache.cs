namespace RepairableGear
{
    using System.Collections.Generic;

    public static class ThingCache
    {
        private static Dictionary<string, CachedThingProps> Cache = new Dictionary<string, CachedThingProps>();

        public static CachedThingProps Get(string id)
        {
            Cache.TryGetValue(id, out CachedThingProps value);

            return value;
        }

        public static CachedThingProps Add(string id)
        {
            CachedThingProps cachedThingProps = new CachedThingProps(id);
            Cache[id] = cachedThingProps;
            return cachedThingProps;
        }

        public static CachedThingProps GetOrAdd(string id)
        {
            CachedThingProps cachedThingProps = Get(id);

            if (cachedThingProps == null)
            {
                cachedThingProps = Add(id);
            }

            return cachedThingProps;
        }
    }

    public class Settable<T>
    {
        public Settable()
        {
        }

        public Settable(T value)
        {
            this.Value = value;
            this.IsSet = true;
        }

        public bool IsSet { get; }

        public T Value { get; }
    }
}
