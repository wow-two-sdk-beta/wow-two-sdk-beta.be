namespace WoW.Two.Sdk.Beta.Be.Media.Images;

/// <summary>Refers to how a collage arranges its images.</summary>
public enum CollageLayout
{
    /// <summary>Rows of <see cref="CollageSpec.Columns"/> cells, square-ish by default.</summary>
    Grid,

    /// <summary>One row, side by side — a duo or a strip.</summary>
    Row,

    /// <summary>One column, stacked.</summary>
    Column,
}
