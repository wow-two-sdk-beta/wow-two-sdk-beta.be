namespace WoW.Two.Sdk.Beta.Be.Foundation.Paging.Models;

/// <summary>Represents a zero-based page index and its effective size.</summary>
public sealed record IndexedPaginationModel
{
    /// <summary>Gets the zero-based page index.</summary>
    public required int PageIndex { get; init; }

    /// <summary>Gets the effective number of items per page.</summary>
    public required int PageSize { get; init; }

    /// <summary>Resolves an omitted size while preserving the supplied index and any explicit size.</summary>
    /// <param name="request">The requested page.</param>
    /// <param name="defaultPageSize">The size to use when the request omits it.</param>
    /// <returns>The page with a concrete size.</returns>
    public static IndexedPaginationModel Resolve(IIndexedPagination request, int defaultPageSize)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new IndexedPaginationModel
        {
            PageIndex = request.PageIndex,
            PageSize = request.RequestedPageSize ?? defaultPageSize,
        };
    }
}
