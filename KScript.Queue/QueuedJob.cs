using System.Globalization;

using KScript.Runtime;

namespace KScript.Queueing
{
    /// <summary>
    /// A job as a queue keeps it: what to run (the job's name and file, and its fields as JSON), where, how often it
    /// has been tried, and the options it was dispatched with. Drivers store it as JSON.
    /// </summary>
    public sealed class QueuedJob
    {
        public string Id { get; set; }

        /// <summary>The job type's name, such as SendWelcome.</summary>
        public string Job { get; set; }

        /// <summary>The file that declares the job: relative to the project folder when it is inside it.</summary>
        public string File { get; set; }

        public string Queue { get; set; } = "default";

        public string Connection { get; set; }

        /// <summary>The fields, as JSON data.</summary>
        public Dictionary<string, object> Fields { get; set; } = new Dictionary<string, object>(StringComparer.Ordinal);

        /// <summary>Attempts so far, counting the one running now.</summary>
        public int Attempts { get; set; }

        public int Tries { get; set; } = 3;

        /// <summary>Seconds before each retry; null for exponential.</summary>
        public List<double> Backoff { get; set; }

        public double Timeout { get; set; } = 60;

        /// <summary>After this, no more retries.</summary>
        public DateTime? RetryUntil { get; set; }

        public List<string> RetryOn { get; set; } = new List<string>();

        public List<string> FailOn { get; set; } = new List<string>();

        /// <summary>The key that keeps identical jobs from being queued twice, and when that lock ends.</summary>
        public string UniqueKey { get; set; }

        public DateTime DispatchedAt { get; set; }

        /// <summary>When the job may run: now, or later for a delayed or retried job.</summary>
        public DateTime AvailableAt { get; set; }

        /// <summary>While a worker runs the job: when its lease ends, after which another worker may take it.</summary>
        public DateTime? ReservedUntil { get; set; }

        /// <summary>The jobs to dispatch, in order, once this one succeeds.</summary>
        public List<Dictionary<string, object>> Chain { get; set; } = new List<Dictionary<string, object>>();

        /// <summary>The last error: type, message, file, line.</summary>
        public Dictionary<string, object> LastError { get; set; }

        public DateTime? FailedAt { get; set; }

        /// <summary>The options a retry needs, from the job type.</summary>
        internal JobOptions Options => new JobOptions { Tries = Tries, Backoff = Backoff, Timeout = Timeout, RetryOn = RetryOn, FailOn = FailOn };

        /// <summary>A new id: sortable by time, then random.</summary>
        public static string NewId() => DateTime.UtcNow.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture) + Guid.NewGuid().ToString("N").Substring(0, 8);

        public QueuedJob Copy() => FromMap(ToMap());

        public Dictionary<string, object> ToMap()
        {
            var map = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["id"] = Id,
                ["job"] = Job,
                ["file"] = File,
                ["queue"] = Queue,
                ["connection"] = Connection,
                ["fields"] = Fields,
                ["attempts"] = (double)Attempts,
                ["tries"] = (double)Tries,
                ["backoff"] = Backoff?.Select(b => (object)b).ToList(),
                ["timeout"] = Timeout,
                ["retry_until"] = Time(RetryUntil),
                ["retry_on"] = RetryOn.Cast<object>().ToList(),
                ["fail_on"] = FailOn.Cast<object>().ToList(),
                ["unique_key"] = UniqueKey,
                ["dispatched_at"] = Time(DispatchedAt),
                ["available_at"] = Time(AvailableAt),
                ["reserved_until"] = Time(ReservedUntil),
                ["chain"] = Chain.Cast<object>().ToList(),
                ["last_error"] = LastError,
                ["failed_at"] = Time(FailedAt),
            };
            return map;
        }

        public string ToJson() => Values.ToJson(ToMap());

        public static QueuedJob FromJson(string json) =>
            Values.ParseJson(json) is Dictionary<string, object> map ? FromMap(map) : throw new KScriptException("A queued job file isn't a JSON object.", "Json");

        public static QueuedJob FromMap(Dictionary<string, object> map)
        {
            object Get(string key) => map.TryGetValue(key, out object v) ? v : null;
            string Str(string key) => Get(key) is object v ? Values.ToStr(v) : null;
            List<string> Names(string key) => Get(key) is List<object> l ? l.Select(Values.ToStr).ToList() : new List<string>();
            return new QueuedJob
            {
                Id = Str("id"),
                Job = Str("job"),
                File = Str("file"),
                Queue = Str("queue") ?? "default",
                Connection = Str("connection"),
                Fields = Get("fields") as Dictionary<string, object> ?? new Dictionary<string, object>(StringComparer.Ordinal),
                Attempts = Get("attempts") is double a ? (int)a : 0,
                Tries = Get("tries") is double t ? (int)t : 3,
                Backoff = Get("backoff") is List<object> b ? b.Select(x => Values.ToNumber(x, "backoff")).ToList() : null,
                Timeout = Get("timeout") is double to ? to : 60,
                RetryUntil = ParseTime(Get("retry_until")),
                RetryOn = Names("retry_on"),
                FailOn = Names("fail_on"),
                UniqueKey = Str("unique_key"),
                DispatchedAt = ParseTime(Get("dispatched_at")) ?? DateTime.UtcNow,
                AvailableAt = ParseTime(Get("available_at")) ?? DateTime.UtcNow,
                ReservedUntil = ParseTime(Get("reserved_until")),
                Chain = Get("chain") is List<object> c ? c.OfType<Dictionary<string, object>>().ToList() : new List<Dictionary<string, object>>(),
                LastError = Get("last_error") as Dictionary<string, object>,
                FailedAt = ParseTime(Get("failed_at")),
            };
        }

        /// <summary>The job as a script sees it: in @queue()->waiting(), ->failed() and kscript queue list --format json.</summary>
        public Dictionary<string, object> ToValue() => new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["id"] = Id,
            ["job"] = Job,
            ["queue"] = Queue,
            ["connection"] = Connection,
            ["fields"] = Fields,
            ["attempts"] = (double)Attempts,
            ["tries"] = (double)Tries,
            ["dispatched_at"] = DispatchedAt,
            ["available_at"] = AvailableAt,
            ["failed_at"] = FailedAt,
            ["error"] = LastError,
        };

        private static object Time(DateTime? time) => time?.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

        private static DateTime? ParseTime(object value) => value switch
        {
            string s when DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime parsed) => parsed,
            _ => null,
        };
    }
}
