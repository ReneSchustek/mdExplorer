using MdExplorer.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MdExplorer.Data.Configuration;

/// <summary>Fluent-Mapping für <see cref="Tag"/> — Slug ist Unique, Name behält Originalschreibweise.</summary>
public sealed class TagConfiguration : IEntityTypeConfiguration<Tag>
{
    /// <summary>SQLite-Tabellenname.</summary>
    public const string TableName = "Tags";

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Tag> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        _ = builder.ToTable(TableName);
        _ = builder.HasKey(tag => tag.Id);

        _ = builder.Property(tag => tag.Name)
            .IsRequired()
            .HasMaxLength(200);

        _ = builder.Property(tag => tag.Slug)
            .IsRequired()
            .HasMaxLength(200);

        _ = builder.HasIndex(tag => tag.Slug)
            .IsUnique()
            .HasDatabaseName("IX_Tags_Slug");
    }
}
