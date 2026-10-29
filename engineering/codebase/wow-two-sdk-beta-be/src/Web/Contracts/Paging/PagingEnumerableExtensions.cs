namespace WoW.Two.Sdk.Beta.Be.Web.Contracts.Paging;

/// <summary>Pages lists already in memory, such as a cached catalog, in the same shape database reads use.</summary>
public static class PagingEnumerableExtensions
{
    /// <summary>The offset page of <paramref name="source"/> the request names.</summary>
    /// <typeparam name="T">The row type.</typeparam>
    /// <param name="source">The rows, in the order pages follow.</param>
    /// <param name="request">The page and size asked for.</param>
    /// <param name="maxPageSize">The size cap.</param>
    public static PageDto<T> ToPage<T>(this IEnumerable<T> source, PageApiRequest request, int maxPageSize = PagingConstants.MaxPageSize)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(request);
        var rows = source as IReadOnlyList<T> ?? [.. source];
        var size = PageSizeMapper.Clamp(request.PageSize, maxPageSize);
        var number = Math.Max(request.PageNumber ?? 1, 1);
        return new PageDto<T> { Items = [.. rows.Skip((number - 1) * size).Take(size)], PageNumber = number, PageSize = size, TotalCount = rows.Count };
    }

    /// <summary>
    /// The keyset page of <paramref name="source"/> after the request's token, ordered by one key; the key should be
    /// unique. Pages stay steady while rows are added, as a database keyset read does.
    /// </summary>
    /// <typeparam name="T">The row type.</typeparam>
    /// <typeparam name="TKey">The key type; strings compare ordinally.</typeparam>
    /// <param name="source">The rows, in any order.</param>
    /// <param name="key">The key pages follow.</param>
    /// <param name="request">The token and size.</param>
    /// <param name="descending">Whether pages run from the highest key down.</param>
    /// <param name="maxPageSize">The size cap.</param>
    /// <exception cref="WoW.Two.Sdk.Beta.Be.Foundation.Errors.AppException">The token is not one a page answered with (<c>PageTokenInvalid</c>).</exception>
    public static TokenPageDto<T> ToTokenPage<T, TKey>(this IEnumerable<T> source, Func<T, TKey> key, TokenPageApiRequest request, bool descending = false, int maxPageSize = PagingConstants.MaxPageSize)
    {
        ArgumentNullException.ThrowIfNull(key);
        return Keyset(source, [Key.Of(key)], request, descending, maxPageSize);
    }

    /// <summary>
    /// The keyset page of <paramref name="source"/> after the request's token, ordered by <paramref name="first"/> then
    /// <paramref name="then"/> — such as submission time then id, so rows sharing a time are neither skipped nor repeated.
    /// </summary>
    /// <typeparam name="T">The row type.</typeparam>
    /// <typeparam name="TFirst">The first key type; strings compare ordinally.</typeparam>
    /// <typeparam name="TThen">The tie-breaking key type; make it unique.</typeparam>
    /// <param name="source">The rows, in any order.</param>
    /// <param name="first">The first key.</param>
    /// <param name="then">The tie-breaking key.</param>
    /// <param name="request">The token and size.</param>
    /// <param name="descending">Whether pages run from the highest keys down, as a newest-first feed does.</param>
    /// <param name="maxPageSize">The size cap.</param>
    /// <exception cref="WoW.Two.Sdk.Beta.Be.Foundation.Errors.AppException">The token is not one a page answered with (<c>PageTokenInvalid</c>).</exception>
    public static TokenPageDto<T> ToTokenPage<T, TFirst, TThen>(this IEnumerable<T> source, Func<T, TFirst> first, Func<T, TThen> then, TokenPageApiRequest request, bool descending = false, int maxPageSize = PagingConstants.MaxPageSize)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(then);
        return Keyset(source, [Key.Of(first), Key.Of(then)], request, descending, maxPageSize);
    }

    private static TokenPageDto<T> Keyset<T>(IEnumerable<T> source, IReadOnlyList<Key<T>> keys, TokenPageApiRequest request, bool descending, int maxPageSize)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(request);
        var size = PageSizeMapper.Clamp(request.PageSize, maxPageSize);
        var sign = descending ? -1 : 1;
        var order = Comparer<T>.Create((left, right) =>
        {
            foreach (var key in keys)
            {
                var compared = key.Compare(key.Read(left), key.Read(right));
                if (compared != 0)
                    return sign * compared;
            }

            return 0;
        });

        IEnumerable<T> rows = source;
        if (!string.IsNullOrWhiteSpace(request.PageToken))
        {
            var position = PageTokenMapper.Decode(request.PageToken, [.. keys.Select(key => key.Type)]);
            rows = rows.Where(row => After(row, keys, position, sign));
        }

        var page = rows.Order(order).Take(size + 1).ToList();
        if (page.Count <= size)
            return new TokenPageDto<T> { Items = page, PageSize = size };

        page.RemoveAt(size);
        var last = page[^1];
        return new TokenPageDto<T> { Items = page, PageSize = size, NextPageToken = PageTokenMapper.Encode([.. keys.Select(key => key.Read(last))]) };
    }

    /// <summary>Whether a row comes after the position in page order: beyond it on the first key that differs.</summary>
    private static bool After<T>(T row, IReadOnlyList<Key<T>> keys, object?[] position, int sign)
    {
        for (var index = 0; index < keys.Count; index++)
        {
            var compared = sign * keys[index].Compare(keys[index].Read(row), position[index]);
            if (compared != 0)
                return compared > 0;
        }

        return false;
    }

    /// <summary>A key of a row: how to read it, its type and how two values compare (strings ordinally).</summary>
    private sealed record Key<T>(Func<T, object?> Read, Type Type, Comparison<object?> Compare);

    private static class Key
    {
        public static Key<T> Of<T, TKey>(Func<T, TKey> read)
        {
            IComparer<TKey> comparer = typeof(TKey) == typeof(string) ? (IComparer<TKey>)StringComparer.Ordinal : Comparer<TKey>.Default;
            return new Key<T>(row => read(row), typeof(TKey), (left, right) => comparer.Compare((TKey)left!, (TKey)right!));
        }
    }
}
