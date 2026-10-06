using ECommerce.Application.Common.Models;
using FluentAssertions;
using Xunit;

namespace ECommerce.Application.UnitTests;

public class PagingTests
{
    [Theory]
    [InlineData(1, 20, 1, 20)]      // defaults pass through
    [InlineData(0, 20, 1, 20)]      // page below the floor
    [InlineData(-5, 0, 1, 20)]      // both out of range
    [InlineData(3, 1, 3, 1)]        // valid minimum page size
    [InlineData(2, 100, 2, 100)]    // valid maximum page size
    [InlineData(7, 101, 7, 20)]     // page size above the ceiling falls back to the default
    public void Normalize_ClampsClientSuppliedPaging(int page, int pageSize, int expectedPage, int expectedPageSize)
    {
        var (normalizedPage, normalizedPageSize) = Paging.Normalize(page, pageSize);

        normalizedPage.Should().Be(expectedPage);
        normalizedPageSize.Should().Be(expectedPageSize);
    }
}
