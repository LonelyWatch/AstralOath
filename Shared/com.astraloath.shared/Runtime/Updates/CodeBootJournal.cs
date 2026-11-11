using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace AstralOath.Shared.Updates
{
    public sealed class CodeBootJournal
    {
        private const string Header = "AstralOathCodeBoot/1";
        private const int MaxJournalBytes = 1024 * 1024;
        private const int DigestLength = 64;
        private readonly string path;

        public CodeBootJournal(string path)
        {
            this.path = Path.GetFullPath(path);
        }

        public long? LastHealthyVersion
        {
            get
            {
                using (AcquireJournal())
                    return Read().Healthy;
            }
        }

        public void Begin(long version)
        {
            RequireVersion(version);
            using (AcquireJournal())
            {
                var state = Read();
                if (state.Pending.HasValue || state.Rejected.Contains(version))
                    throw new InvalidOperationException("Another code boot is pending or this version was rejected.");
                state.Pending = version;
                Write(state);
            }
        }

        public void Confirm(long version)
        {
            RequireVersion(version);
            using (AcquireJournal())
            {
                var state = Read();
                if (state.Pending != version)
                    throw new InvalidOperationException("Only the pending code boot can be confirmed.");
                state.Pending = null;
                state.Healthy = version;
                Write(state);
            }
        }

        // Call once at process startup, before beginning any code boot.
        public long? RecoverInterruptedBoot()
        {
            using (AcquireJournal())
            {
                var state = Read();
                var interrupted = state.Pending;
                if (interrupted.HasValue)
                {
                    state.Pending = null;
                    state.Rejected.Add(interrupted.Value);
                    if (state.Healthy == interrupted)
                        state.Healthy = null;
                    Write(state);
                }
                return interrupted;
            }
        }

        public bool IsRejected(long version)
        {
            RequireVersion(version);
            using (AcquireJournal())
                return Read().Rejected.Contains(version);
        }

        private FileStream AcquireJournal()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            return new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }

        private State Read()
        {
            var state = new State();
            string record;
            try
            {
                using (var stream = File.OpenRead(path))
                {
                    if (stream.Length > MaxJournalBytes)
                        throw InvalidJournal();
                    using (var reader = new StreamReader(stream, Encoding.ASCII, false))
                        record = reader.ReadToEnd();
                }
            }
            catch (FileNotFoundException)
            {
                return state;
            }

            // A truncated list can still contain valid versions. Verify the entire
            // record before reading any state, including the rejected versions.
            int trailer = record.LastIndexOf('\n');
            if (trailer < 0 || record.Length - trailer - 1 != DigestLength)
                throw InvalidJournal();
            string payload = record.Substring(0, trailer);
            if (!string.Equals(Digest(payload), record.Substring(trailer + 1), StringComparison.Ordinal))
                throw InvalidJournal();

            string[] lines = payload.Split('\n');
            if (lines.Length < 3 || lines[0] != Header)
                throw InvalidJournal();
            state.Pending = ReadOptionalVersion(lines[1]);
            state.Healthy = ReadOptionalVersion(lines[2]);
            for (int i = 3; i < lines.Length; i++)
            {
                long? rejected = ReadOptionalVersion(lines[i]);
                if (!rejected.HasValue || rejected == state.Pending || rejected == state.Healthy ||
                    !state.Rejected.Add(rejected.Value))
                    throw InvalidJournal();
            }
            return state;
        }

        private void Write(State state)
        {
            var text = new StringBuilder(Header)
                .Append('\n').Append(FormatVersion(state.Pending))
                .Append('\n').Append(FormatVersion(state.Healthy));
            foreach (long version in state.Rejected)
                text.Append('\n').Append(FormatVersion(version));
            if (text.Length + 1 + DigestLength > MaxJournalBytes)
                throw new IOException("Code boot journal exceeds its size limit.");

            string temporary = path + ".pending";
            string payload = text.ToString();
            byte[] bytes = Encoding.ASCII.GetBytes(payload + "\n" + Digest(payload));
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
            if (File.Exists(path))
                File.Replace(temporary, path, null);
            else
                File.Move(temporary, path);
        }

        private static string FormatVersion(long? version) => (version ?? 0).ToString(CultureInfo.InvariantCulture);

        private static string Digest(string text)
        {
            using (var hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(Encoding.ASCII.GetBytes(text))).Replace("-", "");
        }

        private static long? ReadOptionalVersion(string text)
        {
            if (!long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out long version) ||
                FormatVersion(version) != text)
                throw InvalidJournal();
            return version == 0 ? (long?)null : version;
        }

        private static InvalidDataException InvalidJournal() => new InvalidDataException("Code boot journal is malformed.");

        private static void RequireVersion(long version)
        {
            if (version < 1)
                throw new ArgumentOutOfRangeException(nameof(version));
        }

        private sealed class State
        {
            public long? Pending;
            public long? Healthy;
            public readonly SortedSet<long> Rejected = new SortedSet<long>();
        }
    }
}
