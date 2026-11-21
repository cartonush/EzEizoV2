namespace EzEizo.Core;

public static class NativeDiagnostics
{
    public static void VerifyPdf()
    {
        var db = new Database();
        var manager = db.Authenticate("1234567", "654321", AppRole.Manager)
            ?? throw new InvalidOperationException("מנהל הבדיקה לא נמצא.");
        var form = new OfflineStore().List("issues", manager).First();
        var folder = Path.Combine(Path.GetTempPath(), "EzEizo-pdf-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var pdf = NativePdf.SaveForm(form, folder);
            if (new FileInfo(pdf).Length < 1000) throw new InvalidOperationException("קובץ PDF ריק.");
            var zip = NativePdf.SaveArchive([form], "יומן פעילות", folder);
            if (new FileInfo(zip).Length < 1000) throw new InvalidOperationException("קובץ ZIP ריק.");
        }
        finally { Directory.Delete(folder, true); }
    }
}
