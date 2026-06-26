using MdExplorer.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MdExplorer.Data.Configuration;

/// <summary>Fluent-Mapping für die Join-Entität <see cref="MarkdownFileTag"/> (Composite-Key).</summary>
public sealed class MarkdownFileTagConfiguration : IEntityTypeConfiguration<MarkdownFileTag>
{
    /// <summary>SQLite-Tabellenname.</summary>
    public const string TableName = "MarkdownFileTags";

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<MarkdownFileTag> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        _ = builder.ToTable(TableName);
        _ = builder.HasKey(link => new { link.MarkdownFileId, link.TagId });

        _ = builder.HasIndex(link => link.TagId)
            .HasDatabaseName("IX_MarkdownFileTags_TagId");

        _ = builder.HasOne(link => link.MarkdownFile)
            .WithMany()
            .HasForeignKey(link => link.MarkdownFileId)
            .OnDelete(DeleteBehavior.Cascade);

        _ = builder.HasOne(link => link.Tag)
            .WithMany()
            .HasForeignKey(link => link.TagId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
