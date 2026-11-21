using System.Data.OleDb;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace EzEizo.Core;

// The website's complete form is stored as a JSON document in Access. The existing
// relational tables and user IDs stay intact, so an update never replaces the ACCDB.
public sealed class OfflineStore
{
    private static readonly object SerialGate = new();
    private static string ConnectionString => $"Provider=Microsoft.ACE.OLEDB.16.0;Data Source={Database.FilePath};Persist Security Info=False;";
    private static OleDbConnection Open() { var c = new OleDbConnection(ConnectionString); c.Open(); return c; }
    private static OleDbCommand Cmd(OleDbConnection c, OleDbTransaction? tx, string sql, params object?[] values)
    {
        var command = new OleDbCommand(sql, c, tx);
        foreach (var value in values)
        {
            var type = value switch
            {
                DateTime => OleDbType.Date,
                bool => OleDbType.Boolean,
                int => OleDbType.Integer,
                string text when text.Length > 255 => OleDbType.LongVarWChar,
                _ => OleDbType.VarWChar
            };
            command.Parameters.Add("@p",type).Value = value ?? DBNull.Value;
        }
        return command;
    }
    public void Initialize()
    {
        using var c = Open();
        var names = c.GetSchema("Tables").Rows.Cast<System.Data.DataRow>().Select(r => Convert.ToString(r["TABLE_NAME"])).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!names.Contains("OfflineDocuments"))
        {
            using var cmd = Cmd(c, null, "CREATE TABLE [OfflineDocuments] ([DocID] TEXT(36) PRIMARY KEY, [CollectionName] TEXT(30), [SerialValue] TEXT(24), [OwnerID] TEXT(36), [Payload] MEMO, [UpdatedAt] DATETIME)");
            cmd.ExecuteNonQuery();
            using var index = Cmd(c, null, "CREATE INDEX [IX_OfflineCollection] ON [OfflineDocuments] ([CollectionName])");
            index.ExecuteNonQuery();
        }
        if (!names.Contains("OfflineDrafts"))
        {
            using var cmd = Cmd(c, null, "CREATE TABLE [OfflineDrafts] ([DraftID] TEXT(36) PRIMARY KEY, [UserID] TEXT(36), [DraftKey] TEXT(120), [Payload] MEMO, [UpdatedAt] DATETIME)");
            cmd.ExecuteNonQuery();
            using var index = Cmd(c, null, "CREATE UNIQUE INDEX [UX_OfflineDraft] ON [OfflineDrafts] ([UserID],[DraftKey])");
            index.ExecuteNonQuery();
        }
        if (!names.Contains("OfflineRevisions"))
        {
            using var revision = Cmd(c, null, "CREATE TABLE [OfflineRevisions] ([CollectionName] TEXT(30) PRIMARY KEY, [RevisionValue] LONG)");
            revision.ExecuteNonQuery();
        }
        ConsolidateLegacyIssues(c);
    }
    public int Version(string collection)
    {
        if (collection is not ("issues" or "keyLoads" or "aircraftConstants")) throw new ArgumentException("אוסף לא תקין.");
        using var c = Open();
        using var cmd = Cmd(c, null, "SELECT [RevisionValue] FROM [OfflineRevisions] WHERE [CollectionName]=?", collection);
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
    }
    private static void Touch(OleDbConnection c, OleDbTransaction tx, string collection)
    {
        using var update = Cmd(c, tx, "UPDATE [OfflineRevisions] SET [RevisionValue]=[RevisionValue]+1 WHERE [CollectionName]=?", collection);
        if (update.ExecuteNonQuery() != 0) return;
        using var insert = Cmd(c, tx, "INSERT INTO [OfflineRevisions] ([CollectionName],[RevisionValue]) VALUES (?,?)", collection, 1);
        insert.ExecuteNonQuery();
    }
    private static void ConsolidateLegacyIssues(OleDbConnection c)
    {
        using var duplicates = Cmd(c, null, "SELECT [SerialValue] FROM [OfflineDocuments] WHERE [CollectionName]='issues' GROUP BY [SerialValue] HAVING COUNT(*)>1");
        var serials = new List<string>();
        using (var reader = duplicates.ExecuteReader())
            while (reader is not null && reader.Read()) serials.Add(reader.GetString(0));
        foreach (var serial in serials)
        {
            using var tx = c.BeginTransaction();
            try
            {
                var entries = new List<(string id, JsonNode data)>();
                using (var cmd = Cmd(c, tx, "SELECT [DocID],[Payload] FROM [OfflineDocuments] WHERE [CollectionName]='issues' AND [SerialValue]=? ORDER BY [UpdatedAt]", serial))
                using (var reader = cmd.ExecuteReader())
                    while (reader is not null && reader.Read()) entries.Add((reader.GetString(0), JsonNode.Parse(reader.GetString(1))!));
                if (entries.Count < 2 || entries.Any(x => Value(x.data, "status") != "open" || x.data["assigneeUids"] is not null)) { tx.Commit(); continue; }
                var comparable = entries.Select(x => {
                    var sheet = x.data["sheet"]!.DeepClone().AsObject();
                    sheet.Remove("id"); sheet.Remove("technicians");
                    return sheet.ToJsonString();
                }).Distinct().Count() == 1;
                if (!comparable || entries.Select(x => Value(x.data,"assigneeUid")).Distinct().Count() != entries.Count) { tx.Commit(); continue; }
                var busy = false;
                foreach (var entry in entries)
                {
                    using var lockQuery = Cmd(c, tx, "SELECT [ExpiresAt] FROM [EditLocks] WHERE [FormID]=?", entry.id);
                    if (lockQuery.ExecuteScalar() is DateTime expiry && expiry > DateTime.Now) { busy = true; break; }
                    using var draftQuery = Cmd(c, tx, "SELECT COUNT(*) FROM [OfflineDrafts] WHERE [DraftKey]=?", "fault-sheet-v2:" + entry.id);
                    if (Convert.ToInt32(draftQuery.ExecuteScalar()) > 0) { busy = true; break; }
                }
                if (busy) { tx.Commit(); continue; }
                var ids = entries.Select(x => Value(x.data, "assigneeUid")).ToArray();
                var names = entries.Select(x => Value(x.data, "assigneeName")).ToArray();
                var merged = entries[0].data;
                merged["assigneeUids"] = Names(ids); merged["assigneeNames"] = Names(names);
                merged["sheet"]!["technicians"] = string.Join(" · ", names);
                Replace(c, tx, "issues", entries[0].id, merged);
                foreach (var (id, _) in entries.Skip(1))
                {
                    using var archive = Cmd(c, tx, "UPDATE [OfflineDocuments] SET [CollectionName]='legacyIssues' WHERE [DocID]=?", id);
                    archive.ExecuteNonQuery();
                }
                tx.Commit();
            }
            catch { tx.Rollback(); throw; }
        }
    }
    private static void CheckDraftKey(string key)
    {
        if (key.Length is < 1 or > 120 || !(key.StartsWith("ezeizo-") || key.StartsWith("fault-sheet-")))
            throw new ArgumentException("מפתח טיוטה אינו תקין.");
    }
    public Dictionary<string, string> Drafts(Account actor)
    {
        using var c = Open();
        using var cmd = Cmd(c, null, "SELECT [DraftKey],[Payload] FROM [OfflineDrafts] WHERE [UserID]=?", actor.Id);
        using var reader = cmd.ExecuteReader();
        var result = new Dictionary<string, string>();
        while (reader is not null && reader.Read()) result.Add(reader.GetString(0), reader.GetString(1));
        reader?.Close();
        using var shared = Cmd(c, null, "SELECT [DraftKey],[Payload] FROM [OfflineDrafts] WHERE [UserID]='shared' AND [DraftKey] LIKE 'fault-sheet-v2:%'");
        var sharedEntries = new List<(string key, string value)>();
        using (var sharedReader = shared.ExecuteReader())
            while (sharedReader is not null && sharedReader.Read())
                sharedEntries.Add((sharedReader.GetString(0), sharedReader.GetString(1)));
        foreach (var (key, value) in sharedEntries)
        {
            var issue = Find(c, null, "issues", key["fault-sheet-v2:".Length..]);
            if (issue is not null && (actor.Role == "manager" || IssueMember(issue, actor.Id)))
                result[key] = value;
        }
        return result;
    }
    public string? ReadDraft(string key, Account actor)
    {
        CheckDraftKey(key);
        using var c = Open();
        var owner = DraftOwner(c, key, actor);
        using var cmd = Cmd(c, null, "SELECT [Payload] FROM [OfflineDrafts] WHERE [UserID]=? AND [DraftKey]=?", owner, key);
        return cmd.ExecuteScalar() as string;
    }
    private static string DraftOwner(OleDbConnection c, string key, Account actor)
    {
        if (!key.StartsWith("fault-sheet-v2:")) return actor.Id;
        var id = key["fault-sheet-v2:".Length..];
        var issue = Find(c, null, "issues", id) ?? throw new InvalidOperationException("הטופס אינו קיים.");
        if (!IssueMember(issue, actor.Id)) throw new UnauthorizedAccessException("אינך משויך לטופס.");
        using var lockQuery = Cmd(c, null, "SELECT [OwnerUserID],[ExpiresAt] FROM [EditLocks] WHERE [FormID]=?", id);
        using var lockReader = lockQuery.ExecuteReader();
        if (lockReader is not null && lockReader.Read() && lockReader.GetDateTime(1) > DateTime.Now && lockReader.GetString(0) != actor.Id)
            throw new InvalidOperationException("הטופס נמצא בעריכת טכנאי אחר.");
        return "shared";
    }
    public void SaveDraft(string key, string value, Account actor)
    {
        CheckDraftKey(key);
        if (value.Length > 5_000_000) throw new ArgumentException("הטיוטה גדולה מדי.");
        using var c = Open(); var owner = DraftOwner(c,key,actor); using var tx = c.BeginTransaction();
        try
        {
            using var lookup = Cmd(c, tx, "SELECT [DraftID] FROM [OfflineDrafts] WHERE [UserID]=? AND [DraftKey]=?", owner, key);
            var id = lookup.ExecuteScalar() as string;
            if (id is null)
            {
                using var insert = Cmd(c, tx, "INSERT INTO [OfflineDrafts] ([DraftID],[UserID],[DraftKey],[Payload],[UpdatedAt]) VALUES (?,?,?,?,?)", Guid.NewGuid().ToString(), owner, key, value, DateTime.Now);
                insert.ExecuteNonQuery();
            }
            else
            {
                using var update = Cmd(c, tx, "UPDATE [OfflineDrafts] SET [Payload]=?,[UpdatedAt]=? WHERE [DraftID]=?", value, DateTime.Now, id);
                update.ExecuteNonQuery();
            }
            tx.Commit();
        }
        catch { tx.Rollback(); throw; }
    }
    public void RemoveDraft(string key, Account actor)
    {
        CheckDraftKey(key);
        using var c = Open();
        var owner = DraftOwner(c,key,actor);
        using var cmd = Cmd(c, null, "DELETE FROM [OfflineDrafts] WHERE [UserID]=? AND [DraftKey]=?", owner, key);
        cmd.ExecuteNonQuery();
    }
    private static string Value(JsonNode? node, string key) => node?[key]?.ToString() ?? "";
    private static bool IssueMember(JsonNode data, string userId) =>
        (data["assigneeUids"] as JsonArray)?.Any(x => x?.ToString() == userId) ?? (Value(data, "assigneeUid") == userId);
    private static string Now() => DateTimeOffset.Now.ToString("o");
    private static JsonNode Parse(JsonElement value) => JsonNode.Parse(value.GetRawText()) ?? new JsonObject();
    private static void Put(OleDbConnection c, OleDbTransaction tx, string collection, string id, string serial, string owner, JsonNode data)
    {
        using var cmd = Cmd(c, tx, "INSERT INTO [OfflineDocuments] ([DocID],[CollectionName],[SerialValue],[OwnerID],[Payload],[UpdatedAt]) VALUES (?,?,?,?,?,?)", id, collection, serial, owner, data.ToJsonString(), DateTime.Now);
        cmd.ExecuteNonQuery();
        Touch(c, tx, collection);
    }
    private static JsonNode? Find(OleDbConnection c, OleDbTransaction? tx, string collection, string id)
    {
        using var cmd = Cmd(c, tx, "SELECT [Payload] FROM [OfflineDocuments] WHERE [CollectionName]=? AND [DocID]=?", collection, id);
        var raw = cmd.ExecuteScalar() as string;
        return raw is null ? null : JsonNode.Parse(raw);
    }
    private static void Replace(OleDbConnection c, OleDbTransaction tx, string collection, string id, JsonNode data)
    {
        using var cmd = Cmd(c, tx, "UPDATE [OfflineDocuments] SET [Payload]=?,[UpdatedAt]=? WHERE [CollectionName]=? AND [DocID]=?", data.ToJsonString(), DateTime.Now, collection, id);
        if (cmd.ExecuteNonQuery() != 1) throw new InvalidOperationException("הטופס לא נמצא.");
        Touch(c, tx, collection);
    }
    private static int Next(OleDbConnection c, OleDbTransaction tx, string collection)
    {
        using var cmd = Cmd(c, tx, "SELECT [SerialValue] FROM [OfflineDocuments] WHERE [CollectionName]=?", collection);
        using var r = cmd.ExecuteReader();
        var max = 0;
        while (r is not null && r.Read())
        {
            var value = r.IsDBNull(0) ? "" : r.GetString(0).Replace("K-", "");
            if (int.TryParse(value, out var number)) max = Math.Max(max, number);
        }
        r?.Close();
        if (collection == "issues")
        {
            using var legacy=Cmd(c,tx,"SELECT [SerialNumber] FROM [Forms] WHERE [FormKind]='fault'");
            using var old=legacy.ExecuteReader();
            while(old is not null && old.Read())
                if(int.TryParse(old.GetString(0),out var number)) max=Math.Max(max,number);
        }
        return max + 1;
    }
    public List<JsonNode> List(string collection, Account actor)
    {
        if (collection is not ("issues" or "keyLoads" or "aircraftConstants")) throw new ArgumentException("אוסף לא תקין.");
        if (collection == "aircraftConstants" && actor.Role != "manager") throw new UnauthorizedAccessException();
        using var c = Open();
        using var cmd = Cmd(c, null, "SELECT [Payload] FROM [OfflineDocuments] WHERE [CollectionName]=? ORDER BY [UpdatedAt] DESC", collection);
        using var r = cmd.ExecuteReader();
        var result = new List<JsonNode>();
        while (r is not null && r.Read())
        {
            var data = JsonNode.Parse(r.GetString(0)); if (data is null) continue;
            if (actor.Role != "manager" && (collection == "issues" ? !IssueMember(data, actor.Id) :
                !(data["teamUids"] as JsonArray ?? new JsonArray()).Any(x => x?.ToString() == actor.Id))) continue;
            result.Add(data);
        }
        return result;
    }
    public JsonNode? Get(string collection, string id, Account actor)
    {
        using var c = Open();
        var data = Find(c, null, collection, id);
        if (data is null) return null;
        if (actor.Role != "manager" && (collection == "issues" ? !IssueMember(data, actor.Id) :
            !(data["teamUids"] as JsonArray ?? new JsonArray()).Any(x => x?.ToString() == actor.Id))) throw new UnauthorizedAccessException("אין גישה לטופס.");
        return data;
    }
    public string CreateIssues(JsonElement sheetValue, JsonElement assigneesValue, Account actor)
    {
        if (actor.Role != "manager") throw new UnauthorizedAccessException();
        var assignees = assigneesValue.EnumerateArray().Select(Parse).ToList();
        if (assignees.Count < 1 || assignees.Select(x => Value(x,"uid")).Distinct().Count() != assignees.Count) throw new ArgumentException("יש לבחור טכנאים שונים.");
        var active = new Database().Technicians().Select(x=>x.Id).ToHashSet();
        if (assignees.Any(x=>!active.Contains(Value(x,"uid")))) throw new ArgumentException("יש לבחור טכנאים פעילים.");
        var sheet = Parse(sheetValue);
        if (string.IsNullOrWhiteSpace(Value(sheet,"fault"))) throw new ArgumentException("תיאור התקלה חסר.");
        lock (SerialGate)
        {
            using var c = Open(); using var tx = c.BeginTransaction();
            try
            {
                var next = Next(c, tx, "issues");
                var serial = next.ToString("D10");
                var id = Guid.NewGuid().ToString();
                var copy = sheet.DeepClone(); copy["id"] = id; copy["serial"] = serial;
                copy["technicians"] = string.Join(" · ", assignees.Select(x => Value(x,"name")));
                var data = new JsonObject { ["id"] = id, ["sheet"] = copy, ["status"] = "open",
                    ["assigneeUid"] = Value(assignees[0],"uid"), ["assigneeName"] = Value(assignees[0],"name"),
                    ["assigneeUids"] = new JsonArray(assignees.Select(x => (JsonNode?)JsonValue.Create(Value(x,"uid"))).ToArray()),
                    ["assigneeNames"] = new JsonArray(assignees.Select(x => (JsonNode?)JsonValue.Create(Value(x,"name"))).ToArray()),
                    ["createdAt"] = Now() };
                Put(c, tx, "issues", id, serial, Value(assignees[0],"uid"), data);
                tx.Commit(); return serial;
            }
            catch { tx.Rollback(); throw; }
        }
    }
    public JsonNode CreateKey(JsonElement value, Account actor)
    {
        if (actor.Role != "manager") throw new UnauthorizedAccessException();
        var load = Parse(value);
        var team=(load["teamUids"] as JsonArray)?.Select(x=>x?.ToString() ?? "").ToArray() ?? [];
        var active = new Database().Technicians().Select(x=>x.Id).ToHashSet();
        if (team.Length < 2 || team.Distinct().Count()!=team.Length || team.Any(x=>!active.Contains(x)))
            throw new ArgumentException("יש לבחור שני טכנאים פעילים ושונים לפחות.");
        lock (SerialGate)
        {
            using var c = Open(); using var tx = c.BeginTransaction();
            try
            {
                var id = Guid.NewGuid().ToString(); var serial = "K-" + Next(c,tx,"keyLoads").ToString("D9");
                load["id"] = id; load["serial"] = serial; load["status"] = "open"; load["createdAt"] = Now();
                Put(c,tx,"keyLoads",id,serial,"",load);
                tx.Commit(); return new JsonObject { ["id"] = id, ["serial"] = serial };
            }
            catch { tx.Rollback(); throw; }
        }
    }
    private static void RequireLock(OleDbConnection c, OleDbTransaction tx, string id, string session, Account actor)
    {
        using var cmd=Cmd(c,tx,"SELECT [OwnerUserID],[SessionID],[ExpiresAt] FROM [EditLocks] WHERE [FormID]=?",id);
        using var r=cmd.ExecuteReader();
        if(r is null||!r.Read()||r.GetString(0)!=actor.Id||r.GetString(1)!=session||r.GetDateTime(2)<=DateTime.Now)
            throw new InvalidOperationException("נעילת העריכה אבדה. פתח את הטופס מחדש.");
    }
    private static void RequireNoActiveLock(OleDbConnection c, OleDbTransaction tx, string id)
    {
        using var cmd = Cmd(c, tx, "SELECT [ExpiresAt] FROM [EditLocks] WHERE [FormID]=?", id);
        var value = cmd.ExecuteScalar();
        if (value is DateTime expiry && expiry > DateTime.Now)
            throw new InvalidOperationException("הטופס נמצא כרגע בעריכת טכנאי. נסה שוב לאחר שיצא ממנו.");
    }
    private static JsonArray Names(IEnumerable<string> values) => new(values.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray());
    public JsonNode Update(string collection, string id, string operation, JsonElement payload, Account actor)
    {
        using var c = Open(); using var tx = c.BeginTransaction();
        try
        {
            var data = Find(c,tx,collection,id) ?? throw new InvalidOperationException("הטופס אינו קיים.");
            bool manager = actor.Role == "manager";
            bool member = collection == "issues" ? IssueMember(data, actor.Id) :
                (data["teamUids"] as JsonArray ?? new JsonArray()).Any(x=>x?.ToString()==actor.Id);
            if (!manager && !member) throw new UnauthorizedAccessException("אינך משויך לטופס.");
            switch (operation)
            {
                case "submit":
                    RequireLock(c,tx,id,payload.GetProperty("session").GetString()!,actor);
                    if (manager || Value(data,"status") != "open") throw new InvalidOperationException("הטופס אינו פתוח.");
                    data["status"] = "pendingReview"; data["closed"] = new JsonObject { ["record"] = Parse(payload.GetProperty("record")), ["state"] = Parse(payload.GetProperty("state")) };
                    break;
                case "approve":
                    if (!manager || Value(data,"status") != "pendingReview") throw new InvalidOperationException("הטופס אינו ממתין לסגירה.");
                    if (!payload.GetProperty("signature").GetString()!.StartsWith("data:image/png;base64,")) throw new ArgumentException("חתימת בודק הדף חסרה.");
                    data["status"] = "closed"; data["reviewedAt"] = Now(); data["reviewedBy"] = actor.Id; data["reviewerSignature"] = payload.GetProperty("signature").GetString();
                    break;
                case "reopen":
                    if (!manager || Value(data,"status") is not ("closed" or "pendingReview")) throw new InvalidOperationException("הטופס אינו סגור.");
                    var events = data["reopenEvents"] as JsonArray ?? new JsonArray();
                    events.Add(new JsonObject { ["at"] = Now(), ["by"] = actor.Id, ["priorClosedAt"] = Value(data,"reviewedAt") });
                    data["reopenEvents"] = events.DeepClone(); data["status"] = "open"; data["reopenedAt"] = Now(); data["reopenedBy"] = actor.Id;
                    break;
                case "note":
                    if (!manager || Value(data,"status") != "open") throw new InvalidOperationException("ניתן להוסיף הערות לטופס פתוח בלבד.");
                    var note = payload.GetProperty("note").GetString()?.Trim();
                    if (string.IsNullOrEmpty(note) || note.Length > 500) throw new ArgumentException("יש להזין הערה באורך עד 500 תווים.");
                    if (collection == "issues") { var sheet = data["sheet"]!; sheet["managerNotes"] = string.Join("\n",new[]{Value(sheet,"managerNotes"),note}.Where(x=>x.Length>0)); }
                    else data["managerNotes"] = string.Join("\n",new[]{Value(data,"managerNotes"),note}.Where(x=>x.Length>0));
                    break;
                case "draft":
                    RequireLock(c,tx,id,payload.GetProperty("session").GetString()!,actor);
                    if (manager || Value(data,"status") != "open" || collection != "keyLoads") throw new InvalidOperationException("ההטענה אינה פתוחה לעריכה.");
                    data["workDraft"] = Parse(payload.GetProperty("work")); data["draftBy"] = actor.Id;
                    break;
                case "complete":
                    RequireLock(c,tx,id,payload.GetProperty("session").GetString()!,actor);
                    if (manager || Value(data,"status") != "open" || collection != "keyLoads") throw new InvalidOperationException("ההטענה אינה פתוחה לעריכה.");
                    data["work"] = Parse(payload.GetProperty("work")); data["status"] = "closed"; data["completedAt"] = Now(); data["completedBy"] = actor.Id; (data as JsonObject)?.Remove("workDraft");
                    break;
                case "assign":
                    if (!manager || collection != "issues" || Value(data,"status") != "open") throw new InvalidOperationException("התקלה אינה פתוחה.");
                    RequireNoActiveLock(c,tx,id);
                    var assignedUid = payload.GetProperty("uid").GetString()!;
                    var assignedAccount = new Database().Technicians().FirstOrDefault(x => x.Id == assignedUid)
                        ?? throw new ArgumentException("בחר טכנאי פעיל.");
                    var team = (data["assigneeUids"] as JsonArray)?.Select(x => x?.ToString() ?? "").ToList() ?? [Value(data,"assigneeUid")];
                    if (team.Skip(1).Contains(assignedUid)) throw new ArgumentException("הטכנאי כבר משויך לתקלה.");
                    team[0] = assignedUid;
                    var names = new Database().Technicians().ToDictionary(x => x.Id, x => x.Name);
                    data["assigneeUid"] = assignedUid; data["assigneeName"] = assignedAccount.Name;
                    data["assigneeUids"] = Names(team); data["assigneeNames"] = Names(team.Select(uid => names.GetValueOrDefault(uid, "טכנאי שהוסר")));
                    data["sheet"]!["technicians"] = string.Join(" · ", team.Select(uid => names.GetValueOrDefault(uid, "טכנאי שהוסר")));
                    break;
                case "editDefinition":
                    if (!manager || Value(data,"status") != "open") throw new InvalidOperationException("ניתן לערוך פרטי טופס פתוח בלבד.");
                    RequireNoActiveLock(c,tx,id);
                    var changes = Parse(payload.GetProperty("changes"));
                    var allowed = collection == "issues"
                        ? new HashSet<string> { "squadron", "shelter", "workshop", "aircraft", "systemCode", "systemName", "ewSystem", "fault", "managerNotes" }
                        : collection == "keyLoads"
                            ? new HashSet<string> { "date", "time", "squadron", "shelter", "rows", "aircraftIds", "period" }
                            : throw new ArgumentException("סוג טופס לא תקין.");
                    if (changes is not JsonObject changeObject || changeObject.Any(entry => !allowed.Contains(entry.Key)))
                        throw new ArgumentException("פרטי עריכה לא תקינים.");
                    if (collection == "issues")
                    {
                        var originalSheet = data["sheet"] ?? throw new InvalidOperationException("פרטי התקלה חסרים.");
                        foreach (var item in changeObject) originalSheet[item.Key] = item.Value?.DeepClone();
                        if (string.IsNullOrWhiteSpace(Value(originalSheet,"fault")) || string.IsNullOrWhiteSpace(Value(originalSheet,"squadron")) || string.IsNullOrWhiteSpace(Value(originalSheet,"aircraft")))
                            throw new ArgumentException("יש למלא תיאור תקלה, טייסת ומספר מטוס.");
                        originalSheet["location"] = string.Join("/", new[] { Value(originalSheet,"shelter") == "מוסך" ? Value(originalSheet,"workshop") : Value(originalSheet,"shelter"), Value(originalSheet,"squadron") }.Where(x => x.Length > 0));
                    }
                    else
                    {
                        if (data["workDraft"] is not null && (changes["rows"]?.ToJsonString() != data["rows"]?.ToJsonString() || changes["aircraftIds"]?.ToJsonString() != data["aircraftIds"]?.ToJsonString()))
                            throw new InvalidOperationException("כבר קיימת טיוטת טכנאי. אין לשנות את רשימת המטוסים; ניתן לשנות פרטים אחרים.");
                        foreach (var item in changeObject) data[item.Key] = item.Value?.DeepClone();
                        if (string.IsNullOrWhiteSpace(Value(data,"date")) || string.IsNullOrWhiteSpace(Value(data,"squadron")) || data["rows"] is not JsonArray rows || rows.Count == 0)
                            throw new ArgumentException("יש למלא תאריך, טייסת ולפחות מטוס אחד.");
                    }
                    if (payload.TryGetProperty("teamUids", out var members))
                    {
                        var requested = members.EnumerateArray().Select(x => x.GetString() ?? "").ToArray();
                        var minimum = collection == "issues" ? 1 : 2;
                        var activeNames = new Database().Technicians().ToDictionary(x => x.Id, x => x.Name);
                        if (requested.Length < minimum || requested.Distinct().Count() != requested.Length || requested.Any(uid => !activeNames.ContainsKey(uid)))
                            throw new ArgumentException("בחר טכנאים פעילים ושונים.");
                        if (collection == "issues")
                        {
                            data["assigneeUid"] = requested[0]; data["assigneeName"] = activeNames[requested[0]];
                            data["assigneeUids"] = Names(requested); data["assigneeNames"] = Names(requested.Select(uid => activeNames[uid]));
                            data["sheet"]!["technicians"] = string.Join(" · ", requested.Select(uid => activeNames[uid]));
                        }
                        else { data["teamUids"] = Names(requested); data["teamNames"] = Names(requested.Select(uid => activeNames[uid])); }
                    }
                    var edits = data["managerEdits"] as JsonArray ?? new JsonArray();
                    edits.Add(new JsonObject { ["at"] = Now(), ["by"] = actor.Id });
                    data["managerEdits"] = edits.DeepClone();
                    break;
                case "serial":
                    if (!manager || collection != "keyLoads") throw new UnauthorizedAccessException();
                    if (Value(data,"serial").Length==0) data["serial"] = "K-" + Next(c,tx,"keyLoads").ToString("D9");
                    break;
                default: throw new ArgumentException("פעולה לא מוכרת.");
            }
            Replace(c,tx,collection,id,data);tx.Commit();return data;
        }
        catch { tx.Rollback(); throw; }
    }
    public JsonNode Lock(string collection, string id, string session, Account actor)
    {
        using var c=Open();using var tx=c.BeginTransaction();
        try
        {
            var data=Find(c,tx,collection,id) ?? throw new InvalidOperationException("הטופס אינו קיים.");
            if (Value(data,"status")!="open") throw new InvalidOperationException("הטופס אינו פתוח.");
            var assigned=collection=="issues"?IssueMember(data,actor.Id):(data["teamUids"] as JsonArray ?? new JsonArray()).Any(x=>x?.ToString()==actor.Id);
            if (!assigned) throw new UnauthorizedAccessException("אינך משויך לטופס.");
            using var lookup=Cmd(c,tx,"SELECT [OwnerUserID],[SessionID],[ExpiresAt] FROM [EditLocks] WHERE [FormID]=?",id);
            using var r=lookup.ExecuteReader(); string owner="",existing="";DateTime expires=DateTime.MinValue;
            if(r is not null && r.Read()){owner=r.GetString(0);existing=r.GetString(1);expires=r.GetDateTime(2);} r?.Close();
            if(expires>DateTime.Now && existing!=session)
            {
                using var name=Cmd(c,tx,"SELECT [DisplayName] FROM [Users] WHERE [UserID]=?",owner);
                var display=Convert.ToString(name.ExecuteScalar()) ?? "טכנאי";
                tx.Commit();return new JsonObject{["held"]=false,["ownerName"]=display};
            }
            using(var del=Cmd(c,tx,"DELETE FROM [EditLocks] WHERE [FormID]=?",id))del.ExecuteNonQuery();
            using(var insert=Cmd(c,tx,"INSERT INTO [EditLocks] ([LockID],[FormID],[OwnerUserID],[SessionID],[AcquiredAt],[ExpiresAt]) VALUES (?,?,?,?,?,?)",Guid.NewGuid().ToString(),id,actor.Id,session,DateTime.Now,DateTime.Now.AddSeconds(90)))insert.ExecuteNonQuery();
            tx.Commit();return new JsonObject{["held"]=true};
        }catch{tx.Rollback();throw;}
    }
    public JsonNode? ReadLock(string id)
    {
        using var c=Open();using var cmd=Cmd(c,null,"SELECT [OwnerUserID],[SessionID],[ExpiresAt] FROM [EditLocks] WHERE [FormID]=?",id);using var r=cmd.ExecuteReader();
        if(r is null||!r.Read()||r.GetDateTime(2)<DateTime.Now)return null;
        var owner=r.GetString(0);var session=r.GetString(1);var expiry=r.GetDateTime(2);r.Close();
        using var name=Cmd(c,null,"SELECT [DisplayName] FROM [Users] WHERE [UserID]=?",owner);
        return new JsonObject{["ownerName"]=Convert.ToString(name.ExecuteScalar()) ?? "טכנאי",["session"]=session,["expiresAt"]=new DateTimeOffset(expiry).ToUnixTimeMilliseconds()};
    }
    public void ReleaseLock(string id,string session)
    { using var c=Open();using var cmd=Cmd(c,null,"DELETE FROM [EditLocks] WHERE [FormID]=? AND [SessionID]=?",id,session);cmd.ExecuteNonQuery(); }
    public List<JsonNode> Constants(Account actor)=>List("aircraftConstants",actor);
    public void SaveConstants(JsonElement p,Account actor)
    {
        if(actor.Role!="manager")throw new UnauthorizedAccessException();
        var squadron=p.GetProperty("squadron").GetString()!;var aircraft=p.GetProperty("aircraft").GetString()!;var id=squadron+"_"+aircraft;
        using var c=Open();using var tx=c.BeginTransaction();try{
            var data=new JsonObject{["id"]=id,["squadron"]=squadron,["aircraft"]=aircraft,["constants"]=Parse(p.GetProperty("constants")),["updatedAt"]=Now(),["updatedBy"]=actor.Id};
            if(Find(c,tx,"aircraftConstants",id) is null)Put(c,tx,"aircraftConstants",id,"","",data);else Replace(c,tx,"aircraftConstants",id,data);
            tx.Commit();}catch{tx.Rollback();throw;}
    }
}
