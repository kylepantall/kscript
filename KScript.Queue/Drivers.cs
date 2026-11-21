using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using KScript.Runtime;

namespace KScript.Queueing
{
    /// <summary>
    /// Where queued jobs are kept. A driver hands each waiting job to one worker at a time: <see cref="Reserve"/> takes
    /// the next job that is due and leases it to the caller until its timeout (plus a margin) runs out, after which
    /// another worker may take it again (so a worker that dies loses no jobs).
    /// </summary>
    public interface IQueueDriver
    {
        /// <summary>memory, file or database.</summary>
        string Kind { get; }

        /// <summary>The connection's name in kscript.json.</summary>
        string Name { get; }

        void Push(QueuedJob job);

        /// <summary>The next due job on the first of <paramref name="queues"/> that has one, leased to the caller, or null.</summary>
        QueuedJob Reserve(IReadOnlyList<string> queues, DateTime now);

        /// <summary>A reserved job finished: forget it.</summary>
        void Complete(QueuedJob job);

        /// <summary>A reserved job goes back to waiting, due at <see cref="QueuedJob.AvailableAt"/>.</summary>
        void Release(QueuedJob job);

        /// <summary>A reserved job moves to the failed jobs.</summary>
        void Fail(QueuedJob job);

        /// <summary>Jobs waiting on a queue (all queues when null), due or not, oldest due first.</summary>
        List<QueuedJob> Waiting(string queue);

        /// <summary>Jobs that workers are running now.</summary>
        List<QueuedJob> Running();

        /// <summary>The names of the queues that have waiting jobs, sorted.</summary>
        List<string> QueueNames();

        List<QueuedJob> Failed();

        /// <summary>Removes the waiting jobs of a queue (all queues when null). Returns how many.</summary>
        int Clear(string queue);

        /// <summary>Puts a failed job back on its queue with its tries reset. False if there is no failed job with that id.</summary>
        bool Retry(string id);

        bool Forget(string id);

        /// <summary>Takes a unique-job lock until <paramref name="until"/>; false if a job holds it already.</summary>
        bool Lock(string key, DateTime until, DateTime now);

        void Unlock(string key);

        /// <summary>Removes failed jobs that failed before <paramref name="before"/>.</summary>
        int Prune(DateTime before);
    }

    /// <summary>Jobs in this process's memory: for kscript serve --with-worker and tests. They are gone when it stops.</summary>
    public sealed class MemoryQueueDriver : IQueueDriver
    {
        private static readonly ConcurrentDictionary<string, MemoryQueueDriver> Drivers = new ConcurrentDictionary<string, MemoryQueueDriver>(StringComparer.Ordinal);

        private readonly object gate = new object();
        private readonly List<QueuedJob> waiting = new List<QueuedJob>();
        private readonly Dictionary<string, QueuedJob> running = new Dictionary<string, QueuedJob>(StringComparer.Ordinal);
        private readonly List<QueuedJob> failed = new List<QueuedJob>();
        private readonly Dictionary<string, DateTime> locks = new Dictionary<string, DateTime>(StringComparer.Ordinal);

        private MemoryQueueDriver(string name) => Name = name;

        /// <summary>The memory queue with this key (one per app and connection for the whole process).</summary>
        public static MemoryQueueDriver Named(string key, string name) => Drivers.GetOrAdd(key, _ => new MemoryQueueDriver(name));

        public string Kind => "memory";

        public string Name { get; }

        public void Push(QueuedJob job)
        {
            lock (gate) waiting.Add(job.Copy());
        }

        public QueuedJob Reserve(IReadOnlyList<string> queues, DateTime now)
        {
            lock (gate)
            {
                Recover(now);
                foreach (string queue in queues)
                {
                    QueuedJob next = waiting.Where(j => j.Queue == queue && j.AvailableAt <= now).OrderBy(j => j.AvailableAt).ThenBy(j => j.Id, StringComparer.Ordinal).FirstOrDefault();
                    if (next == null) continue;
                    waiting.Remove(next);
                    next.Attempts++;
                    next.ReservedUntil = now.AddSeconds(next.Timeout + QueueRuntime.LeaseMargin);
                    running[next.Id] = next;
                    return next.Copy();
                }
                return null;
            }
        }

