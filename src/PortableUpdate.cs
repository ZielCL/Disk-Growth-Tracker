using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace DiskGrowth
{
    internal sealed class UpdateRelease
    {
        private const string Repository = "https://github.com/ZielCL/Disk-Growth-Tracker/";
        public static readonly Uri Manifest = new Uri("https://raw.githubusercontent.com/ZielCL/Disk-Growth-Tracker/main/updates/latest.txt");
        public Version Version { get; private set; }
        public Uri DownloadPage
        {
            get
            {
                return new Uri(Repository + "blob/main/downloads/" +
                    Uri.EscapeDataString("Disk Growth Tracker " + Version.ToString(3) + ".exe"));
            }
        }

        public static UpdateRelease Parse(string text)
        {
            var match = Regex.Match(text.Trim(), @"\Aversion=([0-9]+\.[0-9]+\.[0-9]+)\z");
            Version version;
            if (!match.Success || !Version.TryParse(match.Groups[1].Value, out version))
                throw new InvalidDataException();
            return new UpdateRelease { Version = new Version(version.Major, version.Minor, version.Build, 0) };
        }
    }

    internal sealed class PortableUpdate : IDisposable
    {
        private readonly HttpClient client;

        public PortableUpdate(HttpMessageHandler handler = null)
        {
            if (handler == null)
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            client = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false });
            client.Timeout = TimeSpan.FromSeconds(20);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("DiskGrowthTracker");
            client.DefaultRequestHeaders.CacheControl = new CacheControlHeaderValue { NoCache = true };
        }

        public async Task<UpdateRelease> Check(CancellationToken token)
        {
            const int limit = 128;
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                deadline.CancelAfter(TimeSpan.FromSeconds(20));
                using (var response = await client.GetAsync(UpdateRelease.Manifest, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    if (response.Content.Headers.ContentLength > limit)
                        throw new InvalidDataException();
                    using (deadline.Token.Register(response.Dispose))
                    using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    {
                        var bytes = new byte[limit + 1];
                        int total = 0;
                        int read;
                        while (total < bytes.Length && (read = await stream.ReadAsync(bytes, total, bytes.Length - total, deadline.Token).ConfigureAwait(false)) > 0)
                            total += read;
                        if (total > limit)
                            throw new InvalidDataException();
                        return UpdateRelease.Parse(new UTF8Encoding(false, true).GetString(bytes, 0, total));
                    }
                }
            }
        }

        public void Dispose()
        {
            client.Dispose();
        }
    }
}
