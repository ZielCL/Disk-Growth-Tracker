using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading;

namespace DiskGrowth
{
    internal sealed class FileScanLimitException : Exception { }

    internal sealed class FileSnapshot
    {
        public const int MaxFiles = 100000;
        private const int MaxPathCharacters = 24000000;
        private int pathCharacters;
        public DateTime Date;
        public long Omitted;
        public readonly Dictionary<string, long> Files = new Dictionary<string, long>(StringComparer.Ordinal);

        public void Add(string path, long size)
        {
            if (Files.Count >= MaxFiles || path.Length > MaxPathCharacters - pathCharacters)
                throw new FileScanLimitException();
            Files.Add(path, size);
            pathCharacters += path.Length;
        }
    }

    internal sealed class FileHistory
    {
        public const int Limit = 6;
        private const int Version = 1;
        private readonly History index;

        public FileHistory(string directory)
        {
            index = new History(directory);
        }

        public List<HistoryEntry> List(string root)
        {
            return index.List(root, Limit);
        }

        public void Save(string root, FileSnapshot snapshot, CancellationToken token)
        {
            index.WriteSnapshot(root, snapshot.Date, Limit, writer =>
            {
                writer.Write(Version);
                writer.Write(snapshot.Omitted);
                writer.Write(snapshot.Files.Count);
                foreach (var file in snapshot.Files)
                {
                    token.ThrowIfCancellationRequested();
                    writer.Write(file.Key);
                    writer.Write(file.Value);
                }
            }, token);
        }

        public FileSnapshot Read(HistoryEntry entry, CancellationToken token = default(CancellationToken))
        {
            using (var stream = File.Open(entry.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
            using (var gzip = new GZipStream(stream, CompressionMode.Decompress))
            using (var reader = new BinaryReader(gzip))
            {
                if (reader.ReadInt32() != Version)
                    throw new InvalidDataException();
                var snapshot = new FileSnapshot { Date = entry.Date, Omitted = reader.ReadInt64() };
                int count = reader.ReadInt32();
                if (snapshot.Omitted < 0 || count < 0 || count > FileSnapshot.MaxFiles)
                    throw new InvalidDataException();
                for (int i = 0; i < count; i++)
                {
                    token.ThrowIfCancellationRequested();
                    string path = reader.ReadString();
                    long size = reader.ReadInt64();
                    if (size < 0 || !ValidRelativePath(path) || snapshot.Files.ContainsKey(path))
                        throw new InvalidDataException();
                    snapshot.Add(path, size);
                }
                return snapshot;
            }
        }

        private static bool ValidRelativePath(string path)
        {
            if (path.Length == 0 || Path.IsPathRooted(path) || path.IndexOfAny(new[] { ':', '\0' }) >= 0)
                return false;
            foreach (string part in path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                if (part.Length == 0 || part == "." || part == "..")
                    return false;
            return true;
        }
    }

    internal enum FileChangeKind { Added = 1, Removed, Grew, Shrank, Uncertain }

    internal sealed class FileChange
    {
        public string Path;
        public long? Before;
        public long? Now;
        public FileChangeKind Kind;
        public long? Delta { get { return Now - Before; } }

        public static IEnumerable<FileChange> Between(FileSnapshot before, FileSnapshot now)
        {
            foreach (var previous in before.Files)
            {
                long bytes;
                if (now.Files.TryGetValue(previous.Key, out bytes))
                {
                    if (bytes != previous.Value)
                        yield return New(previous.Key, previous.Value, bytes,
                            bytes > previous.Value ? FileChangeKind.Grew : FileChangeKind.Shrank);
                }
                else
                {
                    bool unknown = now.Omitted > 0;
                    yield return New(previous.Key, previous.Value, unknown ? (long?)null : 0L,
                        unknown ? FileChangeKind.Uncertain : FileChangeKind.Removed);
                }
            }
            foreach (var current in now.Files)
            {
                if (!before.Files.ContainsKey(current.Key))
                {
                    bool unknown = before.Omitted > 0;
                    yield return New(current.Key, unknown ? (long?)null : 0L, current.Value,
                        unknown ? FileChangeKind.Uncertain : FileChangeKind.Added);
                }
            }
        }

        private static FileChange New(string path, long? before, long? now, FileChangeKind kind)
        {
            return new FileChange { Path = path, Before = before, Now = now, Kind = kind };
        }
    }
}
