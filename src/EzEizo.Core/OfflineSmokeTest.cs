using System.Text.Json;

namespace EzEizo.Core;

public static class OfflineSmokeTest
{
    private static JsonDocument Document(object value) => JsonDocument.Parse(JsonSerializer.Serialize(value));

    public static void Run(Database db, Account manager, Account technician)
    {
        var store = new OfflineStore();
        store.Initialize();
        using var sheet = Document(new { fault = "פירוק והרכבה אנטנת מכם", squadron = "105", aircraft = "603", actions = Array.Empty<string>(), constants = new { } });
        using var assigned = Document(new[] { new { uid = technician.Id, name = technician.Name } });
        var serial = store.CreateIssues(sheet.RootElement, assigned.RootElement, manager);
        var issue = store.List("issues", technician).Single(x => x["sheet"]?["serial"]?.ToString() == serial);
        var id = issue["id"]!.ToString();
        var session = Guid.NewGuid().ToString();
        if (store.Lock("issues", id, session, technician)["held"]?.GetValue<bool>() != true)
            throw new InvalidOperationException("נעילת הטופס נכשלה.");
        if (store.ReadLock(id)?["session"]?.ToString() != session)
            throw new InvalidOperationException("נעילת הטופס אינה מוצגת.");
        using var submit = Document(new { record = new { serial }, state = new { repair = "בוצע" }, session });
        store.Update("issues", id, "submit", submit.RootElement, technician);
        using var approve = Document(new { signature = "data:image/png;base64,aGVsbG8=" });
        store.Update("issues", id, "approve", approve.RootElement, manager);
        if (store.Get("issues", id, manager)?["status"]?.ToString() != "closed")
            throw new InvalidOperationException("סגירת טופס מלא נכשלה.");
        var second = db.CreateAccount("בדיקת טכנאי נוסף", "9876543", "mechanic", "246810");
        using var keyLoad = Document(new { squadron = "105", teamUids = new[] { technician.Id, second.Id }, rows = new[] { new { aircraft = "603", blue = true } } });
        var key = store.CreateKey(keyLoad.RootElement, manager);
        if (!key["serial"]!.ToString().StartsWith("K-")) throw new InvalidOperationException("מספר הטענה אינו תקין.");
        var keyId = key["id"]!.ToString(); var keySession = Guid.NewGuid().ToString();
        store.Lock("keyLoads", keyId, keySession, technician);
        using var draft = Document(new { work = new { tasks = new[] { true } }, session = keySession });
        store.Update("keyLoads", keyId, "draft", draft.RootElement, technician);
        store.Update("keyLoads", keyId, "complete", draft.RootElement, technician);
        if (store.List("keyLoads", manager).Single(x => x["id"]?.ToString() == keyId)["status"]?.ToString() != "closed")
            throw new InvalidOperationException("סגירת מפתחות נכשלה.");
        using var constants = Document(new { squadron = "105", aircraft = "603", constants = new { HUD = new { K1 = "0" } } });
        store.SaveConstants(constants.RootElement, manager);
        if (!store.Constants(manager).Any(x => x["aircraft"]?.ToString() == "603"))
            throw new InvalidOperationException("שמירת קבועים נכשלה.");
    }
}
