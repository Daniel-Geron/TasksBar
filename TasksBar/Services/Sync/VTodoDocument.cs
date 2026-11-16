using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace TasksBar.Services.Sync
{
    /// <summary>
    /// A minimal iCalendar (RFC 5545) document holding one VTODO. It keeps every original line, so
    /// properties TasksBar doesn't understand (due dates, priority, reminders, Tasks.org extras...)
    /// survive an edit untouched. Only the properties we change are rewritten.
    /// </summary>
    public class VTodoDocument
    {
        private readonly List<string> _lines; // unfolded content lines

        private VTodoDocument(List<string> lines) => _lines = lines;

        public static VTodoDocument? Parse(string ics)
        {
            var lines = Unfold(ics);
            var doc = new VTodoDocument(lines);
            return doc.FindTodo(out _, out _) ? doc : null;
        }

        public static VTodoDocument CreateNew(string uid)
        {
            string now = FormatUtc(DateTime.UtcNow);
            return new VTodoDocument(new List<string>
            {
                "BEGIN:VCALENDAR",
                "VERSION:2.0",
                "PRODID:-//TasksBar//TasksBar//EN",
                "BEGIN:VTODO",
                "UID:" + uid,
                "DTSTAMP:" + now,
                "CREATED:" + now,
                "LAST-MODIFIED:" + now,
                "STATUS:NEEDS-ACTION",
                "END:VTODO",
                "END:VCALENDAR"
            });
        }

        // ------------------------------------------------------------------ typed accessors

        public string Uid => GetText("UID") ?? "";
        public string Summary => GetText("SUMMARY") ?? "";
        public string Description => GetText("DESCRIPTION") ?? "";

        public bool IsCompleted =>
            string.Equals(GetText("STATUS"), "COMPLETED", StringComparison.OrdinalIgnoreCase) || GetRaw("COMPLETED") != null;

        /// <summary>The UID of the parent task (RELATED-TO with RELTYPE=PARENT, which is also the default).</summary>
        public string? ParentUid
        {
            get
            {
                foreach (var (name, parameters, value) in TodoProperties())
                {
                    if (name != "RELATED-TO") continue;
                    string? reltype = GetParam(parameters, "RELTYPE");
                    if (reltype == null || reltype.Equals("PARENT", StringComparison.OrdinalIgnoreCase))
                        return Unescape(value);
                }
                return null;
            }
        }

        /// <summary>When the task was created, used to show newest tasks first like Google does.</summary>
        public DateTime Created => ParseDate(GetRaw("CREATED")) ?? ParseDate(GetRaw("DTSTAMP")) ?? DateTime.MinValue;

        /// <summary>
        /// Manual order ("My order" in Tasks.org, also Apple Reminders and Nextcloud): X-APPLE-SORT-ORDER,
        /// lowest first. Tasks without one sort by creation time in seconds since 2001, as Apple does.
        /// </summary>
        public long SortKey =>
            long.TryParse(GetRaw("X-APPLE-SORT-ORDER"), NumberStyles.Integer, CultureInfo.InvariantCulture, out long order)
                ? order
                : (long)(Created - new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;

        public void SetSortKey(long value) => SetRaw("X-APPLE-SORT-ORDER", value.ToString(CultureInfo.InvariantCulture));

        public void SetSummary(string value) => SetText("SUMMARY", value);

        public void SetDescription(string value)
        {
            if (string.IsNullOrEmpty(value)) Remove("DESCRIPTION");
            else SetText("DESCRIPTION", value);
        }

        public void SetCompleted(bool completed)
        {
            if (completed == IsCompleted) return;
            if (completed)
            {
                SetRaw("STATUS", "COMPLETED");
                SetRaw("COMPLETED", FormatUtc(DateTime.UtcNow));
                SetRaw("PERCENT-COMPLETE", "100");
            }
            else
            {
                SetRaw("STATUS", "NEEDS-ACTION");
                Remove("COMPLETED");
                Remove("PERCENT-COMPLETE");
            }
        }

        public void SetParentUid(string? parentUid)
        {
            RemoveWhere(p => p.Name == "RELATED-TO");
            if (!string.IsNullOrEmpty(parentUid)) SetRaw("RELATED-TO", Escape(parentUid), ";RELTYPE=PARENT");
        }

        /// <summary>Bumps LAST-MODIFIED/DTSTAMP/SEQUENCE so other clients notice the change.</summary>
        public void Touch()
        {
            string now = FormatUtc(DateTime.UtcNow);
            SetRaw("LAST-MODIFIED", now);
            SetRaw("DTSTAMP", now);
            int.TryParse(GetRaw("SEQUENCE"), out int sequence);
            SetRaw("SEQUENCE", (sequence + 1).ToString(CultureInfo.InvariantCulture));
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            foreach (var line in _lines) Fold(sb, line);
            return sb.ToString();
        }

        // ------------------------------------------------------------------ property plumbing

        private bool FindTodo(out int begin, out int end)
        {
            begin = _lines.FindIndex(l => l.Equals("BEGIN:VTODO", StringComparison.OrdinalIgnoreCase));
            end = begin < 0 ? -1 : _lines.FindIndex(begin, l => l.Equals("END:VTODO", StringComparison.OrdinalIgnoreCase));
            return begin >= 0 && end > begin;
        }

        /// <summary>The VTODO's own properties with their line index, skipping nested components such as VALARM.</summary>
        private IEnumerable<(int Index, string Name, string Params, string Value)> TodoLines()
        {
            if (!FindTodo(out int begin, out int end)) yield break;
            int depth = 0;
            for (int i = begin + 1; i < end; i++)
            {
                string line = _lines[i];
                if (line.StartsWith("BEGIN:", StringComparison.OrdinalIgnoreCase)) { depth++; continue; }
                if (line.StartsWith("END:", StringComparison.OrdinalIgnoreCase)) { depth--; continue; }
                if (depth != 0) continue;
                var (name, parameters, value) = SplitLine(line);
                yield return (i, name, parameters, value);
            }
        }

        private IEnumerable<(string Name, string Params, string Value)> TodoProperties() =>
            TodoLines().Select(p => (p.Name, p.Params, p.Value));

        private string? GetRaw(string name) =>
            TodoLines().Where(p => p.Name == name).Select(p => p.Value).FirstOrDefault();

        private string? GetText(string name)
        {
            string? raw = GetRaw(name);
            return raw == null ? null : Unescape(raw);
        }

        private void SetText(string name, string value) => SetRaw(name, Escape(value));

        private void SetRaw(string name, string value, string parameters = "")
        {
            var existing = TodoLines().FirstOrDefault(p => p.Name == name);
            string line = name + parameters + ":" + value;
            if (existing.Name != null)
            {
                // Keep parameters such as LANGUAGE unless we pass our own
                _lines[existing.Index] = parameters.Length > 0 || existing.Params.Length == 0
                    ? line
                    : name + existing.Params + ":" + value;
            }
            else
            {
                FindTodo(out _, out int end);
                _lines.Insert(end, line);
            }
        }

        private void Remove(string name) => RemoveWhere(p => p.Name == name);

        private void RemoveWhere(Func<(int Index, string Name, string Params, string Value), bool> predicate)
        {
            foreach (var index in TodoLines().Where(predicate).Select(p => p.Index).OrderByDescending(i => i).ToList())
                _lines.RemoveAt(index);
        }

        // "NAME;PARAM=x;PARAM2="a:b":value" -> ("NAME", ";PARAM=x;PARAM2=\"a:b\"", "value")
        private static (string Name, string Params, string Value) SplitLine(string line)
        {
            bool quoted = false;
            int colon = -1, semicolon = -1;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"') quoted = !quoted;
                else if (!quoted && c == ';' && semicolon < 0) semicolon = i;
                else if (!quoted && c == ':') { colon = i; break; }
            }
            if (colon < 0) return (line.ToUpperInvariant(), "", "");
            int nameEnd = semicolon >= 0 && semicolon < colon ? semicolon : colon;
            return (line.Substring(0, nameEnd).ToUpperInvariant(), line.Substring(nameEnd, colon - nameEnd), line.Substring(colon + 1));
        }

        private static string? GetParam(string parameters, string name)
        {
            foreach (var part in parameters.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = part.IndexOf('=');
                if (eq > 0 && part.Substring(0, eq).Equals(name, StringComparison.OrdinalIgnoreCase))
                    return part.Substring(eq + 1).Trim('"');
            }
            return null;
        }

        // ------------------------------------------------------------------ text encoding

        public static string Escape(string value) => value
            .Replace("\\", "\\\\")
            .Replace(";", "\\;")
            .Replace(",", "\\,")
            .Replace("\r\n", "\n")
            .Replace("\r", "\n")
            .Replace("\n", "\\n");

        public static string Unescape(string value)
        {
            var sb = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c == '\\' && i + 1 < value.Length)
                {
                    char next = value[++i];
                    sb.Append(next switch { 'n' or 'N' => '\n', _ => next });
                }
                else sb.Append(c);
            }
            return sb.ToString();
        }

        private static List<string> Unfold(string ics)
        {
            var lines = new List<string>();
            foreach (var raw in ics.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                if ((raw.StartsWith(' ') || raw.StartsWith('\t')) && lines.Count > 0)
                    lines[^1] += raw.Substring(1);
                else if (raw.Length > 0)
                    lines.Add(raw);
            }
            return lines;
        }

        // Lines longer than 75 bytes continue on the next line after a space, without splitting a UTF-8 character
        private static void Fold(StringBuilder sb, string line)
        {
            int bytes = 0;
            for (int i = 0; i < line.Length; i++)
            {
                int width = char.IsHighSurrogate(line[i]) && i + 1 < line.Length ? 4 : Encoding.UTF8.GetByteCount(line[i].ToString());
                if (bytes + width > 75)
                {
                    sb.Append("\r\n ");
                    bytes = 1;
                }
                sb.Append(line[i]);
                if (width == 4) sb.Append(line[++i]);
                bytes += width;
            }
            sb.Append("\r\n");
        }

        private static string FormatUtc(DateTime utc) => utc.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

        private static DateTime? ParseDate(string? value)
        {
            if (string.IsNullOrEmpty(value)) return null;
            string[] formats = { "yyyyMMdd'T'HHmmss'Z'", "yyyyMMdd'T'HHmmss", "yyyyMMdd" };
            return DateTime.TryParseExact(value, formats, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var date) ? date : null;
        }
    }
}
