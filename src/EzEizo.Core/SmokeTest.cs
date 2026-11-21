namespace EzEizo.Core;

public static class SmokeTest
{
    public static void Run()
    {
        var db = new Database();
        db.Initialize();
        var manager = db.CreateAccount("בדיקת מנהל", "1234567", "manager", "654321");
        var mechanic = db.CreateAccount("בדיקת טכנאי", "7654321", "mechanic", "123456");
        if (db.SignIn(manager.PersonalNumber, AppRole.Manager)?.Id != manager.Id ||
            db.SignIn(mechanic.PersonalNumber, AppRole.Mechanic)?.Id != mechanic.Id ||
            db.Authenticate(manager.PersonalNumber, "654321", AppRole.Manager)?.Id != manager.Id ||
            db.Authenticate(mechanic.PersonalNumber, "123456", AppRole.Mechanic)?.Id != mechanic.Id ||
            db.Authenticate(mechanic.PersonalNumber, "654321", AppRole.Mechanic) is not null ||
            db.SignIn("123456", AppRole.Manager) is not null ||
            db.SignIn(mechanic.PersonalNumber, AppRole.Manager) is not null)
            throw new InvalidOperationException("בדיקת התחברות נכשלה.");
        var form = db.CreateFault(manager, "105", "603", "תקלה לדוגמה", [mechanic]);
        if (db.Faults(mechanic).Single().Serial != form.Serial)
            throw new InvalidOperationException("התקלה אינה מופיעה לטכנאי.");
        var outcomes = Enumerable.Repeat("V", 12).ToArray();
        db.SaveFault(form, mechanic, "תיקון לדוגמה", outcomes, true);
        var submitted = db.Faults(mechanic).Single();
        if (submitted.Status != "pending_review" || db.ActionOutcomes(form.Id).Count(x => x == "V") != 12)
            throw new InvalidOperationException("שמירת הטופס נכשלה.");
        db.Review(submitted, manager);
        if (db.Faults().Single().Status != "closed")
            throw new InvalidOperationException("סגירת המנהל נכשלה.");
        OfflineSmokeTest.Run(db, manager, mechanic);
    }
}
