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
        return result;
    }
    public void SaveDraft(string key, string value, Account actor)
    {
        CheckDraftKey(key);
        if (value.Length > 5_000_000) throw new ArgumentException("הטיוטה גדולה מדי.");
        using var c = Open(); using var tx = c.BeginTransaction();
        try
        {
            using var lookup = Cmd(c, tx, "SELECT [DraftID] FROM [OfflineDrafts] WHERE [UserID]=? AND [DraftKey]=?", actor.Id, key);
            var id = lookup.ExecuteScalar() as string;
            if (id is null)
            {
                using var insert = Cmd(c, tx, "INSERT INTO [OfflineDrafts] ([DraftID],[UserID],[DraftKey],[Payload],[UpdatedAt]) VALUES (?,?,?,?,?)", Guid.NewGuid().ToString(), actor.Id, key, value, DateTime.Now);
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
        using var cmd = Cmd(c, null, "DELETE FROM [OfflineDrafts] WHERE [UserID]=? AND [DraftKey]=?", actor.Id, key);
        cmd.ExecuteNonQuery();
    }
    private static string Value(JsonNode? node, string key) => node?[key]?.ToString() ?? "";
    private static string Now() => DateTimeOffset.Now.ToString("o");
    private static JsonNode Parse(JsonElement value) => JsonNode.Parse(value.GetRawText()) ?? new JsonObject();
    private static void Put(OleDbConnection c, OleDbTransaction tx, string collection, string id, string serial, string owner, JsonNode data)
    {
        using var cmd = Cmd(c, tx, "INSERT INTO [OfflineDocuments] ([DocID],[CollectionName],[SerialValue],[OwnerID],[Payload],[UpdatedAt]) VALUES (?,?,?,?,?,?)", id, collection, serial, owner, data.ToJsonString(), DateTime.Now);
        cmd.ExecuteNonQuery();
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
            if (actor.Role != "manager" && (collection == "issues" ? Value(data, "assigneeUid") != actor.Id :
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
        if (actor.Role != "manager" && (collection == "issues" ? Value(data, "assigneeUid") != actor.Id :
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
                foreach (var person in assignees)
                {
                    var id = Guid.NewGuid().ToString();
                    var copy = sheet.DeepClone(); copy["id"] = id; copy["serial"] = serial;
                    copy["technicians"] = string.Join(" · ", assignees.Select(x => Value(x,"name")));
                    var data = new JsonObject { ["id"] = id, ["sheet"] = copy, ["status"] = "open", ["assigneeUid"] = Value(person,"uid"), ["assigneeName"] = Value(person,"name"), ["createdAt"] = Now() };
                    Put(c, tx, "issues", id, serial, Value(person,"uid"), data);
                }
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
    public JsonNode Update(string collection, string id, string operation, JsonElement payload, Account actor)
    {
        using var c = Open(); using var tx = c.BeginTransaction();
        try
        {
            var data = Find(c,tx,collection,id) ?? throw new InvalidOperationException("הטופס אינו קיים.");
            bool manager = actor.Role == "manager";
            bool member = collection == "issues" ? Value(data,"assigneeUid") == actor.Id :
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
                    data["assigneeUid"] = payload.GetProperty("uid").GetString();data["assigneeName"] = payload.GetProperty("name").GetString();
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
            var assigned=collection=="issues"?Value(data,"assigneeUid")==actor.Id:(data["teamUids"] as JsonArray ?? new JsonArray()).Any(x=>x?.ToString()==actor.Id);
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
