using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Xunit;

namespace XDb.Core.Tests;

public class SqliteEnumTests
{
    private enum EData : long
    {
        Default = 0,
        Foo = 1,
        Baz = 2,
    }

    private class EnumData
    {
        public int Id { get; }
        public EData Data { get; }

        public EnumData(int id, EData data)
        {
            Id = id;
            Data = data;
        }
    }

    [Fact]
    public async Task TestReadWrite()
    {
        var conn = new SqliteConnection("Data Source=:memory:");
        var builder = new DbUnitOfWorkFactoryBuilder()
            .SetConnection(conn);
        var factory = await builder.Build();
        
        await using var uof = await factory.CreateAsync();
        await uof.ExecuteAsync("CREATE TABLE xyz (id int, data int);");
        await uof.ExecuteAsync(
            "INSERT INTO xyz (id, data) VALUES (@Id, @Data);",
            new EnumData(1, EData.Default));
        await uof.ExecuteAsync(
            "INSERT INTO xyz (id, data) VALUES (@Id, @Data);",
            new EnumData(5, EData.Baz));
        
        var results = await uof.QueryAsync<EnumData>("SELECT * FROM xyz ORDER BY id;");
        Assert.Equal(2, results.Count);
        Assert.Equal(1, results[0].Id);
        Assert.Equal(EData.Default, results[0].Data);
        Assert.Equal(5, results[1].Id);
        Assert.Equal(EData.Baz, results[1].Data);
    }
}
