using System.Security.Cryptography;
using MdExplorer.Update.Services;

namespace MdExplorer.Update.Tests.Services;

/// <summary>
/// Die Signatur gilt nur, wenn sie mit dem eingebauten Schlüssel über genau diesen Inhalt
/// entstanden ist. Jeder andere Fall, auch ein fehlender oder kaputter Schlüssel, heißt „nicht
/// geprüft" und nie „Ausnahme".
/// </summary>
/// <remarks>
/// Das Schlüsselpaar entsteht je Testklasse neu; ein privater Schlüssel im Quelltext löste zu
/// Recht den Geheimnis-Scanner aus. Das Ergebnis hängt nur davon ab, ob die Teile zusammenpassen.
/// </remarks>
public sealed class ReleaseSignatureVerifierTests : IDisposable
{
    private static readonly byte[] Content = "Ein Installationspaket."u8.ToArray();

    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    public void Dispose() => _key.Dispose();

    [Fact]
    public void Verify_WithTheSignatureOfThisContent_IsTrue()
    {
        ReleaseSignatureVerifier sut = new(_key.ExportSubjectPublicKeyInfoPem());

        Assert.True(sut.IsConfigured);
        Assert.True(Verify(sut, Content, Sign(_key, Content)));
    }

    [Fact]
    public void Verify_WithChangedContent_IsFalse()
    {
        ReleaseSignatureVerifier sut = new(_key.ExportSubjectPublicKeyInfoPem());
        byte[] changed = [.. Content, 0x00];

        Assert.False(Verify(sut, changed, Sign(_key, Content)));
    }

    [Fact]
    public void Verify_WithTheSignatureOfAnotherKey_IsFalse()
    {
        using ECDsa other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        ReleaseSignatureVerifier sut = new(_key.ExportSubjectPublicKeyInfoPem());

        Assert.False(Verify(sut, Content, Sign(other, Content)));
    }

    [Theory]
    [InlineData(new byte[] { 0x01, 0x02, 0x03 })]
    [InlineData(new byte[0])]
    public void Verify_WithAMalformedOrEmptySignature_IsFalse(byte[] signature)
    {
        ReleaseSignatureVerifier sut = new(_key.ExportSubjectPublicKeyInfoPem());

        Assert.False(Verify(sut, Content, signature));
    }

    [Fact]
    public void Verify_WithAnUnreadableKey_IsFalse()
    {
        ReleaseSignatureVerifier sut = new("-----BEGIN PUBLIC KEY-----\nkein Schlüssel\n-----END PUBLIC KEY-----");

        Assert.False(Verify(sut, Content, Sign(_key, Content)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void Verify_WithoutKey_IsNotConfiguredAndFalse(string? publicKeyPem)
    {
        ReleaseSignatureVerifier sut = new(publicKeyPem);

        Assert.False(sut.IsConfigured);
        Assert.False(Verify(sut, Content, Sign(_key, Content)));
    }

    [Fact]
    public void FromEmbeddedKey_InThisBuild_IsAP256Key()
    {
        // Fehlt der Schlüssel im Build, installiert die Anwendung still keine Updates mehr.
        // Das soll hier auffallen und nicht erst beim ersten Release.
        using Stream resource = typeof(ReleaseSignatureVerifier).Assembly
            .GetManifestResourceStream(ReleaseSignatureVerifier.PublicKeyResourceName)
            ?? throw new InvalidOperationException("Der öffentliche Schlüssel ist nicht eingebettet.");
        using StreamReader reader = new(resource);
        using ECDsa embedded = ECDsa.Create();
        embedded.ImportFromPem(reader.ReadToEnd());

        ReleaseSignatureVerifier sut = ReleaseSignatureVerifier.FromEmbeddedKey();

        Assert.True(sut.IsConfigured);
        Assert.Equal(256, embedded.KeySize);
    }

    private static byte[] Sign(ECDsa key, byte[] content) =>
        key.SignData(content, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);

    private static bool Verify(ReleaseSignatureVerifier sut, byte[] content, byte[] signature)
    {
        using MemoryStream stream = new(content, writable: false);
        return sut.Verify(stream, signature);
    }
}
