using System.Security.Cryptography;
using System.Text;
using CCZen.Engine.Rules;

namespace CCZen.Engine.Tests;

public class SignedPackLoaderTests : IDisposable
{
    private readonly string _root;
    private readonly ECDsa _publisherKey;
    private readonly SignedPackLoader _loader;

    public SignedPackLoaderTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cczen-sign-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _publisherKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        _loader = new SignedPackLoader(_publisherKey.ExportSubjectPublicKeyInfo());
    }

    public void Dispose()
    {
        _publisherKey.Dispose();
        Directory.Delete(_root, recursive: true);
    }

    private const string MinimalAdapterPackJson = """
        {
          "schemaVersion": 1,
          "adapters": [
            {
              "id": "x", "name": "X", "category": "other",
              "detect": { "pathPatterns": ["${TEMP}"] },
              "items": [{ "id": "i", "tier": "T1", "targets": ["${TEMP}\\x"], "explain": "e" }]
            }
          ]
        }
        """;

    private string WriteSignedPack(string json, bool corruptAfterSigning = false)
    {
        string packPath = Path.Combine(_root, "pack.json");
        byte[] payload = Encoding.UTF8.GetBytes(json);
        byte[] signature = _publisherKey.SignData(payload, HashAlgorithmName.SHA256);
        File.WriteAllBytes(packPath, corruptAfterSigning
            ? Encoding.UTF8.GetBytes(json.Replace("T1", "T0"))
            : payload);
        File.WriteAllText(packPath + SignedPackLoader.SignatureSuffix, Convert.ToBase64String(signature));
        return packPath;
    }

    [Fact]
    public void ValidSignature_LoadsExternalPack()
    {
        string packPath = WriteSignedPack(MinimalAdapterPackJson);

        AdapterPack pack = _loader.LoadAdapterPackOrBaseline(packPath);

        Assert.Equal("x", Assert.Single(pack.Adapters).Id);
    }

    [Fact]
    public void TamperedPayload_FallsBackToBaseline()
    {
        string packPath = WriteSignedPack(MinimalAdapterPackJson, corruptAfterSigning: true);

        AdapterPack pack = _loader.LoadAdapterPackOrBaseline(packPath);

        Assert.DoesNotContain(pack.Adapters, a => a.Id == "x");
        Assert.True(pack.Adapters.Count >= 5);
    }

    [Fact]
    public void MissingSignatureFile_FallsBackToBaseline()
    {
        string packPath = Path.Combine(_root, "pack.json");
        File.WriteAllText(packPath, MinimalAdapterPackJson);

        Assert.Null(_loader.LoadVerified(packPath));
        Assert.DoesNotContain(_loader.LoadAdapterPackOrBaseline(packPath).Adapters, a => a.Id == "x");
    }

    [Fact]
    public void WrongPublisherKey_FallsBackToBaseline()
    {
        string packPath = Path.Combine(_root, "pack.json");
        byte[] payload = Encoding.UTF8.GetBytes(MinimalAdapterPackJson);
        using var attackerKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        File.WriteAllBytes(packPath, payload);
        File.WriteAllText(
            packPath + SignedPackLoader.SignatureSuffix,
            Convert.ToBase64String(attackerKey.SignData(payload, HashAlgorithmName.SHA256)));

        Assert.Null(_loader.LoadVerified(packPath));
    }

    [Fact]
    public void SignedButSchemaInvalid_FallsBackToBaseline()
    {
        string packPath = WriteSignedPack("""{"schemaVersion":1,"adapters":[{"bogus":true}]}""");

        AdapterPack pack = _loader.LoadAdapterPackOrBaseline(packPath);

        Assert.True(pack.Adapters.Count >= 5);
    }

    [Fact]
    public void GarbageSignatureText_FallsBackToBaseline()
    {
        string packPath = Path.Combine(_root, "pack.json");
        File.WriteAllText(packPath, MinimalAdapterPackJson);
        File.WriteAllText(packPath + SignedPackLoader.SignatureSuffix, "not-base64!!");

        Assert.Null(_loader.LoadVerified(packPath));
    }
}
