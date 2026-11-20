using System.Drawing;

namespace EzEizo.Core;

public sealed class MainWindow : Form
{
    private readonly AppRole role;
    private readonly Database db = new();
    private Account? account;
    private readonly Panel content = new() { Dock = DockStyle.Fill, AutoScroll = true };
    private readonly Color ink = Color.FromArgb(21, 45, 65);
    private readonly Color blue = Color.FromArgb(24, 105, 150);
    private static readonly string[] Actions =
    [
        "חתום על כלים רלוונטיים ושג לנקודת עבודה",
        "זמן הגעה לדת״ק",
        "קבל אישור עבודה על המטוס מר״צ חימוש / מפקד דת״ק",
        "בצע ביקורת כלים עם ר״צ / מפקד תורן - קוד שכיח 310",
        "וודא מטוס בטוח לאחזקה לפי כרך בטיחות המטוס",
        "לפתרון התקלה פעל עפ״י תהליך מרכזי דרג א׳",
        "עדכן ר״צ / מפקד דת״ק בסטטוס המטוס",
        "בצע ביקורת כלים בסיום העבודה - קוד שכיח 310",
        "בצע בדיקה להיעדר גופים זרים במקום העבודה - קוד שכיח 3100",
        "זכה את הכלים שנלקחו מהגף",
        "הצמד תג בר תיקון למכלול / צב״ד תקול (אפשר גם לסמן X)",
        "זמן סיום התקלה"
    ];

    public MainWindow(AppRole role)
    {
        this.role = role;
        Text = role == AppRole.Manager ? "EzEizo · Metis" : "EzEizo · טכנאי";
        MinimumSize = new Size(840, 600);
        Size = new Size(1100, 790);
        Font = new Font("Segoe UI", 11);
        RightToLeft = RightToLeft.Yes;
        RightToLeftLayout = true;
        BackColor = Color.FromArgb(237, 244, 248);
        ForeColor = ink;
        Controls.Add(content);
        try { db.Initialize(); ShowLogin(); }
        catch (Exception ex) { ShowFailure(ex); }
    }

    private void ShowFailure(Exception ex)
    {
        content.Controls.Clear();
        var panel = Stack();
        panel.Controls.Add(Title("לא ניתן לפתוח את מסד הנתונים"));
        panel.Controls.Add(Info(ex.Message));
        panel.Controls.Add(Info($"הנתיב הנדרש: {Database.FilePath}"));
        panel.Controls.Add(Info("בדוק שמותקן Microsoft Access Database Engine בגרסת 64 סיביות."));
    }

