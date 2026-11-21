namespace EzEizo.Core;

public static class SharedDatabaseStartup
{
    public static void Initialize()
    {
        // Both EXEs can start together against one ACCDB. Serialize schema migration
        // and first-run administrator provisioning across processes, not just threads.
        using var gate = new Mutex(false, @"Local\EzEizo.Access.Initialization");
        var acquired = false;
        try
        {
            try { acquired = gate.WaitOne(TimeSpan.FromSeconds(45)); }
            catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) throw new TimeoutException("מסד הנתונים עסוק באתחול מתוכנה נוספת. נסה לפתוח שוב בעוד רגע.");
            var database = new Database();
            database.Initialize();
            database.EnsureDemoManager();
            var store = new OfflineStore();
            store.Initialize();
            _ = store.Version("issues");
            _ = store.Version("keyLoads");
        }
        finally { if (acquired) gate.ReleaseMutex(); }
    }
}
