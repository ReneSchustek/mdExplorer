using MdExplorer.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MdExplorer.Data.Configuration;

/// <summary>
/// Fluent-Mapping für <see cref="MarkdownDocument"/>. Der GZip-HTML-Blob wird über das private
/// Backing-Field <c>_renderedHtmlGz</c> auf die Spalte gemappt, damit die Public-API
/// <see cref="ReadOnlyMemory{T}"/> bleibt und CA1819 vermieden wird.
/// </summary>
public sealed class MarkdownDocumentConfiguration : IEntityTypeConfiguration<MarkdownDocument>
{
    /// <summary>SQLite-Tabellenname.</summary>
    public const string TableName = "MarkdownDocuments";

    /// <summary>Spaltenname für das gerenderte, GZip-komprimierte HTML.</summary>
    public const string RenderedHtmlGzColumn = "RenderedHtmlGz";

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<MarkdownDocument> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        _ = builder.ToTable(TableName);
        _ = builder.HasKey(document => document.Id);

        _ = builder.Property(document => document.MarkdownFileId).IsRequired();

        _ = builder.Property(document => document.SourceContentHash)
            .IsRequired()
            .HasMaxLength(64);

        _ = builder.Property(document => document.FrontmatterJson)
            .IsRequired();

        _ = builder.Property(document => document.OutlinksJson)
            .IsRequired();

        _ = builder.Property(document => document.ParsedAtUtc).IsRequired();

        _ = builder.Property<byte[]>("_renderedHtmlGz")
            .HasField("_renderedHtmlGz")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnName(RenderedHtmlGzColumn)
            .HasColumnType("BLOB")
            .IsRequired();

        _ = builder.Ignore(document => document.RenderedHtmlGz);

        _ = builder.HasIndex(document => document.MarkdownFileId)
            .IsUnique()
            .HasDatabaseName("IX_MarkdownDocuments_MarkdownFileId");

        _ = builder.HasOne(document => document.MarkdownFile)
            .WithOne()
            .HasForeignKey<MarkdownDocument>(document => document.MarkdownFileId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
