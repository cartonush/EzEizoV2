using System.Text;

namespace EzEizo.Core;

public static class NativeStartup
{
    public static string DiagnosticPath => Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "data", "startup-diagnostics.txt"));

    public static void Mark(string application, string stage)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(DiagnosticPath)!);
            File.AppendAllText(DiagnosticPath,
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} | {application} | {stage}{Environment.NewLine}", Encoding.UTF8);
        }
        catch { /* Startup must not depend on diagnostic logging. */ }
    }

    public static void Failure(string application, Exception error)
    {
        Mark(application, "ERROR: " + error);
        MessageBox.Show(error.GetBaseException().Message + Environment.NewLine + Environment.NewLine +
            "פרטי התקלה נשמרו בקובץ data/startup-diagnostics.txt",
            "EzEizo - שגיאת הפעלה", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
