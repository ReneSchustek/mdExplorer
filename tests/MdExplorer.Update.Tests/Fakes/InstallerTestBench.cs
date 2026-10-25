using System.Net;
using System.Security.Cryptography;
using MdExplorer.Update.Models;
using MdExplorer.Update.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace MdExplorer.Update.Tests.Fakes;

/// <summary>
/// Gemeinsamer Aufbau der Installer-Tests: eigener Ablageordner, eigenes Schlüsselpaar,
/// aufgezeichnete Prozessstarts und eine Testuhr.
/// </summary>
/// <remarks>
/// Das Schlüsselpaar entsteht je Prüfbank neu. Ein fest eingetragener privater Schlüssel stünde
/// sonst im Repository, und jeder Geheimnis-Scanner schlüge zu Recht an. Das Ergebnis der Tests
/// hängt vom Schlüssel nicht ab, nur davon, ob Signatur und Inhalt zusammenpassen.
/// </remarks>
internal sealed class InstallerTestBench : IDisposable
{
    public const string PackageName = "MdExplorer-1.0.0-setup.exe";

    public static readonly Uri PackageAddress =
        new("https://github.com/ReneSchustek/mdExplorer/releases/download/v1.0.0/" + PackageName);

    private readonly List<IDisposable> _disposables = [];
    private readonly ECDsa _signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    public InstallerTestBench()
    {
        DownloadDirectory = Directory.CreateTempSubdirectory("MdExplorer.Tests-").FullName;
        Verifier = new ReleaseSignatureVerifier(_signingKey.ExportSubjectPublicKeyInfoPem());
    }

    public string DownloadDirectory { get; }

    public ReleaseSignatureVerifier Verifier { get; }

    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 10, 25, 9, 0, 0, TimeSpan.Zero));

    public List<string> Launched { get; } = [];

    /// <summary>Wirft der Start, landet diese Ausnahme beim Installer statt eines Prozesses.</summary>
    public Exception? LaunchFailure { get; set; }

    public static byte[] Payload(int length)
    {
        // Deterministisch statt zufällig: Jeder Lauf prüft denselben Inhalt und denselben Hash.
        byte[] content = new byte[length];
        for (int i = 0; i < length; i++)
        {
            content[i] = (byte)(i % 251);
        }

        return content;
    }

    public static string Sha256Of(byte[] content) => Convert.ToHexString(SHA256.HashData(content));

    public static StubHttpMessageHandler WithPayload(byte[] content) =>
        StubHttpMessageHandler.WithStream(content, setContentLength: true);

    public static StubHttpMessageHandler WithContent(Func<HttpContent> content) =>
        StubHttpMessageHandler.Responding(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = content() });

    public byte[] Sign(byte[] content) =>
        _signingKey.SignData(content, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);

    /// <summary>Ein vollständig veröffentlichtes Paket: Prüfwert und Signatur passen zum Inhalt.</summary>
    public UpdateAsset AssetFor(byte[] content) => new(PackageName, PackageAddress, Sha256Of(content), Sign(content));

    public HttpUpdateInstaller CreateSut(StubHttpMessageHandler handler, DownloadLimits? limits = null, ReleaseSignatureVerifier? verifier = null) =>
        CreateSut(handler, DownloadDirectory, limits, verifier);

    public HttpUpdateInstaller CreateSut(StubHttpMessageHandler handler, string downloadDirectory, DownloadLimits? limits = null, ReleaseSignatureVerifier? verifier = null)
    {
        _disposables.Add(handler);
        HttpClient client = new(handler);
        _disposables.Add(client);
        return new HttpUpdateInstaller(
            client,
            downloadDirectory,
            limits ?? DownloadLimits.Default,
            verifier ?? Verifier,
            Time,
            Launch,
            NullLogger<HttpUpdateInstaller>.Instance);
    }

    /// <summary>Alle Dateien unterhalb des Ablageordners, auch in den Unterordnern je Download.</summary>
    public string[] StoredFiles() => Directory.GetFiles(DownloadDirectory, "*", SearchOption.AllDirectories);

    public void Track(IDisposable disposable) => _disposables.Add(disposable);

    public void Dispose()
    {
        foreach (IDisposable single in _disposables)
        {
            single.Dispose();
        }

        _signingKey.Dispose();
        Directory.Delete(DownloadDirectory, recursive: true);
    }

    private void Launch(string path)
    {
        if (LaunchFailure is not null)
        {
            throw LaunchFailure;
        }

        Launched.Add(path);
    }
}
