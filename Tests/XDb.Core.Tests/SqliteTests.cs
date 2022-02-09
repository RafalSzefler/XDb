using System;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using XDb.Abstractions;
using Xunit;

namespace XDb.Core.Tests;

public class SqliteTests
{
    private class InnerId
    {
        public InnerId(long value)
        {
            Value = value;
        }

        public long Value { get; }
    }

    private class FooDataWithConversion
    {
        public InnerId Id { get; }
        public string Value { get; }

        public FooDataWithConversion(InnerId id, string value)
        {
            Id = id;
            Value = value;
        }
    }

    private class FooData
    {        
        public long Id { get; }
        public string Value { get; }

        public FooData(long id, string value)
        {
            Id = id;
            Value = value;
        }
    }

    private class LongData
    {
        public long Id { get; }
        public long Value { get; }

        public LongData(long id, long value)
        {
            Id = id;
            Value = value;
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
        await uof.ExecuteAsync("CREATE TABLE foo (id int, value text);");
        await uof.ExecuteAsync(
            "INSERT INTO foo (id, value) VALUES (@Id, @Value);",
            new FooData(1, "foo"));
        await uof.ExecuteAsync(
            "INSERT INTO foo (id, value) VALUES (@Id, @Value);",
            new FooData(3, "baz"));
        
        var results = await uof.QueryAsync<FooData>("SELECT * FROM foo ORDER BY id;");
        Assert.Equal(2, results.Count);
        Assert.Equal(1, results[0].Id);
        Assert.Equal("foo", results[0].Value);
        Assert.Equal(3, results[1].Id);
        Assert.Equal("baz", results[1].Value);
    }

    [Fact]
    public async Task TestReadWriteWithConverter()
    {
        var converter = ValueConverter.Create((InnerId from) => from.Value, (value) => new InnerId(value));

        var conn = new SqliteConnection("Data Source=:memory:");
        var builder = new DbUnitOfWorkFactoryBuilder()
            .AddValueConverters(new[] { converter })
            .SetConnection(conn);
        var factory = await builder.Build();
        
        await using var uof = await factory.CreateAsync();
        await uof.ExecuteAsync("CREATE TABLE foo (id int, value text);");
        await uof.ExecuteAsync(
            "INSERT INTO foo (id, value) VALUES (@Id, @Value);",
            new FooDataWithConversion(new InnerId(1), "foo"));
        await uof.ExecuteAsync(
            "INSERT INTO foo (id, value) VALUES (@Id, @Value);",
            new FooDataWithConversion(new InnerId(3), "baz"));
        
        var results = await uof.QueryAsync<FooDataWithConversion>("SELECT * FROM foo ORDER BY id;");
        Assert.Equal(2, results.Count);
        Assert.Equal(1, results[0].Id.Value);
        Assert.Equal("foo", results[0].Value);
        Assert.Equal(3, results[1].Id.Value);
        Assert.Equal("baz", results[1].Value);
    }

    [Fact]
    public async Task TestSingleReadWrite()
    {
        var conn = new SqliteConnection("Data Source=:memory:");
        var builder = new DbUnitOfWorkFactoryBuilder()
            .SetConnection(conn);
        var factory = await builder.Build();
        
        await using var uof = await factory.CreateAsync();
        await uof.ExecuteAsync("CREATE TABLE foo (id int, value text);");
        await uof.ExecuteAsync(
            "INSERT INTO foo (id, value) VALUES (@Id, @Value);",
            new FooData(1, "foo"));
        await uof.ExecuteAsync(
            "INSERT INTO foo (id, value) VALUES (@Id, @Value);",
            new FooData(3, "baz"));
        
        var results = await uof.SingleAsync<FooData>("SELECT * FROM foo WHERE id = @Id", new { Id = 1 });
        Assert.Equal(1, results.Id);
        Assert.Equal("foo", results.Value);
    }

    [Fact]
    public async Task TestManyItems()
    {
        const int size = 100;
        var conn = new SqliteConnection("Data Source=:memory:");
        var builder = new DbUnitOfWorkFactoryBuilder()
            .SetConnection(conn);
        var factory = await builder.Build();
        
        await using var uof = await factory.CreateAsync();
        await uof.ExecuteAsync("CREATE TABLE foo (id int, value text);");

        for (var i = 0; i < size; i++)
        {
            await uof.ExecuteAsync(
                "INSERT INTO foo (id, value) VALUES (2 * @Id, 'xyz-' || @Value);",
                new FooData(i, $"value-{i}"));
        }

        var results = await uof.QueryAsync<FooData>("SELECT * FROM foo ORDER BY id;");
        Assert.Equal(size, results.Count);
        for (var i = 0; i < size; i++)
        {
            var result = results[i];
            Assert.NotNull(result);
            Assert.Equal(2*i, result.Id);
            Assert.Equal($"xyz-value-{i}", result.Value);
        }
    }

    [Fact]
    public async Task TestTransactions()
    {
        const int size = 8;
        Func<Task<IDbUnitOfWorkFactory>> builder = async () =>
        {
            var conn = new SqliteConnection("Data Source=InMemoryTest;Mode=Memory;Cache=Shared");
            var builder = new DbUnitOfWorkFactoryBuilder()
                .SetConnection(conn);
            return await builder.Build();
        };

        await using var factory = await builder();

        {
            await using var uof = await factory.CreateAsync();
            await uof.ExecuteAsync("CREATE TABLE foo (id int, value int);");
            await uof.ExecuteAsync(
                "INSERT INTO foo (id, value) VALUES (@Id, @Value);",
                new { Id = 1, Value = 0 });
            await uof.CommitAsync();
        }

        var tasks = new Task[size];
        for (var i = 0; i < size; i++)
        {
            tasks[i] = Task.Run(async () => {
                await using var factory = await builder();
                await using var uof = await factory.CreateAsync();
                var result = await uof.SingleAsync<LongData>("SELECT * FROM foo WHERE id = @Id", new { Id = 1 });
                await Task.Delay(100);
                await uof.ExecuteAsync("UPDATE foo SET value = @Value WHERE id = @Id", new { Id = 1, Value = result.Value + 1});
                await uof.CommitAsync();
            });
        }

        await Task.WhenAll(tasks);

        {
            await using var uof = await factory.CreateAsync();
            var result = await uof.SingleAsync<LongData>("SELECT * FROM foo WHERE id = @Id", new { Id = 1 });
            Assert.Equal(1, result.Id);
            Assert.Equal(size, result.Value);
        }
    }
}
