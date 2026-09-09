using EShop.BuildingBlocks.Application.Caching;

namespace EShop.BuildingBlocks.Infrastructure.Caching;

public sealed class CacheInvalidationContext : ICacheInvalidationContext
{
    private readonly HashSet<string> _keys = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _families = new(StringComparer.OrdinalIgnoreCase);

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

    public void AddFamily(string family)
    {
        if (string.IsNullOrWhiteSpace(family))
        {
            return;
        }

        _families.Add(family);
    }

    public void AddFamilies(IEnumerable<string> families)
    {
        foreach (var family in families)
        {
            AddFamily(family);
        }
    }

    public IReadOnlyCollection<string> GetFamilies()
    {
        return _families.ToArray();
    }

    public void Clear()
    {
        _keys.Clear();
        _families.Clear();
    }
}
