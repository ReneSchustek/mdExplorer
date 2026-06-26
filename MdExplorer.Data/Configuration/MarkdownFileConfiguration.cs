using MdExplorer.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MdExplorer.Data.Configuration;

/// <summary>
/// Fluent-API-Mapping für <see cref="MarkdownFile"/>. Hält EF-Attribute aus dem Indexer-Modul fern.
/// </summary>
public sealed class MarkdownFileConfiguration : IEntityTypeConfiguration<MarkdownFile>
{
    /// <summary>SQLite-Tabellenname.</summary>
    public const string TableName = "MarkdownFiles";

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<MarkdownFile> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        _ = builder.ToTable(TableName);
        _ = builder.HasKey(file => file.Id);

        _ = builder.Property(file => file.AbsolutePath)
            .IsRequired()
            .HasMaxLength(1024);

        _ = builder.Property(file => file.RelativePath)
            .IsRequired()
            .HasMaxLength(1024);

        _ = builder.Property(file => file.FileNameWithoutExtension)
            .IsRequired()
            .HasMaxLength(260);

        _ = builder.Property(file => file.SizeBytes).IsRequired();
        _ = builder.Property(file => file.LastWriteTimeUtc).IsRequired();
        _ = builder.Property(file => file.IndexedAtUtc).IsRequired();

        _ = builder.Property(file => file.ContentHash)
            .IsRequired()
            .HasMaxLength(64);

        _ = builder.HasIndex(file => file.AbsolutePath)
            .IsUnique()
            .HasDatabaseName("IX_MarkdownFiles_AbsolutePath");

        _ = builder.HasIndex(file => file.LastWriteTimeUtc)
            .HasDatabaseName("IX_MarkdownFiles_LastWriteTimeUtc");
    }
}
