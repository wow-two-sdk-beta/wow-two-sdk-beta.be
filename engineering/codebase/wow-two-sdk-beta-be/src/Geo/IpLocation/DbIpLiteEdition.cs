namespace WoW.Two.Sdk.Beta.Be.Geo.IpLocation;

/// <summary>Refers to the precision of a free DB-IP Lite database.</summary>
public enum DbIpLiteEdition
{
    /// <summary>Country and continent per address range; the smaller download.</summary>
    Country = 0,

    /// <summary>Adds subdivision, city and approximate coordinates; a much larger download.</summary>
    City = 1,
}
