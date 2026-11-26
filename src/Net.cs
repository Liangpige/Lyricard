// ---------------------------------------------------------------------------
// Net.cs
//
/// TLS bootstrap shared by every outgoing request (the GitHub update check and the three
/// lyric sources). In-box .NET Framework defaults to SSL3/TLS1.0, which every modern HTTPS
/// endpoint now refuses - the symptom is WebException "请求被终止: 未能创建 SSL/TLS 安全通道".
// ---------------------------------------------------------------------------
using System;
using System.Net;

namespace Amc
{
    /// <summary>One-shot switch that makes ServicePointManager negotiate TLS 1.2.</summary>
    static class Net
    {
        static bool _ready;

        /// <summary>
        /// Raise the process-wide <see cref="ServicePointManager.SecurityProtocol"/> to include
        /// TLS 1.2. Written as a numeric flag on purpose: the project compiles against .NET 4.0
        /// reference assemblies, where <c>SecurityProtocolType.Tls12</c> does not exist yet.
        /// Idempotent and safe to call before every request.
        /// </summary>
        public static void EnsureTls()
        {
            if (_ready) return;
            _ready = true;
            try
            {
                ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; // Tls12
            }
            catch
            {
                // Older SChannel without TLS 1.2 support: leave the defaults alone and let the
                // request fail with its own message rather than crashing here.
            }
        }
    }
}
