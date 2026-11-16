using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace TasksBar.Services.Sync
{
    /// <summary>
    /// Tasks over CalDAV (RFC 4791): Tasks.org Cloud, Nextcloud, Fastmail, Radicale, etc.
    /// Lists are calendar collections that accept VTODOs; a task's id is the URL of its .ics resource.
    /// Task data is cached by ETag, so a refresh only downloads tasks that changed.
    /// </summary>
    public class CalDavTaskBackend : ITaskBackend
    {
        private static readonly XNamespace D = "DAV:";
        private static readonly XNamespace C = "urn:ietf:params:xml:ns:caldav";

        private readonly HttpClient _http;
        private readonly Uri _homeUrl;
        private readonly string _displayName;

        // href -> cached item, per list. Guarded by _cacheLock (the Stream Deck server calls in from another thread).
        private readonly Dictionary<string, Dictionary<string, CachedItem>> _cache = new();
        private readonly object _cacheLock = new();

        private class CachedItem
        {
            public string Etag = "";
            public string Ics = "";
            public VTodoDocument Doc = null!;
        }

        private CalDavTaskBackend(HttpClient http, Uri homeUrl, string displayName)
        {
            _http = http;
            _homeUrl = homeUrl;
            _displayName = displayName;
        }

        public string DisplayName => _displayName;
        public Uri HomeUrl => _homeUrl;

        // ------------------------------------------------------------------ connecting

        public static HttpClient CreateHttpClient(string username, string password)
        {
            // Redirects are followed by hand: HttpClient drops the Authorization header on redirect
            var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(60) };
            string token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
            http.DefaultRequestHeaders.UserAgent.ParseAdd("TasksBar/1.0");
            return http;
        }

        /// <summary>Uses a calendar home URL found earlier by <see cref="DiscoverHomeAsync"/>.</summary>
        public static CalDavTaskBackend Create(string homeUrl, string username, string password, string displayName) =>
            new CalDavTaskBackend(CreateHttpClient(username, password), new Uri(homeUrl), displayName);

        /// <summary>
        /// Finds the user's calendar home from whatever URL they entered: the server root (via
        /// /.well-known/caldav and the current-user-principal), the principal, or the home itself.
        /// </summary>
        public static async Task<Uri> DiscoverHomeAsync(string serverUrl, string username, string password)
        {
            if (!serverUrl.Contains("://")) serverUrl = "https://" + serverUrl;
            var start = new Uri(serverUrl.EndsWith("/") ? serverUrl : serverUrl + "/");
            using var http = CreateHttpClient(username, password);

            var candidates = new List<Uri> { start, new Uri(start, "/.well-known/caldav") };
            foreach (var url in candidates)
            {
                var principal = await FindPropertyHrefAsync(http, url, D + "current-user-principal");
                if (principal == null) continue;

                var home = await FindPropertyHrefAsync(http, principal, C + "calendar-home-set");
                if (home != null) return home;
            }

            // Maybe they entered the principal or the home set directly
            var directHome = await FindPropertyHrefAsync(http, start, C + "calendar-home-set");
            if (directHome != null) return directHome;

            var (status, _, _) = await SendAsync(http, "PROPFIND", start, PropfindBody(D + "resourcetype"), depth: "1");
            if (status == HttpStatusCode.MultiStatus) return start;

            throw new InvalidOperationException("Couldn't find any task lists at that address. Check the server URL.");
        }

        private static async Task<Uri?> FindPropertyHrefAsync(HttpClient http, Uri url, XName property)
        {
            var (status, body, finalUrl) = await SendAsync(http, "PROPFIND", url, PropfindBody(property), depth: "0");
            if (status == HttpStatusCode.Unauthorized)
                throw new RemoteAuthException("The server didn't accept that username and password.");
            if (status != HttpStatusCode.MultiStatus || body == null) return null;

            var href = ParseXml(body)?.Descendants(property).Elements(D + "href").FirstOrDefault()?.Value;
            return string.IsNullOrWhiteSpace(href) ? null : new Uri(finalUrl, href.Trim());
        }

        // ------------------------------------------------------------------ lists

        public async Task<List<RemoteList>> GetListsAsync()
        {
            var body = PropfindBody(D + "displayname", D + "resourcetype", C + "supported-calendar-component-set");
            var xml = await MultiStatusAsync("PROPFIND", _homeUrl, body, "1");

            var lists = new List<RemoteList>();
            foreach (var response in xml.Descendants(D + "response"))
            {
                var href = response.Element(D + "href")?.Value;
                if (href == null) continue;
                var props = OkProps(response);
                if (props.Descendants(D + "resourcetype").Elements(C + "calendar").FirstOrDefault() == null) continue;

                // Calendars that only hold events are skipped. No component list means "anything".
                var comps = props.Descendants(C + "supported-calendar-component-set").Elements(C + "comp")
                                 .Select(c => (string?)c.Attribute("name")).ToList();
                if (comps.Count > 0 && !comps.Any(n => string.Equals(n, "VTODO", StringComparison.OrdinalIgnoreCase))) continue;

                var url = new Uri(_homeUrl, href.Trim());
                string title = props.Descendants(D + "displayname").FirstOrDefault()?.Value ?? "";
                if (string.IsNullOrWhiteSpace(title)) title = Uri.UnescapeDataString(url.Segments.Last().TrimEnd('/'));
                lists.Add(new RemoteList { Id = url.AbsoluteUri, Title = title });
            }
            return lists;
        }

        public async Task<RemoteList> CreateListAsync(string title)
        {
            var url = new Uri(_homeUrl, Guid.NewGuid().ToString("N") + "/");
            var props = new XElement(D + "prop",
                new XElement(D + "displayname", title),
                new XElement(C + "supported-calendar-component-set", new XElement(C + "comp", new XAttribute("name", "VTODO"))));

            var mkcalendar = new XDocument(new XElement(C + "mkcalendar",
                new XAttribute(XNamespace.Xmlns + "D", D), new XAttribute(XNamespace.Xmlns + "C", C),
                new XElement(D + "set", props)));
            var (status, _, _) = await SendAsync(_http, "MKCALENDAR", url, mkcalendar.ToString());

            if (!IsSuccess(status))
            {
                // Some servers only support the extended MKCOL (RFC 5689)
                var mkcol = new XDocument(new XElement(D + "mkcol",
                    new XAttribute(XNamespace.Xmlns + "D", D), new XAttribute(XNamespace.Xmlns + "C", C),
                    new XElement(D + "set", new XElement(D + "prop",
                        new XElement(D + "resourcetype", new XElement(D + "collection"), new XElement(C + "calendar")),
                        new XElement(D + "displayname", title),
                        new XElement(C + "supported-calendar-component-set", new XElement(C + "comp", new XAttribute("name", "VTODO")))))));
                (status, _, _) = await SendAsync(_http, "MKCOL", url, mkcol.ToString());
                EnsureSuccess(status, "create the list");
            }

            return new RemoteList { Id = url.AbsoluteUri, Title = title };
        }

        public async Task<string?> GetDefaultListIdAsync()
        {
            var lists = await GetListsAsync();
            return lists.FirstOrDefault(l => !StickyNoteSync.IsNotesList(l.Id, l.Title))?.Id;
        }

        // ------------------------------------------------------------------ tasks

        public async Task<List<RemoteTask>> GetTasksAsync(string listId, bool includeCompleted, bool includeNotes = true)
        {
            var listUrl = new Uri(listId);
            var xml = await MultiStatusAsync("PROPFIND", listUrl, PropfindBody(D + "getetag", D + "resourcetype"), "1");

            // Current etag of every resource in the collection
            var etags = new Dictionary<string, string>();
            foreach (var response in xml.Descendants(D + "response"))
            {
                var href = response.Element(D + "href")?.Value;
                if (href == null) continue;
                var props = OkProps(response);
                if (props.Descendants(D + "resourcetype").Elements(D + "collection").Any()) continue;
                var url = new Uri(listUrl, href.Trim());
                if (url.AbsoluteUri.TrimEnd('/') == listUrl.AbsoluteUri.TrimEnd('/')) continue;
                etags[url.AbsoluteUri] = props.Descendants(D + "getetag").FirstOrDefault()?.Value ?? "";
            }

            Dictionary<string, CachedItem> cache;
            List<string> stale;
            lock (_cacheLock)
            {
                if (!_cache.TryGetValue(listUrl.AbsoluteUri, out cache!))
                    _cache[listUrl.AbsoluteUri] = cache = new Dictionary<string, CachedItem>();

                foreach (var gone in cache.Keys.Except(etags.Keys).ToList()) cache.Remove(gone);
                stale = etags.Where(e => !cache.TryGetValue(e.Key, out var item) || item.Etag != e.Value || e.Value.Length == 0)
                             .Select(e => e.Key).ToList();
            }

            await DownloadAsync(listUrl, stale, cache);

            lock (_cacheLock)
            {
                var uidToHref = UidMap(listId);
                // Manual order first (shared with Tasks.org's "My order"), newest first among equals
                return cache
                    .OrderBy(c => c.Value.Doc.SortKey)
                    .ThenByDescending(c => c.Value.Doc.Created)
                    .ThenBy(c => c.Value.Doc.Summary, StringComparer.CurrentCulture)
                    .Select(c => ToRemote(c.Key, c.Value, uidToHref))
                    .Where(t => includeCompleted || !t.Completed)
                    .ToList();
            }
        }

        // calendar-multiget in batches, falling back to plain GETs if the server doesn't support it
        private async Task DownloadAsync(Uri listUrl, List<string> hrefs, Dictionary<string, CachedItem> cache)
        {
            foreach (var batch in hrefs.Chunk(50))
            {
                var report = new XDocument(new XElement(C + "calendar-multiget",
                    new XAttribute(XNamespace.Xmlns + "D", D), new XAttribute(XNamespace.Xmlns + "C", C),
                    new XElement(D + "prop", new XElement(D + "getetag"), new XElement(C + "calendar-data")),
                    batch.Select(h => new XElement(D + "href", new Uri(h).AbsolutePath))));

                var (status, body, finalUrl) = await SendAsync(_http, "REPORT", listUrl, report.ToString(), depth: "1");
                var received = new HashSet<string>();
                if (status == HttpStatusCode.MultiStatus && body != null && ParseXml(body) is XDocument xml)
                {
                    foreach (var response in xml.Descendants(D + "response"))
                    {
                        var href = response.Element(D + "href")?.Value;
                        var props = OkProps(response);
                        var data = props.Descendants(C + "calendar-data").FirstOrDefault()?.Value;
                        if (href == null || data == null) continue;
                        var url = new Uri(finalUrl, href.Trim()).AbsoluteUri;
                        Store(cache, url, props.Descendants(D + "getetag").FirstOrDefault()?.Value ?? "", data);
                        received.Add(url);
                    }
                }
                else EnsureNotAuthFailure(status);

                foreach (var missing in batch.Where(h => !received.Contains(h)))
                {
                    var (getStatus, ics, etag) = await GetIcsAsync(new Uri(missing));
                    if (getStatus == HttpStatusCode.OK && ics != null) Store(cache, missing, etag, ics);
                }
            }
        }

        private void Store(Dictionary<string, CachedItem> cache, string href, string etag, string ics)
        {
            var doc = VTodoDocument.Parse(ics);
            lock (_cacheLock)
            {
                if (doc == null) cache.Remove(href); // An event, not a task
                else cache[href] = new CachedItem { Etag = etag, Ics = ics, Doc = doc };
            }
        }

        public async Task<RemoteTask> GetTaskAsync(string listId, string taskId)
        {
            var item = await FetchAsync(listId, taskId);
            lock (_cacheLock) return ToRemote(taskId, item, UidMap(listId));
        }

        public async Task<RemoteTask> CreateTaskAsync(string listId, RemoteTask task)
        {
            var listUrl = new Uri(listId);
            string uid = Guid.NewGuid().ToString();
            var url = new Uri(listUrl, uid + ".ics");

            var doc = VTodoDocument.CreateNew(uid);
            doc.SetSummary(task.Title);
            doc.SetDescription(task.Notes);
            doc.SetCompleted(task.Completed);
            if (!string.IsNullOrEmpty(task.Parent)) doc.SetParentUid((await FetchAsync(listId, task.Parent)).Doc.Uid);

            // New tasks go to the top, like they appear in TasksBar
            var cache = CacheFor(listId);
            lock (_cacheLock)
            {
                if (cache.Count > 0) doc.SetSortKey(cache.Values.Min(c => c.Doc.SortKey) - SortGap);
            }

            string etag = await PutAsync(url, doc.ToString(), ifNoneMatch: true, ifMatch: null);
            Store(cache, url.AbsoluteUri, etag, doc.ToString());
            lock (_cacheLock) return ToRemote(url.AbsoluteUri, cache[url.AbsoluteUri], UidMap(listId));
        }

        public async Task<RemoteTask> UpdateTaskAsync(string listId, RemoteTask task)
        {
            // Read-modify-write on the original iCalendar data. If someone else changed it meanwhile
            // (412), re-read it and apply our fields again; if it's gone, report that.
            for (int attempt = 0; ; attempt++)
            {
                var item = await FetchAsync(listId, task.Id, forceRefresh: attempt > 0);
                var doc = VTodoDocument.Parse(item.Ics)!;
                doc.SetSummary(task.Title);
                doc.SetDescription(task.Notes);
                doc.SetCompleted(task.Completed);
                doc.Touch();

                try
                {
                    string etag = await PutAsync(new Uri(task.Id), doc.ToString(), ifNoneMatch: false, ifMatch: item.Etag);
                    var cache = CacheFor(listId);
                    Store(cache, task.Id, etag, doc.ToString());
                    lock (_cacheLock) return ToRemote(task.Id, cache[task.Id], UidMap(listId));
                }
                catch (PreconditionFailedException) when (attempt < 2) { }
            }
        }

        public async Task DeleteTaskAsync(string listId, string taskId)
        {
            var (status, _, _) = await SendAsync(_http, "DELETE", new Uri(taskId), null);
            lock (_cacheLock) CacheFor(listId).Remove(taskId);
            if (status == HttpStatusCode.NotFound || status == HttpStatusCode.Gone)
                throw new RemoteNotFoundException("The task no longer exists.");
            EnsureSuccess(status, "delete the task");
        }

        private const long SortGap = 1024;

        public async Task ReorderTaskAsync(string listId, IReadOnlyList<RemoteTask> newOrder, string movedTaskId)
        {
            int index = newOrder.ToList().FindIndex(t => t.Id == movedTaskId);
            if (index < 0 || newOrder.Count < 2) return;

            // Sort keys of everything shown (from the last refresh); only the moved task normally changes
            var keys = new Dictionary<string, long>();
            foreach (var task in newOrder)
                keys[task.Id] = (await FetchAsync(listId, task.Id)).Doc.SortKey;

            long? before = index > 0 ? keys[newOrder[index - 1].Id] : null;
            long? after = index < newOrder.Count - 1 ? keys[newOrder[index + 1].Id] : null;

            if (before == null) { await SetSortKeyAsync(listId, movedTaskId, after!.Value - SortGap); return; }
            if (after == null) { await SetSortKeyAsync(listId, movedTaskId, before.Value + SortGap); return; }
            if (after.Value - before.Value >= 2)
            {
                await SetSortKeyAsync(listId, movedTaskId, before.Value + (after.Value - before.Value) / 2);
                return;
            }

            // No room between the neighbours (equal keys): space the whole list out again
            long start = newOrder.Min(t => keys[t.Id]);
            for (int i = 0; i < newOrder.Count; i++)
            {
                long key = start + i * SortGap;
                if (keys[newOrder[i].Id] != key || newOrder[i].Id == movedTaskId)
                    await SetSortKeyAsync(listId, newOrder[i].Id, key);
            }
        }

        private async Task SetSortKeyAsync(string listId, string taskId, long key)
        {
            for (int attempt = 0; ; attempt++)
            {
                var item = await FetchAsync(listId, taskId, forceRefresh: attempt > 0);
                var doc = VTodoDocument.Parse(item.Ics)!;
                doc.SetSortKey(key);
                doc.Touch();
                try
                {
                    string etag = await PutAsync(new Uri(taskId), doc.ToString(), ifNoneMatch: false, ifMatch: item.Etag);
                    Store(CacheFor(listId), taskId, etag, doc.ToString());
                    return;
                }
                catch (PreconditionFailedException) when (attempt < 2) { }
            }
        }

        public async Task MoveTaskAsync(string fromListId, string taskId, string toListId)
        {
            // Copy the original data into the other calendar, then remove it here
            var item = await FetchAsync(fromListId, taskId, forceRefresh: true);
            var doc = VTodoDocument.Parse(item.Ics)!;
            doc.SetParentUid(null); // A parent in the old list would dangle
            doc.Touch();

            var target = new Uri(new Uri(toListId), new Uri(taskId).Segments.Last());
            string etag = await PutAsync(target, doc.ToString(), ifNoneMatch: true, ifMatch: null);
            Store(CacheFor(toListId), target.AbsoluteUri, etag, doc.ToString());

            await DeleteTaskAsync(fromListId, taskId);
        }

        // ------------------------------------------------------------------ helpers

        private Dictionary<string, CachedItem> CacheFor(string listId)
        {
            lock (_cacheLock)
            {
                string key = new Uri(listId).AbsoluteUri;
                if (!_cache.TryGetValue(key, out var cache)) _cache[key] = cache = new Dictionary<string, CachedItem>();
                return cache;
            }
        }

        private Dictionary<string, string> UidMap(string listId)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (href, item) in CacheFor(listId)) map[item.Doc.Uid] = href;
            return map;
        }

        private async Task<CachedItem> FetchAsync(string listId, string taskId, bool forceRefresh = false)
        {
            var cache = CacheFor(listId);
            lock (_cacheLock)
            {
                if (!forceRefresh && cache.TryGetValue(taskId, out var cached)) return cached;
            }

            var (status, ics, etag) = await GetIcsAsync(new Uri(taskId));
            if (status == HttpStatusCode.NotFound || status == HttpStatusCode.Gone)
            {
                lock (_cacheLock) cache.Remove(taskId);
                throw new RemoteNotFoundException("The task no longer exists.");
            }
            EnsureSuccess(status, "read the task");
            Store(cache, taskId, etag, ics!);
            lock (_cacheLock)
            {
                return cache.TryGetValue(taskId, out var item) ? item : throw new RemoteNotFoundException("That item isn't a task.");
            }
        }

        private async Task<(HttpStatusCode Status, string? Ics, string Etag)> GetIcsAsync(Uri url)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var response = await SendWithRedirectsAsync(_http, request);
            string? body = response.IsSuccessStatusCode ? await response.Content.ReadAsStringAsync() : null;
            EnsureNotAuthFailure(response.StatusCode);
            return (response.StatusCode, body, response.Headers.ETag?.ToString() ?? "");
        }

        private class PreconditionFailedException : Exception { }

        private async Task<string> PutAsync(Uri url, string ics, bool ifNoneMatch, string? ifMatch)
        {
            using var request = new HttpRequestMessage(HttpMethod.Put, url)
            {
                Content = new StringContent(ics, Encoding.UTF8, "text/calendar")
            };
            if (ifNoneMatch) request.Headers.TryAddWithoutValidation("If-None-Match", "*");
            if (!string.IsNullOrEmpty(ifMatch)) request.Headers.TryAddWithoutValidation("If-Match", ifMatch);

            using var response = await SendWithRedirectsAsync(_http, request);
            if (response.StatusCode == HttpStatusCode.PreconditionFailed)
            {
                if (ifNoneMatch) throw new InvalidOperationException("A task with that name already exists.");
                // If-Match failed: changed elsewhere, or deleted (some servers answer 412 for a missing resource)
                var (status, _, _) = await GetIcsAsync(url);
                if (status == HttpStatusCode.NotFound || status == HttpStatusCode.Gone)
                    throw new RemoteNotFoundException("The task no longer exists.");
                throw new PreconditionFailedException();
            }
            if (response.StatusCode == HttpStatusCode.NotFound || response.StatusCode == HttpStatusCode.Gone)
                throw new RemoteNotFoundException("The task no longer exists.");
            EnsureSuccess(response.StatusCode, "save the task");

            // Servers that rewrite the data don't return an ETag; ask for it so the next refresh doesn't re-download
            string? etag = response.Headers.ETag?.ToString();
            if (string.IsNullOrEmpty(etag))
            {
                var xml = await MultiStatusAsync("PROPFIND", url, PropfindBody(D + "getetag"), "0");
                etag = xml.Descendants(D + "getetag").FirstOrDefault()?.Value ?? "";
            }
            return etag;
        }

        private RemoteTask ToRemote(string href, CachedItem item, Dictionary<string, string> uidToHref)
        {
            string? parentUid = item.Doc.ParentUid;
            return new RemoteTask
            {
                Id = href,
                Title = item.Doc.Summary,
                Notes = item.Doc.Description,
                Completed = item.Doc.IsCompleted,
                Parent = parentUid != null && uidToHref.TryGetValue(parentUid, out var parentHref) ? parentHref : null,
                Updated = item.Etag
            };
        }

        private async Task<XDocument> MultiStatusAsync(string method, Uri url, string body, string depth)
        {
            var (status, response, _) = await SendAsync(_http, method, url, body, depth);
            if (status == HttpStatusCode.NotFound || status == HttpStatusCode.Gone)
                throw new RemoteNotFoundException("The list no longer exists.");
            EnsureSuccess(status, "load your lists");
            return (response != null ? ParseXml(response) : null) ?? throw new InvalidOperationException("The server sent an unreadable response.");
        }

        private static IEnumerable<XElement> OkProps(XElement response) =>
            response.Elements(D + "propstat")
                    .Where(ps => (ps.Element(D + "status")?.Value ?? "").Contains(" 200"))
                    .Elements(D + "prop");

        private static string PropfindBody(params XName[] props) =>
            new XDocument(new XElement(D + "propfind",
                new XAttribute(XNamespace.Xmlns + "D", D), new XAttribute(XNamespace.Xmlns + "C", C),
                new XElement(D + "prop", props.Select(p => new XElement(p))))).ToString();

        private static XDocument? ParseXml(string body)
        {
            try { return XDocument.Parse(body); }
            catch (System.Xml.XmlException) { return null; }
        }

        private static async Task<(HttpStatusCode Status, string? Body, Uri FinalUrl)> SendAsync(
            HttpClient http, string method, Uri url, string? body, string? depth = null)
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), url);
            if (body != null) request.Content = new StringContent(body, Encoding.UTF8, "application/xml");
            if (depth != null) request.Headers.TryAddWithoutValidation("Depth", depth);

            using var response = await SendWithRedirectsAsync(http, request);
            string? text = response.Content == null ? null : await response.Content.ReadAsStringAsync();
            return (response.StatusCode, text, response.RequestMessage?.RequestUri ?? url);
        }

        private static async Task<HttpResponseMessage> SendWithRedirectsAsync(HttpClient http, HttpRequestMessage request)
        {
            string? content = request.Content == null ? null : await request.Content.ReadAsStringAsync();
            string? contentType = request.Content?.Headers.ContentType?.ToString();
            var current = request;

            for (int hop = 0; ; hop++)
            {
                var response = await http.SendAsync(current);
                int code = (int)response.StatusCode;
                if (code < 300 || code >= 400 || code == 304 || response.Headers.Location == null || hop >= 5) return response;

                // Repeat the same request (method, headers, body) at the new address
                var next = new HttpRequestMessage(current.Method, new Uri(current.RequestUri!, response.Headers.Location));
                foreach (var header in request.Headers) next.Headers.TryAddWithoutValidation(header.Key, header.Value);
                if (content != null)
                {
                    next.Content = new StringContent(content, Encoding.UTF8);
                    if (contentType != null) next.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
                }
                response.Dispose();
                if (current != request) current.Dispose();
                current = next;
            }
        }

        private static bool IsSuccess(HttpStatusCode status) => (int)status >= 200 && (int)status < 300;

        private static void EnsureNotAuthFailure(HttpStatusCode status)
        {
            if (status == HttpStatusCode.Unauthorized)
                throw new RemoteAuthException("The server no longer accepts the saved username and password.");
        }

        private static void EnsureSuccess(HttpStatusCode status, string action)
        {
            EnsureNotAuthFailure(status);
            if (!IsSuccess(status))
                throw new HttpRequestException($"Couldn't {action} (server answered {(int)status} {status}).", null, status);
        }
    }
}
