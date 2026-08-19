using MdExplorer.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MdExplorer.Data.Configuration;

/// <summary>
/// Fluent-Mapping für <see cref="ParseFailure"/>. Der Unique-Index auf
/// <see cref="ParseFailure.MarkdownFileId"/> erzwingt höchstens einen Vermerk je Datei,
/// der Cascade-Delete räumt ihn mit der Datei weg.
/// </summary>
public sealed class ParseFailureConfiguration : IEntityTypeConfiguration<ParseFailure>
{
    /// <summary>SQLite-Tabellenname.</summary>
    public const string TableName = "ParseFailures";

    /// <summary>Maximale Länge des SHA-256-Quell-Hashes als Hex-String.</summary>
    private const int ContentHashMaxLength = 64;

    /// <summary>Maximale Länge der Parser-Fassungskennung.</summary>
    private const int EngineVersionMaxLength = 128;

    /// <summary>Maximale Länge des gespeicherten Fehlschlag-Grundes.</summary>
    private const int FailureReasonMaxLength = 512;

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ParseFailure> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        _ = builder.ToTable(TableName);
        _ = builder.HasKey(failure => failure.Id);

        _ = builder.Property(failure => failure.MarkdownFileId).IsRequired();

        _ = builder.Property(failure => failure.ContentHash)
            .IsRequired()
            .HasMaxLength(ContentHashMaxLength);

        _ = builder.Property(failure => failure.EngineVersion)
            .IsRequired()
            .HasMaxLength(EngineVersionMaxLength);

        _ = builder.Property(failure => failure.FailureReason)
            .IsRequired()
            .HasMaxLength(FailureReasonMaxLength);

        _ = builder.Property(failure => failure.FailedAtUtc).IsRequired();

        _ = builder.HasIndex(failure => failure.MarkdownFileId)
            .IsUnique()
            .HasDatabaseName("IX_ParseFailures_MarkdownFileId");

        _ = builder.HasOne(failure => failure.MarkdownFile)
            .WithOne()
            .HasForeignKey<ParseFailure>(failure => failure.MarkdownFileId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
