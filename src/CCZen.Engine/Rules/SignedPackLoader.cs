using System.Security.Cryptography;

namespace CCZen.Engine.Rules;

/// <summary>
/// Loads external rule/adapter pack updates only after ECDSA P-256 signature
/// verification (spec: 04 SAFE-FR-030). The publisher public key is pinned by
/// the host application; when a pack is missing, unsigned, or fails
/// verification, the caller falls back to the built-in baseline packs.
/// </summary>
public sealed class SignedPackLoader
{
    /// <summary>Detached signature file convention: "&lt;pack&gt;.json" + "&lt;pack&gt;.json.sig" (Base64, SHA-256).</summary>
    public const string SignatureSuffix = ".sig";

    private readonly byte[] _publicKeySpki;

    /// <param name="publicKeySpki">Pinned publisher public key, X.509 SubjectPublicKeyInfo (P-256).</param>
    public SignedPackLoader(byte[] publicKeySpki)
    {
        _publicKeySpki = publicKeySpki;
    }

    /// <summary>
    /// Reads and verifies a pack file with its detached signature; returns the
    /// verified JSON text, or null when the file/signature is absent or invalid.
    /// Never throws for tampered or malformed input (SAFE-FR-030 fallback).
    /// </summary>
    public string? LoadVerified(string packPath)
    {
        try
        {
            string signaturePath = packPath + SignatureSuffix;
            if (!File.Exists(packPath) || !File.Exists(signaturePath))
            {
                return null;
            }

            byte[] payload = File.ReadAllBytes(packPath);
            byte[] signature = Convert.FromBase64String(File.ReadAllText(signaturePath).Trim());
            return Verify(payload, signature) ? System.Text.Encoding.UTF8.GetString(payload) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
        {
            return null;
        }
    }

    /// <summary>Verifies an in-memory payload against a detached signature.</summary>
    public bool Verify(byte[] payload, byte[] signature)
    {
        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(_publicKeySpki, out _);
            return ecdsa.VerifyData(payload, signature, HashAlgorithmName.SHA256);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    /// <summary>
    /// Loads a verified adapter pack from <paramref name="packPath"/>, falling
    /// back to the built-in baseline when verification or schema validation fails.
    /// </summary>
    public AdapterPack LoadAdapterPackOrBaseline(string packPath)
    {
        string? json = LoadVerified(packPath);
        if (json is not null)
        {
            try
            {
                return AdapterPack.Load(json);
            }
            catch (InvalidDataException)
            {
            }
        }

        return BaselineAdapterPack.Load();
    }

    /// <summary>
    /// Loads a verified rule pack from <paramref name="packPath"/>, falling
    /// back to the built-in baseline when verification or schema validation fails.
    /// </summary>
    public RulePack LoadRulePackOrBaseline(string packPath)
    {
        string? json = LoadVerified(packPath);
        if (json is not null)
        {
            try
            {
                return RulePack.Load(json);
            }
            catch (InvalidDataException)
            {
            }
        }

        return BaselineRulePack.Load();
    }
}
