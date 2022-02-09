using System;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Xunit;

namespace XDb.Core.Tests;

public class SqliteBlobTests
{
    private class BlobData
    {
        public int Id { get; }
        public byte[] Data { get; }
        public DateTime CreatedAt { get; }

        public BlobData(int id, byte[] data, DateTime createdAt)
        {
            Id = id;
            Data = data;
            CreatedAt = createdAt;
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
        await uof.ExecuteAsync("CREATE TABLE xyz (id int, data blob, created_at int);");
        await uof.ExecuteAsync(
            "INSERT INTO xyz (id, data, created_at) VALUES (@Id, @Data, @CreatedAt);",
            new BlobData(1, Encoding.UTF8.GetBytes("foo"), new DateTime(2000, 1, 1)));
        await uof.ExecuteAsync(
            "INSERT INTO xyz (id, data, created_at) VALUES (@Id, @Data, @CreatedAt);",
            new BlobData(2, Encoding.UTF8.GetBytes("baz"), new DateTime(2000, 1, 3)));
        
        var results = await uof.QueryAsync<BlobData>("SELECT * FROM xyz ORDER BY id;");
        Assert.Equal(2, results.Count);
        Assert.Equal(1, results[0].Id);
        Assert.Equal("foo", Encoding.UTF8.GetString(results[0].Data));
        Assert.Equal(new DateTime(2000, 1, 1), results[0].CreatedAt);
        Assert.Equal(2, results[1].Id);
        Assert.Equal("baz", Encoding.UTF8.GetString(results[1].Data));
        Assert.Equal(new DateTime(2000, 1, 3), results[1].CreatedAt);
    }
}
