using ApexPost.Domain.Entities;
using ApexPost.Domain.ValueObjects;
using ApexPost.Infrastructure.Queries;
using ApexPost.Infrastructure.Repositories;

namespace ApexPost.Infrastructure.Tests.Sql;

[Collection(SqlServerCollection.Name)]
public sealed class CatalogueAndRepositoryGuardTests(SqlServerTestDatabase database)
{
    [Fact]
    public async Task Stock_levels_list_one_branchs_products_in_product_order()
    {
        var data = await SqlTestData.SeedAsync(database, stock: 7);
        var secondProduct = new ProductId($"{data.Product.Value}-B");
        var otherBranch = await SqlTestData.SeedAsync(database, stock: 3);
        await using (var setup = database.CreateContext())
        {
            setup.Products.Add(new Product(secondProduct, "Second Envelope", new Money(2m)));
            setup.BranchStock.Add(new BranchInventoryItem(data.Branch, secondProduct, 11));
            await setup.SaveChangesAsync();
        }

        await using var db = database.CreateContext();
        var levels = await new EfCatalogueQueries(db).GetStockLevelsAsync(data.Branch);

        Assert.Equal([(data.Product.Value, 7), (secondProduct.Value, 11)], levels.Select(level => (level.ProductId, level.Quantity)));
        Assert.DoesNotContain(levels, level => level.ProductId == otherBranch.Product.Value);
        Assert.Empty(await new EfCatalogueQueries(db).GetStockLevelsAsync(new BranchCode("BR-WITHOUT-STOCK")));
    }

    [Fact]
    public async Task A_money_order_must_be_loaded_through_the_repository_before_it_can_be_updated()
    {
        await using var db = database.CreateContext();
        var repository = new EfMoneyOrderRepository(db);
        var detached = new MoneyOrder(new("MO-NOT-LOADED"), new("BR-1"), new Money(10m), "Sender", "Recipient", new Money(2.5m), new("TX-1"));

        var refused = Assert.Throws<InvalidOperationException>(() => repository.Update(detached));

        Assert.Equal("Load a money order through this repository before updating it.", refused.Message);
        Assert.Empty(db.ChangeTracker.Entries());
    }
}
