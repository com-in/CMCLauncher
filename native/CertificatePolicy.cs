using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace CMCLauncher;

// 启动器自己的 TLS 策略，不会修改 Windows 的证书存储。
public static class CertificatePolicy
{
    // Snapshot 2026-10-23. Match SPKI, including cross-signs and reissued certificates.
    // https://emudhra.we-have-a-problem.com/impacted-intermediates.json
    static readonly Dictionary<string, string> DeniedKeys = new(StringComparer.Ordinal)
    {
        ["EBED6475C2C2D80FCB199F3FE8DD6E6B3CE60B77EB15D4E42C3DEC0C6F5B5105"] = "EM DV TLS CA - G3B",
        ["43BFFA19D6EF651451F6841E019931E96F6F2EAEFF28AB4B6F1CE4576DE2717B"] = "EM OV TLS CA - G3B",
        ["E0254C7FCDC265E2F0F8A428DABB75AB4AE039B4872D62F5EE57F0877D5A8A41"] = "EM EV TLS CA - G3B",
        ["49DD353DC2B919A3770645117D0E60C16115E7D1BC259EEFE734B00EA434FE5F"] = "EM DV TLS CA - G3C",
        ["63ECEFEE431D49523BC1136C4E72D0206A477086CAF606CD5CF025686C109CF8"] = "EM OV TLS CA - G3C",
        ["5C0660A2B164910D8FF589C18E14552E9E0EC237DCEFE2CB736D41A4811F81A0"] = "EM EV TLS CA - G3C",
        ["EC4FFA1AD0AC88081314B7AAFFEF6A91F97CD57291BB5BA686C5415F5EB4F6B6"] = "EM OV CS CA - G3B",
        ["92C063D67E8F14DB71D288E11266FFDAF69CBF115F7BAB14F3BC2F68D25E0E30"] = "EM OV CS CA - G3C",
    };

    // 按公钥匹配，证书重新签发或交叉签名后仍能识别同一把密钥。
    public static string? BlockedAuthority(X509Certificate2 certificate)
    {
        var fingerprint = Convert.ToHexString(SHA256.HashData(certificate.PublicKey.ExportSubjectPublicKeyInfo()));
        return DeniedKeys.GetValueOrDefault(fingerprint);
    }

    public static bool Validate(X509Certificate? certificate, X509Chain? chain, SslPolicyErrors errors, bool ignoreSslErrors, Action<string>? blocked = null)
    {
        // An absent chain cannot be inspected, even when the user ignores ordinary TLS errors.
        if (certificate == null || chain == null || chain.ChainElements.Count == 0) return false;
        try
        {
            using var leaf = new X509Certificate2(certificate);
            var authority = BlockedAuthority(leaf);
            foreach (var element in chain.ChainElements)
                authority ??= BlockedAuthority(element.Certificate);
            // 拒绝列表优先于用户的 SSL 错误忽略选项。
            if (authority != null)
            {
                blocked?.Invoke(authority);
                return false;
            }
            return ignoreSslErrors || errors == SslPolicyErrors.None;
        }
        catch (CryptographicException) { return false; }
    }
}
