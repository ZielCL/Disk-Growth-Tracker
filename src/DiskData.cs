using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace DiskGrowth
{
    internal sealed class FolderSize
    {
        public string Path;
        public long Total;
        public long Direct;
        public int Files;
        public bool Partial;
    }

    internal sealed class Snapshot
    {
        public DateTime Date;
        public int Errors;
        public int Links;
        public readonly Dictionary<string, FolderSize> Folders =
            new Dictionary<string, FolderSize>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<FolderSize>> children =
            new Dictionary<string, List<FolderSize>>(StringComparer.OrdinalIgnoreCase);

        public void Add(FolderSize folder)
        {
            Folders.Add(folder.Path, folder);
            if (folder.Path.Length == 0)
                return;

            string parent = Path.GetDirectoryName(folder.Path) ?? "";
            List<FolderSize> group;
            if (!children.TryGetValue(parent, out group))
            {
                group = new List<FolderSize>();
                children.Add(parent, group);
            }
            group.Add(folder);
        }

        public FolderSize Get(string relative)
        {
            FolderSize folder;
            Folders.TryGetValue(relative, out folder);
            return folder;
        }

        public IEnumerable<FolderSize> Children(string relative)
        {
            List<FolderSize> group;
            return children.TryGetValue(relative, out group) ? group : Enumerable.Empty<FolderSize>();
        }

        public IEnumerable<string> DescendantPaths(string relative)
        {
            if (relative.Length == 0)
                return Folders.Keys.Where(path => path.Length > 0);

            string prefix = relative + Path.DirectorySeparatorChar;
            return Folders.Keys.Where(path => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        }
    }

    internal sealed class ScanProgress
    {
        public int Folders;
        public long Files;
    }

    internal sealed class FileSize
    {
        public string Path;
        public long Bytes;
    }

    internal sealed class LargestFiles
    {
        public FileSize[] Entries;
        public long Files;
        public int Errors;
        public int Links;
    }

    internal sealed class Scanner
    {
        private readonly CancellationToken cancellation;
        private readonly IProgress<ScanProgress> progress;
        private readonly Stopwatch timer = Stopwatch.StartNew();
        private readonly Snapshot snapshot = new Snapshot();
        private int folderCount;
        private long fileCount;

        public Scanner(CancellationToken cancellation, IProgress<ScanProgress> progress)
        {
            this.cancellation = cancellation;
            this.progress = progress;
        }

        public Snapshot Scan(string root)
        {
            cancellation.ThrowIfCancellationRequested();
            Visit(new DirectoryInfo(root), "", null);
            cancellation.ThrowIfCancellationRequested();
            snapshot.Date = DateTime.UtcNow;
            return snapshot;
        }

        public LargestFiles ScanLargest(string root, int limit)
        {
            if (limit < 1)
                throw new ArgumentOutOfRangeException("limit");
            cancellation.ThrowIfCancellationRequested();
            var largest = new LargestCollector(limit);
            Visit(new DirectoryInfo(root), "", (entry, relative, bytes) => largest.Add(entry, bytes));
            cancellation.ThrowIfCancellationRequested();
            return new LargestFiles
            {
                Entries = largest.Entries(),
                Files = fileCount,
                Errors = snapshot.Errors,
                Links = snapshot.Links
            };
        }

        public FileSnapshot ScanFiles(string root)
        {
            cancellation.ThrowIfCancellationRequested();
            var files = new FileSnapshot();
            Visit(new DirectoryInfo(root), "", (entry, relative, bytes) =>
                files.Add(Path.Combine(relative, entry.Name), bytes));
            cancellation.ThrowIfCancellationRequested();
            files.Date = DateTime.UtcNow;
            files.Omitted = (long)snapshot.Errors + snapshot.Links;
            return files;
        }

        private FolderSize Visit(DirectoryInfo directory, string relative, Action<FileSystemInfo, string, long> onFile)
        {
            var result = new FolderSize { Path = relative };
            folderCount++;
            try
            {
                foreach (var entry in directory.EnumerateFileSystemInfos())
                {
                    cancellation.ThrowIfCancellationRequested();
                    ReportProgress();
                    try
                    {
                        FileAttributes attributes = entry.Attributes;
                        if (attributes == (FileAttributes)(-1))
                        {
                            Omit(result);
                            continue;
                        }
                        if ((attributes & FileAttributes.ReparsePoint) != 0)
                        {
                            snapshot.Links++;
                            continue;
                        }
                        if ((attributes & FileAttributes.Directory) != 0)
                        {
                            var child = Visit(new DirectoryInfo(entry.FullName), Path.Combine(relative, entry.Name), onFile);
                            result.Total += child.Total;
                            result.Partial |= child.Partial;
                        }
                        else
                        {
                            long bytes = ((FileInfo)entry).Length;
                            result.Direct += bytes;
                            if (onFile != null)
                                onFile(entry, relative, bytes);
                            result.Files++;
                            fileCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        if (!IsFileError(ex))
                            throw;
                        Omit(result);
                    }
                }
            }
            catch (Exception ex)
            {
                if (!IsFileError(ex) || relative.Length == 0)
                    throw;
                Omit(result);
            }

            result.Total += result.Direct;
            if (onFile == null)
                snapshot.Add(result);
            return result;
        }

        private void Omit(FolderSize result)
        {
            snapshot.Errors++;
            result.Partial = true;
        }

        private void ReportProgress()
        {
            if (progress != null && timer.ElapsedMilliseconds >= 180)
            {
                progress.Report(new ScanProgress { Folders = folderCount, Files = fileCount });
                timer.Restart();
            }
        }

        public static bool IsFileError(Exception ex)
        {
            return ex is IOException || ex is UnauthorizedAccessException || ex is SecurityException;
        }

        private sealed class LargestCollector
        {
            private readonly int limit;
            private readonly SortedSet<FileSize> largest = new SortedSet<FileSize>(Comparer<FileSize>.Create((a, b) =>
            {
                int size = a.Bytes.CompareTo(b.Bytes);
                return size != 0 ? size : StringComparer.Ordinal.Compare(b.Path, a.Path);
            }));
            private FileSize smallest;

            public LargestCollector(int limit)
            {
                this.limit = limit;
            }

            public void Add(FileSystemInfo file, long bytes)
            {
                if (smallest != null && (bytes < smallest.Bytes ||
                    (bytes == smallest.Bytes && StringComparer.Ordinal.Compare(file.FullName, smallest.Path) >= 0)))
                    return;

                largest.Add(new FileSize { Path = file.FullName, Bytes = bytes });
                if (largest.Count > limit)
                    largest.Remove(smallest);
                if (largest.Count == limit)
                    smallest = largest.Min;
            }

            public FileSize[] Entries()
            {
                return largest.Reverse().ToArray();
            }
        }
    }

    internal sealed class HistoryEntry
    {
        public string FilePath;
        public DateTime Date;

        public override string ToString()
        {
            return Date.ToLocalTime().ToString("dd/MM/yyyy · HH:mm:ss", CultureInfo.InvariantCulture);
        }
    }

    internal sealed class History
    {
        public const int Limit = 12;
        private const int Version = 1;
        private const string DateFormat = "yyyyMMddHHmmssfffffff";
        private const string LocationFile = "location.txt";
        private readonly string location;

        public History(string dataDirectory)
        {
            location = dataDirectory;
        }

        public static string Normalize(string path)
        {
            var full = Path.GetFullPath(path.Trim().Trim('"'));
            var root = Path.GetPathRoot(full);
            return full.Length == root.Length ? full : full.TrimEnd(Path.DirectorySeparatorChar);
        }

        private string FindDirectory(string path)
        {
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(path.ToUpperInvariant()));
                return Path.Combine(location, BitConverter.ToString(hash).Replace("-", ""));
            }
        }

        public List<HistoryEntry> List(string root, int limit = Limit)
        {
            string directory = FindDirectory(root);
            if (!Directory.Exists(directory))
                return new List<HistoryEntry>();

            var entries = new List<HistoryEntry>();
            foreach (string file in Directory.EnumerateFiles(directory, "*.dgs"))
            {
                string name = Path.GetFileNameWithoutExtension(file);
                int separator = name.IndexOf('-');
                DateTime date;
                if (separator > 0 && DateTime.TryParseExact(name.Substring(0, separator), DateFormat,
                    CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out date))
                {
                    entries.Add(new HistoryEntry { FilePath = file, Date = date });
                }
            }
            return entries.OrderByDescending(entry => entry.Date).Take(limit).ToList();
        }

        public List<string> RecentLocations()
        {
            var known = new List<KeyValuePair<DateTime, string>>();
            try
            {
                if (Directory.Exists(location))
                {
                    foreach (string directory in Directory.EnumerateDirectories(location))
                    {
                        var file = new FileInfo(Path.Combine(directory, LocationFile));
                        try
                        {
                            if (file.Exists && file.Length > 0 && file.Length < 131072)
                            {
                                string root = Normalize(File.ReadAllText(file.FullName, Encoding.UTF8));
                                if (string.Equals(directory, FindDirectory(root), StringComparison.OrdinalIgnoreCase))
                                    known.Add(new KeyValuePair<DateTime, string>(file.LastWriteTimeUtc, root));
                            }
                        }
                        catch (Exception ex)
                        {
                            if (!Scanner.IsFileError(ex) && !(ex is ArgumentException) && !(ex is NotSupportedException))
                                throw;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (!Scanner.IsFileError(ex))
                    throw;
            }
            return known.OrderByDescending(item => item.Key).Take(8).Select(item => item.Value).ToList();
        }

        public void Save(string root, Snapshot snapshot)
        {
            string directory = FindDirectory(root);
            WriteSnapshot(root, snapshot.Date, Limit, writer =>
            {
                writer.Write(Version);
                writer.Write(snapshot.Errors);
                writer.Write(snapshot.Links);
                writer.Write(snapshot.Folders.Count);
                foreach (var folder in snapshot.Folders.Values)
                {
                    writer.Write(folder.Path);
                    writer.Write(folder.Total);
                    writer.Write(folder.Direct);
                    writer.Write(folder.Files);
                    writer.Write(folder.Partial);
                }
            });

            try
            {
                RememberLocation(directory, root, snapshot.Date);
            }
            catch (Exception ex)
            {
                if (!Scanner.IsFileError(ex))
                    throw;
                // The measurement is already committed.
            }
        }

        internal void WriteSnapshot(string root, DateTime date, int limit, Action<BinaryWriter> write, CancellationToken cancellation = default(CancellationToken))
        {
            string directory = FindDirectory(root);
            Directory.CreateDirectory(directory);
            string name = date.ToString(DateFormat, CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N");
            string temp = Path.Combine(directory, name + ".tmp");
            try
            {
                using (var file = File.Open(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var gzip = new GZipStream(file, CompressionMode.Compress))
                using (var writer = new BinaryWriter(gzip))
                    write(writer);
                cancellation.ThrowIfCancellationRequested();
                File.Move(temp, Path.Combine(directory, name + ".dgs"));
            }
            finally
            {
                if (File.Exists(temp))
                    File.Delete(temp);
            }

            try
            {
                var files = Directory.GetFiles(directory, "*.dgs");
                foreach (string old in files.OrderByDescending(file => file, StringComparer.Ordinal).Skip(limit))
                    File.Delete(old);
            }
            catch (Exception ex)
            {
                if (!Scanner.IsFileError(ex))
                    throw;
                // The committed snapshot is usable even if old files are temporarily locked.
            }
        }

        private static void RememberLocation(string directory, string root, DateTime date)
        {
            string file = Path.Combine(directory, LocationFile);
            if (!File.Exists(file))
            {
                string temp = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".tmp");
                try
                {
                    File.WriteAllText(temp, root, Encoding.UTF8);
                    try
                    {
                        File.Move(temp, file);
                    }
                    catch (IOException)
                    {
                        if (!File.Exists(file))
                            throw;
                    }
                }
                finally
                {
                    if (File.Exists(temp))
                        File.Delete(temp);
                }
            }
            File.SetLastWriteTimeUtc(file, date);
        }

        public Snapshot Read(HistoryEntry entry)
        {
            using (var file = File.Open(entry.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
            using (var gzip = new GZipStream(file, CompressionMode.Decompress))
            using (var reader = new BinaryReader(gzip))
            {
                if (reader.ReadInt32() != Version)
                    throw new InvalidDataException("Formato de historial desconocido.");

                var snapshot = new Snapshot
                {
                    Date = entry.Date,
                    Errors = reader.ReadInt32(),
                    Links = reader.ReadInt32()
                };
                int count = reader.ReadInt32();
                for (int i = 0; i < count; i++)
                {
                    snapshot.Add(new FolderSize
                    {
                        Path = reader.ReadString(),
                        Total = reader.ReadInt64(),
                        Direct = reader.ReadInt64(),
                        Files = reader.ReadInt32(),
                        Partial = reader.ReadBoolean()
                    });
                }
                if (snapshot.Get("") == null)
                    throw new InvalidDataException("La medición está incompleta.");
                return snapshot;
            }
        }
    }

    internal sealed class ComparisonRow
    {
        public string Path;
        public long Before;
        public long Now;
        public long Change { get { return Now - Before; } }
        public bool DirectFiles;
        public bool NowPartial;
        public bool BeforePartial;
        public bool Partial { get { return NowPartial || BeforePartial; } }
        public bool ExistsNow;

        public static IEnumerable<ComparisonRow> InFolder(Snapshot current, Snapshot baseline, string path, bool recursive = false)
        {
            var nowFolder = current.Get(path);
            var oldFolder = baseline == null ? null : baseline.Get(path);
            var paths = Paths(current, path, recursive);
            if (baseline != null)
                paths = paths.Union(Paths(baseline, path, recursive), StringComparer.OrdinalIgnoreCase);

            foreach (string child in paths)
            {
                var now = current.Get(child);
                var before = baseline == null ? null : baseline.Get(child);
                var row = Create(child, now, before, false);
                if (now == null)
                    row.NowPartial = HasIncompleteAncestor(current, child);
                if (before == null && baseline != null)
                    row.BeforePartial = HasIncompleteAncestor(baseline, child);
                yield return row;
            }

            if ((nowFolder != null && nowFolder.Files > 0) || (oldFolder != null && oldFolder.Files > 0))
                yield return Create(path, nowFolder, oldFolder, true);
        }

        private static IEnumerable<string> Paths(Snapshot snapshot, string path, bool recursive)
        {
            return recursive ? snapshot.DescendantPaths(path) : snapshot.Children(path).Select(folder => folder.Path);
        }

        private static bool HasIncompleteAncestor(Snapshot snapshot, string path)
        {
            while (path.Length > 0)
            {
                path = System.IO.Path.GetDirectoryName(path) ?? "";
                var ancestor = snapshot.Get(path);
                if (ancestor != null)
                    return ancestor.Partial;
            }
            return false;
        }

        private static ComparisonRow Create(string path, FolderSize now, FolderSize before, bool direct)
        {
            return new ComparisonRow
            {
                Path = path,
                Now = now == null ? 0 : (direct ? now.Direct : now.Total),
                Before = before == null ? 0 : (direct ? before.Direct : before.Total),
                NowPartial = now != null && now.Partial,
                BeforePartial = before != null && before.Partial,
                ExistsNow = now != null,
                DirectFiles = direct
            };
        }
    }
}
