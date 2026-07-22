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

    /// <summary>Maximale Länge für absolute und relative Dateipfade.</summary>
    private const int PathMaxLength = 1024;

    /// <summary>Maximale Länge des Dateinamens ohne Erweiterung (Windows-MAX_PATH-Komponente).</summary>
    private const int FileNameMaxLength = 260;

    /// <summary>Maximale Länge des SHA-256-Inhalts-Hashes als Hex-String.</summary>
    private const int ContentHashMaxLength = 64;

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<MarkdownFile> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        _ = builder.ToTable(TableName);
        _ = builder.HasKey(file => file.Id);

        // NOCASE-Collation: Windows-Dateipfade sind case-insensitiv. Damit vergleicht der
        // Unique-Index und GetByAbsolutePathAsync (== auf der Spalte) gross-/kleinschreibungs-
        // unabhaengig — verhindert Duplikat-Inserts bei abweichender Casing und haelt den
        // Abgleich konsistent zum case-insensitiven Tombstone-Vergleich im Indexer.
        _ = builder.Property(file => file.AbsolutePath)
            .IsRequired()
            .HasMaxLength(PathMaxLength)
            .UseCollation("NOCASE");

        _ = builder.Property(file => file.RelativePath)
            .IsRequired()
            .HasMaxLength(PathMaxLength);

        _ = builder.Property(file => file.FileNameWithoutExtension)
            .IsRequired()
            .HasMaxLength(FileNameMaxLength);

        _ = builder.Property(file => file.SizeBytes).IsRequired();
        _ = builder.Property(file => file.LastWriteTimeUtc).IsRequired();
        _ = builder.Property(file => file.IndexedAtUtc).IsRequired();

        _ = builder.Property(file => file.ContentHash)
            .IsRequired()
            .HasMaxLength(ContentHashMaxLength);

        _ = builder.HasIndex(file => file.AbsolutePath)
            .IsUnique()
            .HasDatabaseName("IX_MarkdownFiles_AbsolutePath");

        _ = builder.HasIndex(file => file.LastWriteTimeUtc)
            .HasDatabaseName("IX_MarkdownFiles_LastWriteTimeUtc");
    }
}