        /// <summary>Jobs whose lease ran out (their worker stopped) wait again.</summary>
        private void Recover(DateTime now)
        {
            foreach (QueuedJob lost in running.Values.Where(j => j.ReservedUntil <= now).ToList())
            {
                running.Remove(lost.Id);
                lost.ReservedUntil = null;
                lost.AvailableAt = now;
                waiting.Add(lost);
            }
        }

        public void Complete(QueuedJob job)
        {
            lock (gate) running.Remove(job.Id);
        }

        public List<string> QueueNames()
        {
            lock (gate) return waiting.Select(j => j.Queue).Distinct().OrderBy(q => q, StringComparer.Ordinal).ToList();
        }

        public void Release(QueuedJob job)
        {
            lock (gate)
            {
                running.Remove(job.Id);
                QueuedJob copy = job.Copy();
                copy.ReservedUntil = null;
                waiting.Add(copy);
            }
        }

        public void Fail(QueuedJob job)
        {
            lock (gate)
            {
                running.Remove(job.Id);
                QueuedJob copy = job.Copy();
                copy.ReservedUntil = null;
                failed.Add(copy);
            }
        }

        public List<QueuedJob> Waiting(string queue)
        {
            lock (gate) return waiting.Where(j => queue == null || j.Queue == queue).OrderBy(j => j.AvailableAt).ThenBy(j => j.Id, StringComparer.Ordinal).Select(j => j.Copy()).ToList();
        }

        public List<QueuedJob> Running()
        {
            lock (gate) return running.Values.Select(j => j.Copy()).ToList();
        }

        public List<QueuedJob> Failed()
        {
            lock (gate) return failed.OrderBy(j => j.FailedAt).Select(j => j.Copy()).ToList();
        }

        public int Clear(string queue)
        {
            lock (gate) return waiting.RemoveAll(j => queue == null || j.Queue == queue);
        }

        public bool Retry(string id)
        {
            lock (gate)
            {
                QueuedJob job = failed.FirstOrDefault(j => j.Id == id);
                if (job == null) return false;
                failed.Remove(job);
                QueueRuntime.ResetForRetry(job);
                waiting.Add(job);
                return true;
            }
        }

        public bool Forget(string id)
        {
            lock (gate) return failed.RemoveAll(j => j.Id == id) > 0;
        }

        public bool Lock(string key, DateTime until, DateTime now)
        {
            lock (gate)
            {
                if (locks.TryGetValue(key, out DateTime held) && held > now) return false;
                locks[key] = until;
                return true;
            }
        }

        public void Unlock(string key)
        {
            lock (gate) locks.Remove(key);
        }

        public int Prune(DateTime before)
        {
            lock (gate) return failed.RemoveAll(j => j.FailedAt < before);
        }

        /// <summary>Empties every memory queue (tests).</summary>
        internal static void ResetAll() => Drivers.Clear();
    }

    /// <summary>
    /// Jobs in files: one JSON file per job, in a folder per queue. A file's name starts with when the job is due, so
    /// the next job is the first name in the folder. A worker takes a job by moving its file into running/, which only
    /// one worker can do, so several workers on one machine can share a queue.
    /// </summary>
    public sealed class FileQueueDriver : IQueueDriver
    {
        public FileQueueDriver(string name, string folder)
        {
            Name = name;
            Folder = folder;
        }

        public string Kind => "file";

        public string Name { get; }

        /// <summary>The folder the queues are in.</summary>
        public string Folder { get; }

        private string RunningFolder => Path.Combine(Folder, "running");
        private string FailedFolder => Path.Combine(Folder, "failed");
        private string LocksFolder => Path.Combine(Folder, "unique");
        private string QueuesFolder => Path.Combine(Folder, "queues");

        private string QueueFolder(string queue) => Path.Combine(QueuesFolder, SafeName(queue));

        public List<string> QueueNames() =>
            Directory.Exists(QueuesFolder)
                ? Directory.GetDirectories(QueuesFolder).Where(d => Directory.EnumerateFiles(d, "*.json").Any()).Select(Path.GetFileName).OrderBy(q => q, StringComparer.Ordinal).ToList()
                : new List<string>();

        private static string SafeName(string queue)
        {
            var sb = new StringBuilder();
            foreach (char c in queue) sb.Append(char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '_');
            return sb.Length == 0 ? "default" : sb.ToString();
        }

        private static string FileName(QueuedJob job) =>
            job.AvailableAt.ToUniversalTime().ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture) + "-" + job.Id + ".json";