    private FlowLayoutPanel Stack()
    {
        content.Controls.Clear();
        var stack = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, AutoScroll = true, Padding = new Padding(30)
        };
        content.Controls.Add(stack);
        stack.Resize += (_, _) =>
        {
            foreach (Control c in stack.Controls) c.Width = Math.Max(480, stack.ClientSize.Width - 75);
        };
        return stack;
    }

    private static Label Title(string text) => new()
    {
        Text = text, Font = new Font("Segoe UI", 23, FontStyle.Bold),
        Height = 70, TextAlign = ContentAlignment.MiddleRight, AutoSize = false
    };
    private static Label Info(string text) => new()
    {
        Text = text, Height = 55, AutoSize = false, TextAlign = ContentAlignment.MiddleRight
    };
    private Button Button(string text, Action action)
    {
        var b = new Button { Text = text, Height = 48, Width = 230, BackColor = blue, ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat, Margin = new Padding(7) };
        b.Click += (_, _) =>
        {
            try { action(); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "שגיאה", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        };
        return b;
    }
    private static TextBox Entry(string placeholder, int max = 255) =>
        new() { PlaceholderText = placeholder, Width = 370, Height = 36, MaxLength = max };

    private void ShowLogin()
    {
        var stack = Stack();
        stack.Controls.Add(Title(role == AppRole.Manager ? "כניסה למנהל · Metis" : "כניסה לטכנאי"));
        stack.Controls.Add(Info("הזן מספר אישי בן 7 ספרות."));
        var number = Entry("מספר אישי", 7);
        number.RightToLeft = RightToLeft.No;
        number.TextAlign = HorizontalAlignment.Right;
        number.KeyPress += (_, e) => { if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar)) e.Handled = true; };
        stack.Controls.Add(number);
        stack.Controls.Add(Button("כניסה", () =>
        {
            if (!Database.ValidPersonalNumber(number.Text)) throw new ArgumentException("מספר אישי חייב להכיל בדיוק 7 ספרות.");
            account = db.SignIn(number.Text, role) ?? throw new InvalidOperationException("לא נמצא משתמש פעיל בתפקיד המתאים.");
            ShowHome();
        }));
        if (role == AppRole.Manager && !db.HasManager())
        {
            stack.Controls.Add(Info("הגדרה ראשונית: צור את חשבון המנהל הראשון."));
            var name = Entry("שם המנהל", 100);
            stack.Controls.Add(name);
            stack.Controls.Add(Button("צור מנהל ראשון", () =>
            {
                account = db.CreateAccount(name.Text, number.Text, "manager");
                ShowHome();
            }));
        }
    }

    private void ShowHome()
    {
        var stack = Stack();
        stack.Controls.Add(Title(role == AppRole.Manager ? "דף מנהל" : "דף טכנאי"));
        stack.Controls.Add(Info($"שלום {account!.Name} · הנתונים בתיקייה: {Database.DataDirectory}"));
        if (role == AppRole.Manager)
        {
            stack.Controls.Add(Button("פתיחת תקלה", ShowNewFault));
            stack.Controls.Add(Button("ניהול משתמשים", ShowUsers));
        }
        stack.Controls.Add(Button("רשימת תקלות", ShowFaults));
        stack.Controls.Add(Button("התנתקות", () => { account = null; ShowLogin(); }));
    }

    private void ShowUsers()
    {
        var stack = Stack();
        stack.Controls.Add(Title("משתמשים"));
        stack.Controls.Add(Info("רק המנהל יוצר טכנאים. מספר אישי: 7 ספרות בדיוק."));
        var name = Entry("שם הטכנאי", 100);
        var number = Entry("מספר אישי", 7);
        number.RightToLeft = RightToLeft.No;
        number.KeyPress += (_, e) => { if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar)) e.Handled = true; };
        stack.Controls.Add(name);
        stack.Controls.Add(number);
        stack.Controls.Add(Button("צור טכנאי", () =>
        {
            db.CreateAccount(name.Text, number.Text, "mechanic");
            ShowUsers();
        }));
        foreach (var person in db.Technicians()) stack.Controls.Add(Info($"{person.Name} · {person.PersonalNumber}"));
        stack.Controls.Add(Button("חזרה", ShowHome));
    }

    private void ShowNewFault()
    {
        var stack = Stack();
        stack.Controls.Add(Title("תקלה חדשה"));
        var squadron = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 370 };
        squadron.Items.AddRange(["101", "105", "109", "מנ״ט"]);
        stack.Controls.Add(Info("טייסת"));
        stack.Controls.Add(squadron);
        var aircraft = Entry("מספר מטוס", 3);
        stack.Controls.Add(aircraft);
        var description = Entry("תיאור תקלה", 200);
        description.Multiline = true;
        description.Height = 95;
        stack.Controls.Add(description);
        var selected = new List<CheckBox>();
        stack.Controls.Add(Info("הקצה לפחות טכנאי אחד:"));
        foreach (var person in db.Technicians())
        {
            var box = new CheckBox { Text = person.Name, Tag = person, AutoSize = true };
            selected.Add(box);
            stack.Controls.Add(box);
        }
        stack.Controls.Add(Button("פתח תקלה", () =>
        {
            db.CreateFault(account!, squadron.Text, aircraft.Text, description.Text,
                selected.Where(x => x.Checked).Select(x => (Account)x.Tag!).ToList());
            ShowFaults();
        }));
        stack.Controls.Add(Button("חזרה", ShowHome));
    }

    private void ShowFaults()
    {
        var stack = Stack();
        stack.Controls.Add(Title("רשימת תקלות"));
        var tickets = db.Faults(role == AppRole.Mechanic ? account : null);
        if (tickets.Count == 0) stack.Controls.Add(Info("אין תקלות להצגה."));
        foreach (var ticket in tickets)
        {
            var label = $"{ticket.Serial} · {ticket.Squadron}/{ticket.Aircraft} · {ticket.Status}";
            stack.Controls.Add(Button(label, () => ShowFault(ticket)));
        }
        stack.Controls.Add(Button("חזרה", ShowHome));
    }

    private void ShowFault(Ticket ticket)
    {
        var stack = Stack();
        stack.Controls.Add(Title($"תקלה {ticket.Serial}"));
        stack.Controls.Add(Info($"מטוס {ticket.Aircraft} · טייסת {ticket.Squadron} · {ticket.Status}"));
        stack.Controls.Add(Info($"תיאור תקלה: {ticket.Description}"));
        var repair = Entry("תיאור תיקון", 2000);
        repair.Text = ticket.Repair;
        repair.Multiline = true;
        repair.Height = 110;
        repair.ReadOnly = role != AppRole.Mechanic || ticket.Status != "open";
        stack.Controls.Add(repair);
        var outcomes = db.ActionOutcomes(ticket.Id);
        var boxes = new List<CheckBox>();
        for (var i = 0; i < Actions.Length; i++)
        {
            var idx = i;
            var box = new CheckBox
            {
                Text = $"{i + 1:00}. {Actions[i]}",
                Checked = outcomes[i] is "V" or "X",
                AutoSize = false,
                Height = 50,
                Width = 850,
                Enabled = role == AppRole.Mechanic && ticket.Status == "open"
            };
            boxes.Add(box);
            stack.Controls.Add(box);
            if (idx == 10) box.ContextMenuStrip = new ContextMenuStrip();
        }
        if (role == AppRole.Mechanic && ticket.Status == "open")
        {
            stack.Controls.Add(Button("שמור", () =>
            {
                db.SaveFault(ticket, account!, repair.Text, boxes.Select((b, i) => b.Checked ? (i == 10 ? "X" : "V") : "").ToArray(), false);
                ShowFaults();
            }));
            stack.Controls.Add(Button("שמירה וסגירה", () =>
            {
                db.SaveFault(ticket, account!, repair.Text, boxes.Select((b, i) => b.Checked ? (i == 10 ? "X" : "V") : "").ToArray(), true);
                ShowFaults();
            }));
        }
        if (role == AppRole.Manager && ticket.Status == "pending_review")
            stack.Controls.Add(Button("אשר סגירת תקלה", () => { db.Review(ticket, account!); ShowFaults(); }));
        stack.Controls.Add(Button("חזרה", ShowFaults));
    }
}
