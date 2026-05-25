using BestAgent.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BestAgent.Infrastructure.Persistence.Configurations;

internal static class EntityTypeBuilderExtensions
{
    public static void ConfigureAuditColumns<TEntity>(this EntityTypeBuilder<TEntity> builder)
        where TEntity : AuditedEntity
    {
        builder.Property(entity => entity.LastModifier).HasColumnName("last_modifier").HasMaxLength(64).IsRequired();
        builder.Property(entity => entity.LastModifyTime).HasColumnName("last_modify_time").HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(entity => entity.LastModifierName).HasColumnName("last_modifier_name").HasMaxLength(128).IsRequired();
        builder.Property(entity => entity.CreateTime).HasColumnName("create_time").HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(entity => entity.CreatorName).HasColumnName("creator_name").HasMaxLength(128).IsRequired();
        builder.Property(entity => entity.Creator).HasColumnName("creator").HasMaxLength(64).IsRequired();
        builder.Property(entity => entity.Deleted).HasColumnName("deleted").HasDefaultValue(false).IsRequired();
    }

    public static void ConfigureStringEnum<TEntity, TEnum>(
        this PropertyBuilder<TEnum> propertyBuilder,
        string columnName,
        int maxLength)
        where TEntity : class
        where TEnum : struct, Enum
    {
        propertyBuilder.HasColumnName(columnName).HasConversion<string>().HasMaxLength(maxLength).IsRequired();
    }
}
