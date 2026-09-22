using JevMcp.App;
using JevMcp.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace JevMcp.Tests.Data;

public sealed class AccessTokenTests
{
    [Fact]
    public async Task IssuedTokenIsNotRecoverableAfterCreation()
    {
        await using var harness = await TokenHarness.CreateAsync();
        var issued = await harness.Tokens.CreateAsync("Agente");

        Assert.StartsWith("jevmcp_", issued.Secret, StringComparison.Ordinal);
        Assert.Equal(issued.Secret[..12], issued.Record.Prefix);
        Assert.NotEqual(issued.Secret, issued.Record.Hash);
        Assert.Equal(AccessTokenFormat.HashHex(issued.Secret), issued.Record.Hash);

        var listed = Assert.Single(await harness.Tokens.ListAsync());
        Assert.Equal("Agente", listed.Name);
        Assert.Equal(issued.Record.Prefix, listed.Prefix);
        Assert.DoesNotContain(issued.Secret, listed.Hash, StringComparison.Ordinal);
        Assert.NotEqual(issued.Secret, listed.Prefix);
    }

    [Fact]
    public async Task RevocationBlocksAuthAndReactivationRestoresIt()
    {
        await using var harness = await TokenHarness.CreateAsync();
        var issued = await harness.Tokens.CreateAsync("Agente");

        Assert.NotNull(await harness.Tokens.AuthenticateAsync(issued.Secret));
        await harness.Tokens.RevokeAsync(issued.Record.Id);
        Assert.Null(await harness.Tokens.AuthenticateAsync(issued.Secret));
        await harness.Tokens.ReactivateAsync(issued.Record.Id);
        Assert.NotNull(await harness.Tokens.AuthenticateAsync(issued.Secret));
    }

    [Fact]
    public async Task DeleteRemovesTheRecordFromTheList()
    {
        await using var harness = await TokenHarness.CreateAsync();
        var issued = await harness.Tokens.CreateAsync("Agente");
        await harness.Tokens.RevokeAsync(issued.Record.Id);

        await harness.Tokens.DeleteAsync(issued.Record.Id);

        Assert.Empty(await harness.Tokens.ListAsync());
    }

    [Fact]
    public void ExposedAddressIsDetectedToRefuseStartup()
    {
        Assert.True(Loopback.IsExposedBinding("http://+:8080"));
        Assert.True(Loopback.IsExposedBinding("http://0.0.0.0:8080"));
        Assert.False(Loopback.IsExposedBinding("http://localhost:5246"));
        Assert.False(Loopback.IsExposedBinding("http://127.0.0.1:5246"));
    }
}

internal sealed class TokenHarness : IAsyncDisposable
{
    private readonly string _directory;

    private TokenHarness(string directory, IAccessTokenService tokens)
    {
        _directory = directory;
        Tokens = tokens;
    }

    public IAccessTokenService Tokens { get; }

    public static async Task<TokenHarness> CreateAsync()
    {
        var directory = Directory.CreateTempSubdirectory("JevMcp-token-").FullName;
        var path = Path.Combine(directory, "audit.db");
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={path}")
            .Options;
        var factory = new TestDbContextFactory(options);
        await using (var db = factory.CreateDbContext())
        {
            await db.Database.MigrateAsync();
        }

        var tokens = new AccessTokenService(factory, new NoopTouch());
        return new TokenHarness(directory, tokens);
    }

    public ValueTask DisposeAsync()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }

        return ValueTask.CompletedTask;
    }
}

internal sealed class NoopTouch : IAccessTokenTouchSink
{
    public void Touch(long tokenId)
    {
    }
}
