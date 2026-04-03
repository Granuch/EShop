using EShop.BuildingBlocks.Application.Caching;

namespace EShop.BuildingBlocks.Infrastructure.Caching;

public sealed class CacheInvalidationContext : ICacheInvalidationContext
{
    private readonly HashSet<string> _keys = new(StringComparer.OrdinalIgnoreCase);

    public void AddKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        _keys.Add(key);
    }

    public void AddKeys(IEnumerable<string> keys)
    {
        foreach (var key in keys)
        {
            AddKey(key);
        }
    }

    public IReadOnlyCollection<string> GetKeys()
    {
        return _keys.ToArray();
    }

    public void Clear()
    {
        _keys.Clear();
    }
}