        private static void Write(string path, QueuedJob job)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temp = path + ".tmp-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            File.WriteAllText(temp, job.ToJson());
            Retrying(() => File.Move(temp, path, overwrite: true));
        }

        /// <summary>
        /// Runs a file operation, trying again for a moment when the file is busy: on Windows a file another worker is
        /// reading, or that was just written (and a virus scanner is looking at), can't be moved straight away.
        /// </summary>
        private static void Retrying(Action work)
        {
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    work();
                    return;
                }
                catch (Exception ex) when (attempt < 20 && ex is IOException or UnauthorizedAccessException && ex is not FileNotFoundException and not DirectoryNotFoundException)
                {
                    Thread.Sleep(10 * attempt);
                }
            }
        }

        /// <summary>A file's text, or null when it stays busy or has gone.</summary>
        private static string ReadText(string path)
        {
            try
            {
                string text = null;
                Retrying(() => text = File.ReadAllText(path));
                return text;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        private static QueuedJob Parse(string text)
        {
            try
            {
                return QueuedJob.FromJson(text);
            }
            catch (KScriptException)
            {
                return null;
            }
        }

        private static QueuedJob Read(string path) => ReadText(path) is string text ? Parse(text) : null;

        public void Push(QueuedJob job) => Write(Path.Combine(QueueFolder(job.Queue), FileName(job)), job);

        public QueuedJob Reserve(IReadOnlyList<string> queues, DateTime now)
        {
            Recover(now);
            string due = now.ToUniversalTime().ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture);
            foreach (string queue in queues)
            {
                string folder = QueueFolder(queue);
                if (!Directory.Exists(folder)) continue;
                foreach (string file in Directory.GetFiles(folder, "*.json").OrderBy(f => Path.GetFileName(f), StringComparer.Ordinal))
                {
                    string name = Path.GetFileName(file);
                    if (string.CompareOrdinal(name.Substring(0, Math.Min(17, name.Length)), due) > 0) break;
                    string id = name.Length > 18 ? name.Substring(18, name.Length - 23) : name;
                    string claimed = Path.Combine(RunningFolder, id + ".json");
                    Directory.CreateDirectory(RunningFolder);
                    // Creating the claim file is what decides which worker gets the job: only one CreateNew succeeds,
                    // on every system. (A rename alone isn't enough on Windows.)
                    if (!TryClaim(id, claimed)) continue;
                    try
                    {
                        File.Move(file, claimed);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        // Another worker ran it and finished, or the file is busy: let it go.
                        DropClaim(id);
                        continue;
                    }
                    // A move that had to copy can leave the original behind while another worker reads it.
                    if (File.Exists(file)) Retrying(() => File.Delete(file));
                    string text = ReadText(claimed);
                    if (text == null)
                    {
                        // Still busy: put it back for later.
                        Retrying(() => File.Move(claimed, file, overwrite: true));
                        DropClaim(id);
                        continue;
                    }
                    QueuedJob job = Parse(text);
                    if (job == null)
                    {
                        // Not a job file: put it with the failed ones so it doesn't block the queue.
                        Directory.CreateDirectory(FailedFolder);
                        Retrying(() => File.Move(claimed, Path.Combine(FailedFolder, "unreadable-" + id + ".json"), overwrite: true));
                        DropClaim(id);
                        continue;
                    }
                    job.Attempts++;
                    job.ReservedUntil = now.AddSeconds(job.Timeout + QueueRuntime.LeaseMargin);
                    Write(claimed, job);
                    return job;
                }
            }
            return null;
        }

        /// <summary>Jobs whose lease ran out (their worker stopped) wait again.</summary>
        private void Recover(DateTime now)
        {
            if (!Directory.Exists(RunningFolder)) return;
            foreach (string file in Directory.GetFiles(RunningFolder, "*.json"))
            {
                QueuedJob job = Read(file);
                if (job == null || job.ReservedUntil == null || job.ReservedUntil > now) continue;
                string recovered = file + ".recovering";
                try
                {
                    File.Move(file, recovered);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    continue;
                }
                job.ReservedUntil = null;
                job.AvailableAt = now;
                Push(job);
                File.Delete(recovered);
                DropClaim(job.Id);
            }
        }

        public void Complete(QueuedJob job)
        {
            string file = Path.Combine(RunningFolder, job.Id + ".json");
            Retrying(() => File.Delete(file));
            DropClaim(job.Id);
        }

        private string ClaimFile(string id) => Path.Combine(RunningFolder, id + ".claim");

        /// <summary>
        /// Takes the job with this id for this worker. A claim left by a worker that stopped before it moved the job
        /// (so there is no running file) is taken over after five minutes.
        /// </summary>
        private bool TryClaim(string id, string runningFile)
        {
            string claim = ClaimFile(id);
            for (int attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    using (new FileStream(claim, FileMode.CreateNew, FileAccess.Write))
                    {
                    }
                    return true;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    bool stale = !File.Exists(runningFile) && File.Exists(claim) && File.GetLastWriteTimeUtc(claim) < DateTime.UtcNow.AddMinutes(-5);
                    if (!stale) return false;
                    DropClaim(id);
                }
            }
            return false;
        }

        private void DropClaim(string id)
        {
            string claim = ClaimFile(id);
            Retrying(() => File.Delete(claim));
        }

        public void Release(QueuedJob job)
        {
            QueuedJob copy = job.Copy();
            copy.ReservedUntil = null;
            Push(copy);
            Complete(job);
        }

        public void Fail(QueuedJob job)
        {
            QueuedJob copy = job.Copy();
            copy.ReservedUntil = null;
            Write(Path.Combine(FailedFolder, job.Id + ".json"), copy);
            Complete(job);
        }

        private static List<QueuedJob> ReadAll(string folder, SearchOption option = SearchOption.TopDirectoryOnly) =>
            !Directory.Exists(folder) ? new List<QueuedJob>()
                : Directory.GetFiles(folder, "*.json", option).OrderBy(f => Path.GetFileName(f), StringComparer.Ordinal).Select(Read).Where(j => j != null).ToList();

        public List<QueuedJob> Waiting(string queue) =>
            (queue == null ? ReadAll(QueuesFolder, SearchOption.AllDirectories) : ReadAll(QueueFolder(queue)))
                .OrderBy(j => j.AvailableAt).ThenBy(j => j.Id, StringComparer.Ordinal).ToList();

        public List<QueuedJob> Running() => ReadAll(RunningFolder);

        public List<QueuedJob> Failed() => ReadAll(FailedFolder).OrderBy(j => j.FailedAt).ToList();

        public int Clear(string queue)
        {
            string folder = queue == null ? QueuesFolder : QueueFolder(queue);
            if (!Directory.Exists(folder)) return 0;
            string[] files = Directory.GetFiles(folder, "*.json", SearchOption.AllDirectories);
            int removed = 0;
            foreach (string file in files)
            {
                File.Delete(file);
                removed++;
            }
            return removed;
        }

        public bool Retry(string id)
        {
            string file = Path.Combine(FailedFolder, SafeName(id) + ".json");
            QueuedJob job = File.Exists(file) ? Read(file) : null;
            if (job == null) return false;
            QueueRuntime.ResetForRetry(job);
            Push(job);
            File.Delete(file);
            return true;
        }

        public bool Forget(string id)
        {
            string file = Path.Combine(FailedFolder, SafeName(id) + ".json");
            if (!File.Exists(file)) return false;
            File.Delete(file);
            return true;
        }

        private string LockFile(string key) =>
            Path.Combine(LocksFolder, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).Substring(0, 32).ToLowerInvariant());

        public bool Lock(string key, DateTime until, DateTime now)
        {
            string file = LockFile(key);
            Directory.CreateDirectory(LocksFolder);
            for (int attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    using (var stream = new FileStream(file, FileMode.CreateNew, FileAccess.Write))
                    {
                        byte[] bytes = Encoding.UTF8.GetBytes(until.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture));
                        stream.Write(bytes, 0, bytes.Length);
                    }
                    return true;
                }
                catch (IOException) when (File.Exists(file))
                {
                    // A lock that has run out is taken over.
                    string text;
                    try
                    {
                        text = File.ReadAllText(file);
                    }
                    catch (IOException)
                    {
                        return false;
                    }
                    if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime held) && held > now) return false;
                    File.Delete(file);
                }
            }
            return false;
        }

        public void Unlock(string key)
        {
            string file = LockFile(key);
            if (File.Exists(file)) File.Delete(file);
        }

        public int Prune(DateTime before)
        {
            int removed = 0;
            foreach (QueuedJob job in Failed().Where(j => j.FailedAt < before))
            {
                if (Forget(job.Id)) removed++;
            }
            return removed;
        }
    }
}
