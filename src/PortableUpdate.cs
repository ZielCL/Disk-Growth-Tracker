using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DiskGrowth
{
    internal sealed class UpdateRelease
    {
        private const string Channel = "https://raw.githubusercontent.com/ZielCL/Disk-Growth-Tracker/main/";
        public static readonly Uri Manifest = new Uri(Channel + "updates/stable.txt");
        public const int MaxBytes = 25 * 1024 * 1024;
        public Version Version { get; private set; }
        public string Hash { get; private set; }
        public int Size { get; private set; }
        public Uri Download { get { return new Uri(Channel + "downloads/Disk%20Growth%20Tracker%20" + Version.ToString(3) + ".exe"); } }

        public static UpdateRelease Parse(string content)
        {
            var match = Regex.Match(content.Trim(), @"\Aversion=([0-9]+\.[0-9]+\.[0-9]+)\r?\nsha256=([A-Fa-f0-9]{64})\r?\nsize=([0-9]+)\z");
            Version version;
            int size;
            if (!match.Success || !Version.TryParse(match.Groups[1].Value, out version) ||
                !int.TryParse(match.Groups[3].Value, NumberStyles.None, CultureInfo.InvariantCulture, out size) ||
                size < 1 || size > MaxBytes)
                throw new InvalidDataException();
            return new UpdateRelease
            {
                Version = new Version(version.Major, version.Minor, version.Build, 0),
                Hash = match.Groups[2].Value.ToLowerInvariant(),
                Size = size
            };
        }

        public void Verify(string path, string assemblyName)
        {
            if (new FileInfo(path).Length != Size || !StringComparer.Ordinal.Equals(PortableUpdate.HashFile(path), Hash))
                throw new InvalidDataException();
            AssemblyName assembly = AssemblyName.GetAssemblyName(path);
            if (assembly.Version != Version || !StringComparer.Ordinal.Equals(assembly.Name, assemblyName))
                throw new InvalidDataException();
        }
    }

    internal sealed class PortableUpdate : IDisposable
    {
        private const string Prefix = ".disk-growth-update-";
        private const string Helper = "apply.exe";
        private const string Payload = "payload.exe";
        private const string Backup = "previous.exe";
        private const string ManifestCopy = "release.txt";
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
                await Transfer(UpdateRelease.Manifest, manifest, 4096, token).ConfigureAwait(false);
                return UpdateRelease.Parse(new UTF8Encoding(false, true).GetString(manifest.ToArray()));
            }
        }

        public async Task<string> Prepare(UpdateRelease release, string executable, CancellationToken token)
        {
            string stage = Path.Combine(Path.GetDirectoryName(executable), Prefix + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stage);
            try
            {
                string payload = Path.Combine(stage, Payload);
                using (var output = new FileStream(payload, FileMode.CreateNew, FileAccess.Write, FileShare.None, 8192, true))
                    await Transfer(release.Download, output, release.Size, token).ConfigureAwait(false);
                release.Verify(payload, AssemblyName.GetAssemblyName(executable).Name);
                token.ThrowIfCancellationRequested();
                File.Copy(executable, Path.Combine(stage, Helper));
                File.WriteAllText(Path.Combine(stage, ManifestCopy), "version=" + release.Version.ToString(3) +
                    "\nsha256=" + release.Hash + "\nsize=" + release.Size.ToString(CultureInfo.InvariantCulture));
                return stage;
            }
            catch
            {
                Cleanup(stage);
                throw;
            }
        }

        private async Task Transfer(Uri uri, Stream output, int maximum, CancellationToken token)
        {
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                deadline.CancelAfter(TimeSpan.FromSeconds(25));
                using (var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    if (response.Content.Headers.ContentLength > maximum)
                        throw new InvalidDataException();
                    using (deadline.Token.Register(response.Dispose))
                    using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    {
                        var buffer = new byte[8192];
                        int total = 0;
                        int read;
                        while ((read = await stream.ReadAsync(buffer, 0, buffer.Length, deadline.Token).ConfigureAwait(false)) > 0)
                        {
                            total += read;
                            if (total > maximum)
                                throw new InvalidDataException();
                            await output.WriteAsync(buffer, 0, read, deadline.Token).ConfigureAwait(false);
                        }
                    }
                }
            }
        }

        public static string HashFile(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }

        public static void LaunchHelper(string stage, string executable)
        {
            using (var current = Process.GetCurrentProcess())
            using (var helper = Process.Start(new ProcessStartInfo(Path.Combine(stage, Helper),
                "--apply-update " + current.Id.ToString(CultureInfo.InvariantCulture) + " \"" + Path.GetFileName(executable) + "\"")
                { UseShellExecute = false, CreateNoWindow = true }))
            {
                if (helper == null)
                    throw new InvalidOperationException();
            }
        }

        public static bool IsStageName(string name)
        {
            Guid id;
            return name.StartsWith(Prefix, StringComparison.Ordinal) &&
                Guid.TryParseExact(name.Substring(Prefix.Length), "N", out id);
        }

        public static void Apply(string[] args, string helper, UiLanguage language)
        {
            var stage = Path.GetDirectoryName(helper);
            int parent;
            if (args.Length != 3 || !IsStageName(Path.GetFileName(stage)) || Path.GetFileName(helper) != Helper ||
                !int.TryParse(args[1], NumberStyles.None, CultureInfo.InvariantCulture, out parent) || parent <= 0 ||
                Path.GetFileName(args[2]) != args[2] || !args[2].EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                return;
            string target = Path.Combine(Path.GetDirectoryName(stage), args[2]);
            string backup = Path.Combine(stage, Backup);
            bool replaced = false;
            try
            {
                WaitForParent(parent);
                if (!StringComparer.Ordinal.Equals(HashFile(target), HashFile(helper)))
                    throw new InvalidDataException();
                var release = UpdateRelease.Parse(File.ReadAllText(Path.Combine(stage, ManifestCopy)));
                AssemblyName current = AssemblyName.GetAssemblyName(target);
                string payload = Path.Combine(stage, Payload);
                if (release.Version <= current.Version)
                    throw new InvalidDataException();
                release.Verify(payload, current.Name);
                File.Replace(payload, target, backup);
                replaced = true;
                StartApp(target, "--updated " + Path.GetFileName(stage), true);
            }
            catch (Exception ex)
            {
                if (!IsUpdateError(ex))
                    throw;
                try
                {
                    if (replaced)
                        File.Replace(backup, target, null);
                    else if (!File.Exists(target) && File.Exists(backup))
                        File.Move(backup, target);
                    StartApp(target, "--updated " + Path.GetFileName(stage), false);
                }
                catch (Exception recovery)
                {
                    if (!IsUpdateError(recovery))
                        throw;
                }
                MessageBox.Show(language.Text("No se pudo aplicar la actualización. Revisa si hay otra instancia abierta o si la carpeta permite escritura."),
                    language.Text("Actualización"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private static void WaitForParent(int id)
        {
            Process process;
            try { process = Process.GetProcessById(id); }
            catch (ArgumentException) { return; }
            using (process)
                if (!process.WaitForExit(20000))
                    throw new IOException();
        }

        private static void StartApp(string path, string arguments, bool verifyStartup)
        {
            using (var process = Process.Start(new ProcessStartInfo(path, arguments)
                { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(path) }))
                if (process == null || (verifyStartup && process.WaitForExit(700)))
                    throw new InvalidOperationException();
        }

        public static void Cleanup(string stage)
        {
            if (!IsStageName(Path.GetFileName(stage)) || !Directory.Exists(stage))
                return;
            try
            {
                if ((File.GetAttributes(stage) & FileAttributes.ReparsePoint) != 0)
                    return;
                foreach (string file in new[] { Helper, Payload, Backup, ManifestCopy })
                    File.Delete(Path.Combine(stage, file));
                Directory.Delete(stage);
            }
            catch (Exception ex)
            {
                if (!Scanner.IsFileError(ex))
                    throw;
            }
        }

        public static bool IsUpdateError(Exception ex)
        {
            return Scanner.IsFileError(ex) || ex is InvalidDataException || ex is BadImageFormatException ||
                ex is Win32Exception || ex is InvalidOperationException || ex is ArgumentException ||
                ex is NotSupportedException || ex is HttpRequestException || ex is OperationCanceledException;
        }

        public void Dispose()
        {
            client.Dispose();
        }
    }
}
