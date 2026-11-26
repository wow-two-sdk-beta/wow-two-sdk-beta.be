namespace WoW.Two.Sdk.Beta.Be.Foundation.Paging;

/// <summary>Defines a request for a page addressed by its zero-based index.</summary>
public interface IIndexedPagination
{
    /// <summary>Gets the requested page index.</summary>
    int PageIndex { get; }

    /// <summary>Gets the requested size, when the caller supplies an override.</summary>
    int? RequestedPageSize { get; }
}
