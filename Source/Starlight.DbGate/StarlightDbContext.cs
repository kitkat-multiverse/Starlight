using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Starlight.DbGate.Models;

namespace Starlight.DbGate;

/// <summary>
/// When marked on a property, it is serialized as JSON as applicable.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class JsonColumnAttribute : Attribute;

public sealed class StarlightDbContext(DbContextOptions options) : DbContext(options)
{
    public DbSet<Player> Players { get; set; } = null!;
    public DbSet<PlayerProfile> Profiles { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            var properties = entity.GetProperties()
                .Where(p => p.PropertyInfo?.GetCustomAttribute<JsonColumnAttribute>() is not null);

            foreach (var property in properties)
            {
                modelBuilder.Entity(entity.ClrType)
                    .OwnsOne(property.ClrType, property.Name, nav => nav.ToJson());
            }
        }
    }
}
