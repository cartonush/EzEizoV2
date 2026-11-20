using System.Text.Json;
using System.Text.Json.Nodes;

namespace EzEizo.Core;

public static class OfflineSmokeTest
{
    public static void Run(Database db, Account manager, Account technician)
    {
        var store = new OfflineStore();
        store.Initialize();
        using var sheet = JsonDocument.Parse("""{"fault":"פירוק והרכבה אנטנת מכם","squadron":"105","aircraft":"603","actions":[],"constants":{}}""");
        using var assigned = JsonDocument.Parse($"""[{{"uid":"{technician.Id}","name":"{technician.Name}"}}]""");
        var serial = store.CreateIssues(sheet.RootElement, assigned.RootElement, manager);
        var issue = store.List("issues", technician).Single(x => x["sheet"]?["serial"]?.ToString() == serial);
        var id = issue["id"]!.ToString();
        var session = Guid.NewGuid().ToString();
        if (store.Lock("issues",id,session,technician)["held"]?.GetValue<bool>() != true)
            throw new InvalidOperationException("נעילת הטופס נכשלה.");
        if (store.ReadLock(id)?["session"]?.ToString() != session)
            throw new InvalidOperationException("נעילת הטופס אינה מוצגת.");
        using var submit = JsonDocument.Parse($"""{{"record":{{"serial":"{serial}"}},"state":{{"repair":"בוצע"}},"session":"{session}"}}""");
        store.Update("issues",id,"submit",submit.RootElement,technician);
        using var approve = JsonDocument.Parse("""{"signature":"data:image/png;base64,aGVsbG8="}""");
        store.Update("issues",id,"approve",approve.RootElement,manager);
        if (store.Get("issues",id,manager)?["status"]?.ToString() != "closed")
            throw new InvalidOperationException("סגירת טופס מלא נכשלה.");
        var second=db.CreateAccount("בדיקת טכנאי נוסף","9876543","mechanic");
        using var validKeyLoad = JsonDocument.Parse($"""{{"squadron":"105","teamUids":["{technician.Id}","{second.Id}"],"rows":[{{"aircraft":"603","blue":true}}]}}""");
        var key = store.CreateKey(validKeyLoad.RootElement,manager);
        if (!key["serial"]!.ToString().StartsWith("K-")) throw new InvalidOperationException("מספר הטענה אינו תקין.");
        var keyId=key["id"]!.ToString();var keySession=Guid.NewGuid().ToString();
        store.Lock("keyLoads",keyId,keySession,technician);
        using var draft=JsonDocument.Parse($"""{{"work":{{"tasks":[true]}},"session":"{keySession}"}}""");
        store.Update("keyLoads",keyId,"draft",draft.RootElement,technician);
        store.Update("keyLoads",keyId,"complete",draft.RootElement,technician);
        if (store.List("keyLoads",manager).Single(x=>x["id"]?.ToString()==keyId)["status"]?.ToString() != "closed")
            throw new InvalidOperationException("סגירת מפתחות נכשלה.");
        using var constants=JsonDocument.Parse("""{"squadron":"105","aircraft":"603","constants":{"HUD":{"K1":"0"}}}""");
        store.SaveConstants(constants.RootElement,manager);
        if (!store.Constants(manager).Any(x=>x["aircraft"]?.ToString()=="603"))
            throw new InvalidOperationException("שמירת קבועים נכשלה.");
    }
}
