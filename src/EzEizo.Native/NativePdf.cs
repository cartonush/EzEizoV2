using System.Drawing.Printing;
using System.IO.Compression;
using System.Text.Json.Nodes;

namespace EzEizo.Core;

internal static class NativePdf
{
    private sealed record Part(string Text, string? Signature = null, bool Heading = false);
    private static string V(JsonNode? node, string name) => node?[name]?.ToString() ?? "";
    private static bool B(JsonNode? node, string name) => V(node, name).Equals("true", StringComparison.OrdinalIgnoreCase);

    private static string FileName(JsonNode item)
    {
        var key = item["sheet"] is null;
        var serial = V(item, "serial");
        var day = DateTime.TryParse(V(item, "createdAt"), out var parsed) ? parsed.ToString("dd-MM-yyyy") : DateTime.Today.ToString("dd-MM-yyyy");
        return $"{(key ? "הטענת מפתחות" : "תקלה")} {serial} {day}.pdf";
    }
    private static string Unique(string folder, string name)
    {
        var destination = Path.Combine(folder, name);
        for (var n = 1; File.Exists(destination); n++)
            destination = Path.Combine(folder, $"{Path.GetFileNameWithoutExtension(name)} ({n}){Path.GetExtension(name)}");
        return destination;
    }
    internal static string SaveForm(JsonNode item, string folder)
    {
        Directory.CreateDirectory(folder);
        var path = Unique(folder, FileName(item));
        Save(Build(item), path);
        return path;
    }
    internal static string SaveArchive(IEnumerable<JsonNode> items, string reportType, string folder)
    {
        Directory.CreateDirectory(folder);
        var safeType = string.Concat(reportType.Where(ch => !Path.GetInvalidFileNameChars().Contains(ch)));
        var zip = Unique(folder, $"דוח - {safeType} - {DateTime.Now:dd-MM-yyyy HH-mm}.zip");
        var temporary = Path.Combine(Path.GetTempPath(), "EzEizo-report-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            foreach (var item in items) SaveForm(item, temporary);
            ZipFile.CreateFromDirectory(temporary, zip, CompressionLevel.Optimal, false);
            return zip;
        }
        finally { try { Directory.Delete(temporary, true); } catch { /* Temporary cleanup can be retried by Windows. */ } }
    }
    internal static string SaveReport(IEnumerable<JsonNode> items, string reportType, string folder)
    {
        Directory.CreateDirectory(folder);
        var safeType = string.Concat(reportType.Where(ch => !Path.GetInvalidFileNameChars().Contains(ch)));
        var path = Unique(folder, $"דוח - {safeType} - {DateTime.Now:dd-MM-yyyy HH-mm}.pdf");
        var entries = items.ToArray();
        var lines = new List<Part>
        {
            new($"דוח - {reportType}", Heading: true),
            new($"תאריך הפקה: {DateTime.Now:dd/MM/yyyy HH:mm}     טפסים: {entries.Length}"),
            new($"תקלות: {entries.Count(x => x["sheet"] is not null)}     הטענות מפתחות: {entries.Count(x => x["sheet"] is null)}"),
            new("טפסים בדוח", Heading: true)
        };
        foreach (var item in entries)
        {
            var sheet = item["sheet"] ?? item;
            var aircraft = V(sheet, "aircraft");
            if (aircraft.Length == 0) aircraft = string.Join(", ", (item["rows"] as JsonArray ?? new()).Select(x => V(x, "aircraft")));
            lines.Add(new($"{(item["sheet"] is null ? "הטענת מפתחות" : "תקלה")} {V(item, "serial")}   ·   טייסת {V(sheet, "squadron")}   ·   מטוס {aircraft}   ·   {V(item, "status")}   ·   {V(item, "createdAt")}"));
        }
        Save(lines, path);
        return path;
    }
    private static List<Part> Build(JsonNode item)
    {
        var key = item["sheet"] is null;
        var source = item["sheet"] ?? item;
        var state = key ? item["work"] ?? item["workDraft"] : item["closed"]?["state"];
        var lines = new List<Part>
        {
            new($"{(key ? "הטענת מפתחות" : "דף טיפול בתקלה")} {V(item, "serial")}", Heading: true),
            new($"טייסת: {V(source, "squadron")}     דת״ק: {V(source, "shelter")}     סטטוס: {V(item, "status")}"),
            new($"שעת פתיחה: {V(item, "createdAt")}     שעת סגירה: {V(item, "reviewedAt")}{V(item, "completedAt")}")
        };
        if (key)
        {
            lines.Add(new($"תאריך: {V(item, "date")}     שעה: {V(item, "time")}     צוות: {string.Join(" · ", (item["teamNames"] as JsonArray ?? new()).Select(x => x?.ToString()))}"));
            lines.Add(new("רשימת הטענה", Heading: true));
            var aircraft = state?["aircraft"] as JsonArray ?? new();
            var rows = item["rows"] as JsonArray ?? new();
            for (var i = 0; i < rows.Count; i++)
                lines.Add(new($"מטוס {V(rows[i], "aircraft")} | ארז כחול: {(B(rows[i], "blue") ? B(i < aircraft.Count ? aircraft[i] : null, "blueDone") ? "בוצע" : "נדרש" : "—")} | שחק: {(B(rows[i], "shach") ? B(i < aircraft.Count ? aircraft[i] : null, "shachDone") ? "בוצע" : "נדרש" : "—")} | הערות: {V(rows[i], "notes")} {V(i < aircraft.Count ? aircraft[i] : null, "technicianNote")} "));
        }
        else
        {
            lines.Add(new($"מטוס {V(source, "aircraft")} | {V(source, "systemName")} | {V(source, "systemCode")} | חליפת ל״א {V(source, "ewSystem")}"));
            lines.Add(new("תיאור תקלה: " + V(source, "fault"), Heading: true));
            lines.Add(new("תיאור תיקון: " + V(state, "repair")));
            lines.Add(new("הערות מנהל: " + V(source, "managerNotes")));
            lines.Add(new("הערות טכנאי: " + V(state, "technicianNote")));
            lines.Add(new("בדיקת המטוס בטוח לאחזקה טרם התהליך", V(state, "safetyTechnicianSignature")));
        }
        lines.Add(new("פעולות שבוצעו", Heading: true));
        var tasks = key ? NativeCatalog.KeyActions : NativeCatalog.Actions;
        var actions = state?["actions"] as JsonArray ?? new();
        for (var i = 0; i < tasks.Length; i++)
            lines.Add(new($"{i + 1:D2}. {(i < actions.Count && actions[i]?.ToString() == "true" ? "✓" : "□")} {tasks[i]}"));
        foreach (var (name, spec) in NativeCatalog.Tables)
        {
            var rows = state?[name] as JsonArray;
            if (rows is null || rows.Count == 0) continue;
            lines.Add(new(spec.title, Heading: true));
            lines.Add(new(string.Join(" | ", spec.fields.Select(x => x.label))));
            foreach (var row in rows)
                if (spec.fields.Any(field => V(row, field.key).Length > 0))
                    lines.Add(new(string.Join(" | ", spec.fields.Select(x => V(row, x.key)))));
        }
        foreach (var (title, keys) in new[]
        {
            ("ביקורת כניסה", new[] { "entryTechnicianSignature", "entryInspectorSignature" }),
            ("ביקורת במהלך העבודה", new[] { "interimTechnicianSignature", "interimInspectorSignature", "interimToolsSignature" }),
            ("ביקורת בסיום העבודה", new[] { "finalTechnicianSignature", "finalInspectorSignature", "finalLeadSignature" })
        })
        {
            if (!keys.Any(k => V(state, k).Length > 0)) continue;
            lines.Add(new(title, Heading: true));
            foreach (var name in keys) if (V(state, name).Length > 0) lines.Add(new(name, V(state, name)));
        }
        if (!key) lines.Add(new("הצהרת הטכנאי", V(state, "technicianDeclarationSignature")));
        if (V(item, "reviewerSignature").Length > 0) lines.Add(new("חתימת בודק הדף", V(item, "reviewerSignature")));
        return lines;
    }
    private static void Save(List<Part> parts, string path)
    {
        using var document = new PrintDocument();
        document.PrintController = new StandardPrintController();
        document.DocumentName = Path.GetFileNameWithoutExtension(path);
        document.PrinterSettings.PrinterName = "Microsoft Print to PDF";
        if (!document.PrinterSettings.IsValid) throw new InvalidOperationException("מדפסת Microsoft Print to PDF אינה מותקנת במחשב.");
        document.PrinterSettings.PrintToFile = true;
        document.PrinterSettings.PrintFileName = path;
        var index = 0;
        document.PrintPage += (_, e) =>
        {
            if (e.Graphics is null) return;
            using var textFont = new Font("Arial", 10);
            using var titleFont = new Font("Arial", 13, FontStyle.Bold);
            using var format = new StringFormat(StringFormatFlags.DirectionRightToLeft) { Alignment = StringAlignment.Near };
            var x = e.MarginBounds.Left; var y = e.MarginBounds.Top;
            while (index < parts.Count)
            {
                var part = parts[index];
                var font = part.Heading ? titleFont : textFont;
                var width = e.MarginBounds.Width;
                var measured = e.Graphics.MeasureString(part.Text, font, width, format);
                var height = Math.Max(25, (int)Math.Ceiling(measured.Height) + 6);
                var isSignature = part.Signature?.StartsWith("data:image/png;base64,", StringComparison.Ordinal) == true;
                var total = height + (isSignature ? 80 : 0);
                if (y + total > e.MarginBounds.Bottom && y > e.MarginBounds.Top) break;
                e.Graphics.DrawString(part.Text, font, Brushes.Black, new RectangleF(x, y, width, height), format);
                y += height;
                if (isSignature)
                {
                    try
                    {
                        using var stream = new MemoryStream(Convert.FromBase64String(part.Signature![22..]));
                        using var image = Image.FromStream(stream);
                        e.Graphics.DrawImage(image, e.MarginBounds.Right - 190, y, 190, 70);
                    }
                    catch { /* Invalid legacy signature remains marked by the signed label. */ }
                    y += 80;
                }
                index++;
            }
            e.HasMorePages = index < parts.Count;
        };
        document.Print();
        if (!File.Exists(path)) throw new IOException("הדפסת PDF לא יצרה קובץ. בדוק את מדפסת Microsoft Print to PDF.");
    }
}
