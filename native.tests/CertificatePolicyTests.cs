using CraftLauncher;
using System.IO;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json.Nodes;

// 用仓库中的证书样本离线验证策略，覆盖原签名、交叉签名和整条证书链。
internal static class CertificatePolicyTests
{
    static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
    }

    // 禁止证书自动下载和在线吊销查询，使测试不依赖外部 CA 服务。
    static X509Chain NewChain() => new()
    {
        ChainPolicy = { RevocationMode = X509RevocationMode.NoCheck, DisableCertificateDownloads = true }
    };

    public static void Run()
    {
        var fixtures = Path.Combine(AppContext.BaseDirectory, "fixtures");
        var entries = JsonNode.Parse(File.ReadAllText(Path.Combine(fixtures, "emudhra-2026-10-23.json")))!.AsArray();
        Check(entries.Count == 16, "Expected original and cross-signed public CA certificates");
        Check(entries.Select(e => e!["spki_sha256"]!.GetValue<string>()).Distinct().Count() == 8, "Expected eight compromised CA keys");
        foreach (var item in entries)
        {
            var entry = item!;
            using var cert = X509Certificate2.CreateFromPem(entry["pem"]!.GetValue<string>());
            Check(Convert.ToHexString(SHA256.HashData(cert.RawData)) == entry["sha256"]!.GetValue<string>(), "Certificate fixture hash mismatch");
            Check(Convert.ToHexString(SHA256.HashData(cert.PublicKey.ExportSubjectPublicKeyInfo())) == entry["spki_sha256"]!.GetValue<string>(), "Public key fixture hash mismatch");
            Check(CertificatePolicy.BlockedAuthority(cert) == entry["name"]!.GetValue<string>(), "Compromised key not blocked");
            using var chain = NewChain(); chain.Build(cert);
            Check(!CertificatePolicy.Validate(cert, chain, SslPolicyErrors.None, false), "Compromised CA accepted");
            Check(!CertificatePolicy.Validate(cert, chain, SslPolicyErrors.RemoteCertificateChainErrors, true), "TLS override bypassed CA rejection");
        }

        // 另造一张同名但不同公钥的证书，确认策略不是只按颁发者名称拦截。
        using var key = ECDsa.Create();
        var request = new CertificateRequest("CN=EM DV TLS CA - G3B", key, HashAlgorithmName.SHA256);
        using var safe = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        using var safeChain = NewChain(); safeChain.Build(safe);
        Check(CertificatePolicy.BlockedAuthority(safe) == null, "Unrelated key with identical CA name blocked");
        Check(CertificatePolicy.Validate(safe, safeChain, SslPolicyErrors.None, false), "Valid platform validation result rejected");
        Check(!CertificatePolicy.Validate(safe, safeChain, SslPolicyErrors.RemoteCertificateNameMismatch, false), "Hostname mismatch accepted");
        Check(CertificatePolicy.Validate(safe, safeChain, SslPolicyErrors.RemoteCertificateNameMismatch, true), "Ordinary TLS override stopped working");
        Check(!CertificatePolicy.Validate(safe, safeChain, SslPolicyErrors.RemoteCertificateChainErrors, false), "Untrusted chain accepted");
        Check(!CertificatePolicy.Validate(safe, null, SslPolicyErrors.None, true), "Missing chain accepted");

        var files = Directory.GetFiles(Path.Combine(fixtures, "emudhra-chains"), "*.pem");
        Check(files.Length == 14, "Expected fourteen supplied TLS test chains");
        foreach (var file in files)
        {
            var certificates = new X509Certificate2Collection();
            certificates.ImportFromPem(File.ReadAllText(file));
            try
            {
                using var chain = NewChain();
                chain.ChainPolicy.ExtraStore.AddRange(certificates);
                chain.Build(certificates[0]);
                Check(!CertificatePolicy.Validate(certificates[0], chain, SslPolicyErrors.None, true), "Compromised chain accepted: " + Path.GetFileName(file));
                Check(!CertificatePolicy.Validate(safe, chain, SslPolicyErrors.None, true), "Compromised intermediate not inspected");
            }
            finally { foreach (var cert in certificates) cert.Dispose(); }
        }
    }
}
