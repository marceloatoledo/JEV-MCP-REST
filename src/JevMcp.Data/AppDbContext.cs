using Microsoft.EntityFrameworkCore;

namespace JevMcp.Data;

/// <summary>Application database: audit, access tokens, and operational options.</summary>
public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<CallLog> CallLogs => Set<CallLog>();

    public DbSet<McpAccessToken> AccessTokens => Set<McpAccessToken>();

    public DbSet<AppSetting> Settings => Set<AppSetting>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        var callLog = modelBuilder.Entity<CallLog>();
        callLog.ToTable("CallLogs");
        callLog.HasKey(log => log.Id);
        callLog.Property(log => log.Tool).HasMaxLength(64).IsRequired();
        callLog.Property(log => log.Provider).HasMaxLength(32);
        callLog.Property(log => log.Model).HasMaxLength(128);
        callLog.Property(log => log.ResultingAction).HasMaxLength(32);
        callLog.Property(log => log.Status).HasMaxLength(16).IsRequired();
        callLog.Property(log => log.CostUsd).HasPrecision(18, 10);
        callLog.Property(log => log.TokenName).HasMaxLength(64);
        callLog.Property(log => log.TokenPrefix).HasMaxLength(16);
        callLog.HasIndex(log => log.Instant);
        callLog.HasIndex(log => log.Tool);
        callLog.HasIndex(log => log.AccessTokenId);

        var token = modelBuilder.Entity<McpAccessToken>();
        token.ToTable("AccessTokens");
        token.HasKey(item => item.Id);
        token.Property(item => item.Name).HasMaxLength(64).IsRequired();
        token.Property(item => item.Hash).HasMaxLength(64).IsRequired();
        token.Property(item => item.Prefix).HasMaxLength(16).IsRequired();
        token.HasIndex(item => item.Hash).IsUnique();
        token.HasIndex(item => item.Active);

        var setting = modelBuilder.Entity<AppSetting>();
        setting.ToTable("Settings");
        setting.HasKey(item => item.Key);
        setting.Property(item => item.Key).HasMaxLength(64);
        setting.Property(item => item.Value).IsRequired();
    }
}
