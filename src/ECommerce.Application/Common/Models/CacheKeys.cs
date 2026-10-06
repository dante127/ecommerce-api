namespace ECommerce.Application.Common.Models;

/// <summary>Canonical Redis keys for the catalog cache (ADR-006).</summary>
public static class CatalogCacheKeys
{
    public const string VersionKey = "catalog:version";

    public static string Products(long version, string canonicalQueryHash) =>
        $"catalog:v{version}:products:{canonicalQueryHash}";
}
