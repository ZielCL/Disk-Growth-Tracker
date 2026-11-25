using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace DiskGrowth
{
    internal sealed class UpdateRelease
    {
        private const string Channel = "https://raw.githubusercontent.com/ZielCL/Disk-Growth-Tracker/main/";
        public static readonly Uri Manifest = new Uri(Channel + "updates/direct.txt");
        private const int MaxExecutableBytes = 25 * 1024 * 1024;
        public Version Version { get; private set; }
        public string Hash { get; private set; }
        public int Size { get; private set; }
        public Uri Download
        {
            get
            {
                return new Uri(Channel + "downloads/" +
                    Uri.EscapeDataString("Disk Growth Tracker " + Version.ToString(3) + ".exe"));
            }
        }

        public static UpdateRelease Parse(string text)
        {
            var match = Regex.Match(text.Trim(), @"\Aversion=([0-9]+\.[0-9]+\.[0-9]+)\r?\nsha256=([0-9a-fA-F]{64})\r?\nsize=([0-9]+)\z");
            Version version;
            int size;
            if (!match.Success || !Version.TryParse(match.Groups[1].Value, out version) ||
                !int.TryParse(match.Groups[3].Value, NumberStyles.None, CultureInfo.InvariantCulture, out size) ||
                size < 1 || size > MaxExecutableBytes)
                throw new InvalidDataException();
            return new UpdateRelease
            {
                Version = new Version(version.Major, version.Minor, version.Build, 0),
                Hash = match.Groups[2].Value.ToLowerInvariant(),
                Size = size
            };
        }

        public void Verify(string path, string expectedAssembly)
        {
            using (var stream = File.OpenRead(path))
            using (var sha = SHA256.Create())
            {
                if (stream.Length != Size)
                    throw new InvalidDataException();
                string actual = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
                if (!StringComparer.Ordinal.Equals(actual, Hash))
                    throw new InvalidDataException();
            }
            var name = AssemblyName.GetAssemblyName(path);
            if (name.Name != expectedAssembly || name.Version != Version)
                throw new InvalidDataException();
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
            using (var manifest = new MemoryStream())
            {
                await Transfer(UpdateRelease.Manifest, manifest, 256, token).ConfigureAwait(false);
                return UpdateRelease.Parse(new UTF8Encoding(false, true).GetString(manifest.ToArray()));
            }
        }

        public async Task<string> Download(UpdateRelease release, string versions, AssemblyName current, CancellationToken token)
        {
            if (release.Version <= current.Version)
                throw new InvalidDataException();
            string folder = Path.Combine(versions, release.Version.ToString(3) + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "Disk Growth Tracker.exe");
            try
            {
                using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 8192, true))
                    await Transfer(release.Download, output, release.Size, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                release.Verify(path, current.Name);
                return path;
            }
            catch
            {
                Discard(path);
                throw;
            }
        }

        public static Process Open(UpdateRelease release, string path, string expectedAssembly, IntPtr owner)
        {
            CheckWithWindows(release, path, expectedAssembly);
            return Process.Start(new ProcessStartInfo(path)
            {
                UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(path), ErrorDialog = true,
                ErrorDialogParentHandle = owner
            });
        }

        public static void CheckWithWindows(UpdateRelease release, string path, string expectedAssembly)
        {
            // Apply Windows attachment policy before opening a downloaded executable.
            var services = (AttachmentExecute)Activator.CreateInstance(Type.GetTypeFromCLSID(
                new Guid("4125DD96-E03A-4103-8F70-E0597D803B9C"), true));
            try
            {
                services.SetClientTitle("Disk Growth Tracker");
                services.SetLocalPath(path);
                services.SetSource(release.Download.AbsoluteUri);
                services.Save();
            }
            finally
            {
                Marshal.ReleaseComObject(services);
            }
            release.Verify(path, expectedAssembly);
        }

        public static void Discard(string path)
        {
            if (path == null)
                return;
            try
            {
                File.Delete(path);
                Directory.Delete(Path.GetDirectoryName(path));
            }
            catch (Exception ex)
            {
                if (!Scanner.IsFileError(ex))
                    throw;
            }
        }

        private async Task Transfer(Uri uri, Stream destination, int limit, CancellationToken token)
        {
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                deadline.CancelAfter(TimeSpan.FromSeconds(25));
                using (var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    if (response.Content.Headers.ContentLength > limit)
                        throw new InvalidDataException();
                    using (deadline.Token.Register(response.Dispose))
                    using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    {
                        var bytes = new byte[8192];
                        int total = 0;
                        int read;
                        while ((read = await stream.ReadAsync(bytes, 0, bytes.Length, deadline.Token).ConfigureAwait(false)) > 0)
                        {
                            total += read;
                            if (total > limit)
                                throw new InvalidDataException();
                            await destination.WriteAsync(bytes, 0, read, deadline.Token).ConfigureAwait(false);
                        }
                    }
                }
            }
        }

        public void Dispose()
        {
            client.Dispose();
        }

        [ComImport, Guid("73DB1241-1E85-4581-8E4F-A81E1D0F8C57"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface AttachmentExecute
        {
            void SetClientTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
            void SetClientGuid(ref Guid guid);
            void SetLocalPath([MarshalAs(UnmanagedType.LPWStr)] string path);
            void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);
            void SetSource([MarshalAs(UnmanagedType.LPWStr)] string source);
            void SetReferrer([MarshalAs(UnmanagedType.LPWStr)] string referrer);
            void CheckPolicy();
            void Prompt(IntPtr owner, uint prompt, out uint action);
            void Save();
        }
    }
}
