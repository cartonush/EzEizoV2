using System.Collections.Concurrent;
using System.Text.Json.Nodes;

namespace EzEizo.Core;

// Native Windows UI backed by the local Access store; no embedded browser.
public sealed partial class NativeWindow : Form
{
    internal string DiagnosticPage => readyPage;
    private static readonly Color Navy = Color.FromArgb(11, 39, 67);
    private static readonly Color Blue = Color.FromArgb(30, 107, 158);
    private static readonly Color Pale = Color.FromArgb(242, 246, 248);
    private static readonly Color Muted = Color.FromArgb(107, 124, 139);
    private readonly AppRole role;
    private readonly Database database = new();
    private readonly OfflineStore store = new();
    private readonly BlockingCollection<(Func<object?> work, TaskCompletionSource<object?> done)> jobs = new();
    private readonly Panel content = new() { Dock = DockStyle.Fill, BackColor = Pale };
    private readonly Label status = new() { Dock = DockStyle.Bottom, Height = 32, Padding = new Padding(14, 6, 14, 6), TextAlign = ContentAlignment.MiddleLeft, BackColor = Color.White };
    private readonly NativeHeader header = new() { Dock = DockStyle.Top, Height = 78 };
    private readonly Panel bottomNav = new() { Dock = DockStyle.Top, Height = 72, BackColor = Color.FromArgb(249, 252, 253) };
    private readonly Panel actionDock = new() { Dock = DockStyle.Bottom, Height = 68, BackColor = Color.White, Visible = false };
    private readonly FlowLayoutPanel actionButtons = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Padding = new Padding(12, 8, 12, 8), BackColor = Color.White };
    private readonly NativeButton backButton = new() { Text = "→ חזרה", Secondary = true, OnDark = true, Width = 110, Height = 37 };
    private readonly NativeButton logoutButton = new() { Text = "התנתק", Secondary = true, OnDark = true, Width = 105, Height = 37 };
    private string documentSection = "issues";
    private Panel? activeCard;
    private readonly List<NativeButton> navButtons = [];
    private Account? account;
    private string currentPage = "";
    private string readyPage = "";
    private string searchTerm = "";
    private string closedOwnerId = "";
    private string statusFilter = "הכל";
    private string listSection = "";
    private Database.TechnicianRecord? historyPerson;
    private int? lastListVersion;
    private readonly System.Windows.Forms.Timer refreshTimer = new() { Interval = 10000 };

    public NativeWindow(AppRole role)
    {
        this.role = role;
        Text = role == AppRole.Manager ? "EzEizo · Metis" : "EzEizo · טכנאי";
        MinimumSize = new Size(960, 660);
        Size = new Size(1120, 820);
        RightToLeft = RightToLeft.Yes;
        RightToLeftLayout = true;
        Font = NativeFonts.Create(10);
        header.Heading = role == AppRole.Manager ? "Metis · דף מנהל" : "גף אוויוניקה קרב · טכנאי";
        header.Subtitle = role == AppRole.Manager ? "יצירת דפי תקלה לטכנאים" : "דף טיפול בתקלה";
        Text += " · 2.6.1";
        header.Manager = role == AppRole.Manager;
        status.TextChanged += (_, _) =>
        {
            var warning = new[] { "חסר", "נכשל", "שגוי", "בעיה", "שגיאה", "לא ניתן", "אין טכנאים" }
                .Any(term => status.Text.Contains(term, StringComparison.OrdinalIgnoreCase));
            status.ForeColor = warning ? Color.FromArgb(205, 22, 43) : Navy;
        };
        var brand = new NativeBrandMark { Text = role == AppRole.Manager ? "מ" : "ט", Manager = role == AppRole.Manager,
            Size = new Size(44, 44), Font = NativeFonts.Create(18, FontStyle.Bold) };
        var version = new NativeBadge { Text = "v2.6.1", Size = new Size(79, 35), Font = NativeFonts.Create(8, FontStyle.Bold) };
        header.Controls.AddRange([brand, version, backButton, logoutButton]);
        header.Resize += (_, _) =>
        {
            brand.Location = new Point(header.Width - 72, 17);
            version.Location = new Point(24, 21);
            logoutButton.Location = new Point(112, 20);
            backButton.Location = new Point(225, 20);
        };
        backButton.Click += async (_, _) =>
        {
            if (currentPage == "document" && historyPerson is not null) await OpenUserHistory(historyPerson);
            else if (currentPage is "document" or "fault-editor" or "key-editor" or "edit-definition") await OpenList(documentSection);
            else if (currentPage is "new-key" or "key-history") await OpenList("keys");
            else if (currentPage == "constants-editor") await OpenConstants();
            else if (currentPage == "user-history") await OpenUsers();
            else if (currentPage is "users" or "constants" or "reports") await OpenManagementHome();
            else await OpenList("issues");
        };
        logoutButton.Click += async (_, _) => { await ReleaseForm(); account = null; Login(); };
        bottomNav.Controls.Add(new Panel { Dock = DockStyle.Bottom, Height = 1, BackColor = Color.FromArgb(213, 226, 233) });
        bottomNav.Resize += (_, _) => LayoutBottomNav();
        actionDock.Controls.Add(actionButtons);
        actionDock.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 1, BackColor = Color.FromArgb(213, 226, 233) });
        actionDock.Resize += (_, _) => LayoutPinnedActions();
        if (role == AppRole.Manager)
        {
            foreach (var (title, action) in new (string, Func<Task>)[]
            {
                ("בית", OpenHome), ("תקלות", () => OpenList("issues")),
                ("מפתחות", () => OpenList("keys")), ("ניהול", OpenManagementHome)
            })
            {
                var button = new NativeButton { Text = title, Secondary = true, Font = NativeFonts.Create(9, FontStyle.Bold) };
                button.Click += async (_, _) => await action();
                bottomNav.Controls.Add(button);
                navButtons.Add(button);
            }
        }
        content.Resize += (_, _) => CenterCard();
        Controls.Add(content);
        Controls.Add(status);
        Controls.Add(actionDock);
        Controls.Add(bottomNav);
        Controls.Add(header);
        var worker = new Thread(() =>
        {
            foreach (var (work, done) in jobs.GetConsumingEnumerable())
                try { done.SetResult(work()); } catch (Exception ex) { done.SetException(ex); }
        }) { IsBackground = true, Name = "EzEizo native Access worker" };
        worker.SetApartmentState(ApartmentState.STA);
        worker.Start();
        Shown += async (_, _) =>
        {
            NativeStartup.Mark(role.ToString(), "Window visible; initializing database");
            Loading("מכין את המסד המקומי…");
            try { await Work(() => { SharedDatabaseStartup.Initialize(); return true; }); NativeStartup.Mark(role.ToString(), "Database ready"); Login(); }
            catch (Exception ex) { Error(ex); }
        };
        refreshTimer.Tick += async (_, _) =>
        {
            if (account is null || currentPage is not ("issues" or "keys" or "queue")) return;
            var page = currentPage; var user = account;
            try
            {
                var revision = await Work(() => page == "queue"
                    ? HashCode.Combine(store.Version("issues"), store.Version("keyLoads"))
                    : store.Version(page == "issues" ? "issues" : "keyLoads"));
                if (account == user && currentPage == page && revision != lastListVersion) await OpenList(page, silent: true);
            }
            catch (Exception ex) { status.Text = "בדיקת עדכונים נכשלה: " + ex.Message; }
        };
        FormClosed += (_, _) => { refreshTimer.Stop(); jobs.CompleteAdding(); };
    }

    private async Task<T> Work<T>(Func<T> action)
    {
        var result = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        jobs.Add((() => action(), result));
        return (T)(await result.Task)!;
    }
    private void Loading(string message)
    {
        readyPage = "";
        status.Text = message;
        var container = Stack();
        container.Controls.Add(Label(message, 46, true));
        for (var i = 0; i < 3; i++)
            container.Controls.Add(new NativeSkeletonBlock(i == 0 ? 62 : 136));
        Present(container, loading: true);
    }
    private void Error(Exception ex)
    {
        NativeStartup.Mark(role.ToString(), "ERROR: " + ex);
        status.Text = "הפעולה נכשלה";
        MessageBox.Show(this, ex.Message, "EzEizo", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
    private static FlowLayoutPanel Stack() => new()
    {
        Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown,
        WrapContents = false, Padding = new Padding(20, 22, 20, 28), BackColor = Color.White
    };
    private static Label Label(string text, int height = 34, bool bold = false) => new()
    {
        Text = text, Width = 750, Height = height, TextAlign = ContentAlignment.MiddleLeft,
        ForeColor = Navy, Font = NativeFonts.Create(bold ? 14.25f : 10, bold ? FontStyle.Bold : FontStyle.Regular)
    };
    private static Button Button(string label, EventHandler click)
    {
        using var measuredFont = NativeFonts.Create(9.75f, FontStyle.Bold);
        var measuredWidth = TextRenderer.MeasureText(label.Replace(Environment.NewLine, " "), measuredFont).Width;
        var button = new NativeButton { Text = label, Width = Math.Clamp(measuredWidth + 42, 175, 350), Height = 44,
            Font = NativeFonts.Create(9.75f, FontStyle.Bold), Margin = new Padding(0, 0, 7, 12) };
        button.Click += click;
        return button;
    }
    private static TextBox Input(string placeholder, int max = 200, bool password = false) => new()
    {
        PlaceholderText = placeholder, Width = 440, Height = 42, AutoSize = false, MaxLength = max,
        UseSystemPasswordChar = password, Margin = new Padding(0, 0, 0, 15),
        BorderStyle = BorderStyle.FixedSingle, BackColor = Color.FromArgb(251, 253, 254),
        ForeColor = Color.FromArgb(21, 43, 64)
    };
    private static string DisplayDate(string value) => DateTimeOffset.TryParse(value, out var date)
        ? date.ToLocalTime().ToString("dd/MM/yyyy HH:mm") : value;
    private static Panel DetailTile(string title, string value, int width = 176)
    {
        var tile = new NativeStatTile { Width = width, Height = 70,
            Margin = new Padding(0, 0, 10, 10), Padding = new Padding(10) };
        tile.Controls.Add(new Label { Text = title, Dock = DockStyle.Top, Height = 25,
            ForeColor = Muted, Font = NativeFonts.Create(8.25f), TextAlign = ContentAlignment.MiddleLeft });
        tile.Controls.Add(new Label { Text = value, Dock = DockStyle.Bottom, Height = 31,
            ForeColor = Navy, Font = NativeFonts.Create(11.25f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true });
        return tile;
    }
    private static NativeCard FormListCard(string title, string details, string date,
        string state, EventHandler open)
    {
        var card = new NativeCard { Width = 715, Height = 118, Margin = new Padding(0, 0, 0, 10) };
        card.Controls.Add(new Label { Text = title, Location = new Point(235, 10), Size = new Size(460, 29),
            TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true,
            ForeColor = Navy, Font = NativeFonts.Create(11.25f, FontStyle.Bold) });
        card.Controls.Add(new Label { Text = details, Location = new Point(150, 41), Size = new Size(545, 25),
            TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true,
            ForeColor = Muted, Font = NativeFonts.Create(9) });
        card.Controls.Add(new Label { Text = date, Location = new Point(275, 70), Size = new Size(420, 22),
            TextAlign = ContentAlignment.MiddleLeft, ForeColor = Muted, Font = NativeFonts.Create(8.5f) });
        card.Controls.Add(new NativeStatusBadge { State = state,
            Text = state switch { "closed" => "סגור", "pendingReview" => "לסגירה", _ => "פתוח" },
            Location = new Point(17, 13), Size = new Size(100, 30) });
        var action = (NativeButton)Button("פתיחת טופס", open);
        action.Secondary = true; action.Width = 145; action.Height = 39;
        action.Location = new Point(17, 62);
        card.Controls.Add(action);
        return card;
    }
    private void CenterCard()
    {
        if (activeCard is null || activeCard.IsDisposed) return;
        var desired = currentPage == "login" ? 600 : 880;
        activeCard.Width = Math.Min(desired, Math.Max(520, content.ClientSize.Width - 32));
        var desiredHeight = currentPage switch
        {
            "login" => 455, "home" => 580, "management" => 530,
            "issues" or "keys" or "queue" => content.ClientSize.Height - 32, _ => content.ClientSize.Height - 32
        };
        activeCard.Height = Math.Min(Math.Max(200, desiredHeight), content.ClientSize.Height - 32);
        activeCard.Location = new Point((content.ClientSize.Width - activeCard.Width) / 2, 16);
    }
    private void Present(FlowLayoutPanel stack, bool loading = false)
    {
        var previousControls = content.Controls.Cast<Control>().ToArray();
        content.Controls.Clear();
        foreach (var previous in previousControls) previous.Dispose();
        var previousActions = actionButtons.Controls.Cast<Control>().ToArray();
        actionButtons.Controls.Clear();
        foreach (var previousAction in previousActions) previousAction.Dispose();
        actionDock.Visible = false;
        var card = new Panel { BackColor = Pale };
        stack.Dock = DockStyle.Fill;
        var toolbar = stack.Controls.OfType<FlowLayoutPanel>().FirstOrDefault(x => x.Name == "EditorToolbar");
        Control page;
        if (toolbar is null)
        {
            page = BuildPage(stack);
            card.Controls.Add(page);
        }
        else
        {
            stack.Controls.Remove(toolbar);
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            toolbar.Dock = DockStyle.Fill;
            layout.Controls.Add(toolbar, 0, 0);
            layout.Controls.Add(BuildPage(stack), 0, 1);
            card.Controls.Add(layout);
            page = layout;
        }
        PinImportantActions(page);
        activeCard = card;
        content.Controls.Add(card);
        StyleInputs(card);
        StyleGrids(card);
        CenterCard();
        bottomNav.Visible = !loading && account is not null && role == AppRole.Manager;
        logoutButton.Visible = !loading && account is not null;
        backButton.Visible = !loading && account is not null &&
            currentPage is not ("home" or "management" or "issues" or "keys" or "queue");
        var section = currentPage switch
        {
            "home" => 0, "issues" or "new-fault" or "fault-editor" => 1,
            "keys" or "new-key" or "key-editor" or "key-history" => 2,
            "management" or "users" or "user-history" or "constants" or "constants-editor" or "reports" => 3,
            "document" or "edit-definition" => documentSection == "keys" ? 2 : 1, _ => -1
        };
        for (var i = 0; i < navButtons.Count; i++)
        {
            navButtons[i].Active = i == section;
            navButtons[i].Invalidate();
        }
        LayoutBottomNav();
        if (!loading) readyPage = currentPage;
    }
    private static Control BuildPage(FlowLayoutPanel stack)
    {
        if (Equals(stack.Tag, "dashboard")) return stack;
        if (!stack.Controls.Cast<Control>().Any(x => Equals(x.Tag, "section-heading")))
        {
            var plain = new NativeCard { Dock = DockStyle.Fill, Padding = new Padding(3) };
            plain.Controls.Add(stack);
            return plain;
        }

        // Each heading begins a separate card, as in the original page. The outer
        // panel owns the only scrollbar; grids and signatures keep their own size.
        var page = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true,
            FlowDirection = FlowDirection.TopDown, WrapContents = false,
            Padding = new Padding(0, 0, 14, 12), BackColor = Pale };
        FlowLayoutPanel? section = null;
        var sections = new List<(NativeCard card, FlowLayoutPanel body)>();
        foreach (var child in stack.Controls.Cast<Control>().ToArray())
        {
            stack.Controls.Remove(child);
            if (section is null || Equals(child.Tag, "section-heading"))
            {
                var card = new NativeCard { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                    Margin = new Padding(0, 0, 0, 16), Padding = new Padding(3) };
                section = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                    FlowDirection = FlowDirection.TopDown, WrapContents = false,
                    BackColor = Color.White, Padding = new Padding(20, 16, 20, 20) };
                card.Controls.Add(section);
                page.Controls.Add(card);
                sections.Add((card, section));
            }
            section.Controls.Add(child);
        }
        void Fit()
        {
            var width = Math.Max(500, page.ClientSize.Width - page.Padding.Horizontal - 22);
            foreach (var (card, body) in sections)
            {
                card.Width = width;
                body.Width = width - 6;
                var inner = width - body.Padding.Horizontal - 12;
                foreach (Control child in body.Controls)
                {
                    if (child is System.Windows.Forms.Label or DataGridView || Equals(child.Tag, "section-heading") ||
                        child is FlowLayoutPanel || child is CheckBox || child is NativeSkeletonBlock)
                        child.Width = inner;
                    else if (child is NativeInputFrame frame && Equals(frame.Tag, "wide-input"))
                        frame.Width = inner;
                }
            }
        }
        page.Resize += (_, _) => Fit();
        page.HandleCreated += (_, _) => Fit();
        return page;
    }
    private static void StyleGrids(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            if (child is DataGridView grid)
            {
                grid.BackgroundColor = Color.White;
                grid.GridColor = Color.FromArgb(213, 226, 233);
                grid.BorderStyle = BorderStyle.None;
                grid.EnableHeadersVisualStyles = false;
                if (grid.RowTemplate.Height < 36) grid.RowTemplate.Height = 36;
                if (grid.ColumnHeadersHeight < 40) grid.ColumnHeadersHeight = 40;
                grid.ColumnHeadersDefaultCellStyle.BackColor = Pale;
                grid.ColumnHeadersDefaultCellStyle.ForeColor = Navy;
                grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(8, 0, 8, 0);
                grid.DefaultCellStyle.BackColor = Color.White;
                grid.DefaultCellStyle.ForeColor = Navy;
                grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(222, 239, 247);
                grid.DefaultCellStyle.SelectionForeColor = Navy;
                grid.DefaultCellStyle.Padding = new Padding(8, 2, 8, 2);
                foreach (var column in grid.Columns.OfType<DataGridViewCheckBoxColumn>())
                    column.FlatStyle = FlatStyle.Flat;
                grid.CellPainting += (_, e) =>
                {
                    if (e.RowIndex < 0 || e.ColumnIndex < 0 || grid.Columns[e.ColumnIndex] is not DataGridViewCheckBoxColumn) return;
                    e.PaintBackground(e.CellBounds, true);
                    var isChecked = e.Value is true || e.FormattedValue is true;
                    var size = 19;
                    var mark = new Rectangle(e.CellBounds.Left + (e.CellBounds.Width - size) / 2,
                        e.CellBounds.Top + (e.CellBounds.Height - size) / 2, size, size);
                    NativeCheckBox.DrawMark(e.Graphics, mark, isChecked, grid.Enabled);
                    e.Paint(e.CellBounds, DataGridViewPaintParts.Border);
                    e.Handled = true;
                };
            }
            StyleGrids(child);
        }
    }
    private static void StyleInputs(Control parent)
    {
        foreach (var child in parent.Controls.Cast<Control>().ToArray())
        {
            if (child is TextBox input && input.BorderStyle == BorderStyle.FixedSingle && parent is FlowLayoutPanel flow)
            {
                var index = flow.Controls.GetChildIndex(input);
                flow.Controls.Remove(input);
                var frame = new NativeInputFrame { Width = input.Width, Height = input.Height,
                    Margin = input.Margin, Tag = input.Multiline ? "wide-input" : null };
                input.BorderStyle = BorderStyle.None;
                input.Margin = Padding.Empty;
                input.Location = new Point(11, input.Multiline ? 9 : 11);
                input.Width = frame.Width - 22;
                input.Height = Math.Max(20, frame.Height - (input.Multiline ? 18 : 17));
                input.GotFocus += (_, _) => frame.Invalidate();
                input.LostFocus += (_, _) => frame.Invalidate();
                frame.Resize += (_, _) => input.Width = Math.Max(20, frame.ClientSize.Width - 22);
                frame.Controls.Add(input);
                flow.Controls.Add(frame);
                flow.Controls.SetChildIndex(frame, index);
            }
            else if (child is NativeChoice choice && parent is FlowLayoutPanel choices)
            {
                var index = choices.Controls.GetChildIndex(choice);
                choices.Controls.Remove(choice);
                var frame = new NativeInputFrame { Width = choice.Width, Height = 44, Margin = choice.Margin };
                choice.Margin = Padding.Empty;
                choice.FlatStyle = FlatStyle.Flat;
                choice.Location = new Point(5, 4);
                choice.Width = frame.Width - 10;
                choice.GotFocus += (_, _) => frame.Invalidate();
                choice.LostFocus += (_, _) => frame.Invalidate();
                frame.Resize += (_, _) => choice.Width = Math.Max(40, frame.ClientSize.Width - 10);
                frame.Controls.Add(choice);
                choices.Controls.Add(frame);
                choices.Controls.SetChildIndex(frame, index);
            }
            else StyleInputs(child);
        }
    }
    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private void PinImportantActions(Control page)
    {
        var labels = currentPage switch
        {
            "issues" => new HashSet<string>(["פתיחת תקלה"]),
            "keys" => new HashSet<string>(["הטענה חדשה"]),
            "new-fault" => new HashSet<string>(["פתח תקלה"]),
            "new-key" => new HashSet<string>(["פתח הטענה"]),
            "fault-editor" => new HashSet<string>(["שמירה וסגירה"]),
            "key-editor" => new HashSet<string>(["סיים הטענה"]),
            "constants-editor" => new HashSet<string>(["שמור קבועים למטוס"]),
            "document" => new HashSet<string>(["בדוק וסגור"]),
            "reports" => new HashSet<string>(["הצג דוח", "הורד PDF של הדוח", "הפק CSV", "הורד את תוצאות החיפוש"]),
            _ => new HashSet<string>()
        };
        var matches = Descendants(page).OfType<NativeButton>().Where(button => labels.Contains(button.Text)).ToArray();
        foreach (var button in matches)
        {
            button.Parent?.Controls.Remove(button);
            button.Margin = new Padding(4);
            button.Height = 48;
            actionButtons.Controls.Add(button);
        }
        actionDock.Visible = matches.Length > 0;
        LayoutPinnedActions();
    }

    private void LayoutPinnedActions()
    {
        var buttons = actionButtons.Controls.OfType<NativeButton>().ToArray();
        if (buttons.Length == 0) return;
        var gap = 8;
        var width = Math.Max(150, (actionButtons.ClientSize.Width - actionButtons.Padding.Horizontal - gap * (buttons.Length - 1)) / buttons.Length - 8);
        width = Math.Min(260, width);
        foreach (var button in buttons) button.Width = width;
    }

    private void LayoutBottomNav()
    {
        if (navButtons.Count == 0) return;
        var width = Math.Min(850, bottomNav.ClientSize.Width - 24);
        var left = (bottomNav.ClientSize.Width - width) / 2;
        var each = (width - 24) / 4;
        for (var i = 0; i < navButtons.Count; i++)
            navButtons[i].SetBounds(left + (3 - i) * (each + 8), 10, each, 53);
    }
    private static NativeCard DashboardCard(string kicker, string title, string? description, int height)
    {
        var card = new NativeCard { Width = 850, Height = height, Margin = new Padding(0, 0, 0, 16) };
        card.Controls.Add(new Label
        {
            Text = kicker, Location = new Point(24, 20), Size = new Size(800, 22),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            TextAlign = ContentAlignment.MiddleLeft, ForeColor = Blue,
            Font = NativeFonts.Create(8.25f, FontStyle.Bold)
        });
        card.Controls.Add(new Label
        {
            Text = title, Location = new Point(24, 48), Size = new Size(800, 36),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            TextAlign = ContentAlignment.MiddleLeft, ForeColor = Navy,
            Font = NativeFonts.Create(14.25f, FontStyle.Bold)
        });
        if (description is not null)
            card.Controls.Add(new Label
            {
                Text = description, Location = new Point(24, 94), Size = new Size(800, 35),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                TextAlign = ContentAlignment.MiddleLeft, ForeColor = Muted,
                Font = NativeFonts.Create(10)
            });
        return card;
    }

    private static NativeButton DashboardTile(string title, string detail, Func<Task> action)
    {
        var tile = (NativeButton)Button(title, async (_, _) => await action());
        tile.Secondary = true;
        tile.TileTitle = title;
        tile.TileDescription = detail;
        tile.AccessibleName = title;
        tile.AccessibleDescription = detail;
        tile.Margin = Padding.Empty;
        return tile;
    }

    private async Task OpenHome()
    {
        if (account is null) { Login(); return; }
        currentPage = "home"; refreshTimer.Stop();
        var stack = Stack();
        stack.Tag = "dashboard";
        stack.BackColor = Pale;
        stack.Padding = new Padding(0, 0, 14, 12);
        var intro = DashboardCard("דף בית", "שלום " + account.Name,
            "בחר את תחום העבודה כדי לפתוח דף או לעקוב אחר משימות הצוות.", 153);
        stack.Controls.Add(intro);
        var tiles = new Panel { Width = 850, Height = 339, BackColor = Pale };
        var entries = new (string title, string detail, Func<Task> action)[]
        {
            ("פתיחת תקלה", "יצירת דף טיפול חדש לטכנאים", ShowCreateFault),
            ("תקלות פתוחות", "משימות טיפול פעילות", () => OpenList("issues")),
            ("תקלות לסגירה וסגורות", "סקירה וחתימת בודק הדף", async () =>
            { listSection = "issues"; statusFilter = "ממתין לסגירה"; await OpenList("issues"); }),
            ("מפתחות", "פתיחת הטענה, מעקב והיסטוריה", () => OpenList("keys")),
            ("ניהול", "משתמשים, הפקת דוחות ועדכון קבועים לפי מטוס", OpenManagementHome)
        };
        var buttons = entries.Select(entry => DashboardTile(entry.title, entry.detail, entry.action)).ToArray();
        tiles.Controls.AddRange(buttons);
        stack.Controls.Add(tiles);
        void Fit()
        {
            var width = Math.Max(520, stack.ClientSize.Width - stack.Padding.Horizontal - 16);
            intro.Width = tiles.Width = width;
            var tileWidth = (width - 12) / 2;
            for (var i = 0; i < buttons.Length; i++)
                buttons[i].SetBounds(i % 2 == 0 ? width - tileWidth : 0,
                    i / 2 * 117, tileWidth, 105);
        }
        stack.Resize += (_, _) => Fit();
        Present(stack);
        Fit();
        status.Text = "הנתונים נשמרים במסד המקומי";
        await Task.CompletedTask;
    }

    private async Task OpenManagementHome()
    {
        if (account is null || role != AppRole.Manager) return;
        currentPage = "management"; refreshTimer.Stop();
        var stack = Stack();
        stack.Tag = "dashboard";
        stack.BackColor = Pale;
        stack.Padding = new Padding(0, 0, 14, 12);
        var card = DashboardCard("דף ראשי", "ניהול", null, 372);
        var entries = new (string title, string detail, Func<Task> action)[]
        {
            ("ניהול משתמשים", "יצירה, הסרה וצפייה בתקלות לפי טכנאי", OpenUsers),
            ("הפקת דוחות", "סינון, תצוגה מקדימה והפקת PDF או CSV", OpenReports),
            ("עדכון קבועים לפי מטוס", "עריכת ערכי המערכות למטוסים", OpenConstants)
        };
        var buttons = entries.Select(entry => DashboardTile(entry.title, entry.detail, entry.action)).ToArray();
        card.Controls.AddRange(buttons);
        stack.Controls.Add(card);
        void Fit()
        {
            var width = Math.Max(520, stack.ClientSize.Width - stack.Padding.Horizontal - 16);
            card.Width = width;
            var tileWidth = (width - 60) / 2;
            for (var i = 0; i < buttons.Length; i++)
                buttons[i].SetBounds(i % 2 == 0 ? width - 24 - tileWidth : 24,
                    108 + i / 2 * 117, tileWidth, 105);
        }
        stack.Resize += (_, _) => Fit();
        Present(stack);
        Fit();
        status.Text = "ניהול המערכת";
        await Task.CompletedTask;
    }
    private void Login()
    {
        currentPage = "login";
        refreshTimer.Stop();
        var stack = Stack();
        stack.Controls.Add(Label("גישה מאובטחת", 32));
        stack.Controls.Add(Label("כניסה למערכת", 64, true));
        stack.Controls.Add(Label("התחבר עם המספר האישי והסיסמה שלך.", 43));
        stack.Controls.Add(Label("מספר אישי", 32));
        var number = Input("7 ספרות", 7);
        stack.Controls.Add(number);
        stack.Controls.Add(Label("סיסמה", 32));
        var password = Input("6 תווים", 6, true);
        stack.Controls.Add(password);
        Button? submit = null;
        submit = Button("התחבר", async (_, _) =>
        {
            if (number.Text.Length != 7 || !number.Text.All(char.IsAsciiDigit) || password.Text.Length != 6)
            {
                status.Text = "יש להזין מספר אישי בן 7 ספרות וסיסמה בת 6 תווים.";
                return;
            }
            var digits = number.Text; var secret = password.Text;
            submit!.Enabled = false;
            status.Text = "בודק פרטי כניסה במסד המקומי…";
            try
            {
                var found = await Work(() => database.Authenticate(digits, secret, role));
                if (found is null || role == AppRole.Manager && !await Work(() => database.IsPrimaryManager(found)))
                    throw new UnauthorizedAccessException("מספר אישי או סיסמה שגויים.");
                account = found;
                if (role == AppRole.Manager) await OpenHome();
                else await OpenList("queue");
            }
            catch (Exception ex) { status.Text = "מספר אישי או סיסמה שגויים."; if (ex is not UnauthorizedAccessException) Error(ex); }
            finally { submit!.Enabled = true; }
        });
        stack.Controls.Add(submit);
        foreach (Control field in stack.Controls)
            field.Width = Math.Min(field.Width, 530);
        Present(stack);
        AcceptButton = submit;
        status.Text = "המערכת פועלת מקומית גם ללא חיבור לאינטרנט";
    }

    private async Task OpenList(string page, bool silent = false)
    {
        if (activeFormId is not null) await ReleaseForm();
        var user = account;
        if (user is null) { Login(); return; }
        if (role == AppRole.Mechanic) { await OpenTechnicianQueue(silent); return; }
        if (!silent) Loading(page == "issues" ? "טוען תקלות…" : "טוען הטענות מפתחות…");
        try
        {
            var version = await Work(() => store.Version(page == "issues" ? "issues" : "keyLoads"));
            var data = await Work(() => store.List(page == "issues" ? "issues" : "keyLoads", user));
            var technicians = page == "issues" ? await Work(() => database.AllTechnicians()) : new List<Database.TechnicianRecord>();
            lastListVersion = version;
            if (account != user || silent && currentPage != page) return;
            if (listSection != page) { listSection = page; statusFilter = "פתוח"; searchTerm = ""; closedOwnerId = ""; }
            currentPage = page;
            refreshTimer.Start();
            var stack = Stack();
            var heading = Section(stack, statusFilter switch
            {
                "סגור" => page == "issues" ? "תקלות סגורות" : "הטענות סגורות",
                "ממתין לסגירה" => "תקלות לסגירה",
                _ => page == "issues" ? "תקלות פתוחות" : "הטענות פתוחות"
            }, "מעקב צוות");
            Action render = () => { };
            if (role == AppRole.Manager)
            {
                var tabs = new FlowLayoutPanel { Width = 785, Height = 54, WrapContents = true,
                    FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0, 0, 0, 7) };
                var tabButtons = new List<NativeButton>();
                void AddTab(string caption, string filter)
                {
                    NativeButton? tab = null;
                    tab = (NativeButton)Button(caption, (_, _) =>
                    {
                        statusFilter = filter;
                        heading.Text = caption;
                        foreach (var button in tabButtons) { button.Active = button == tab; button.Invalidate(); }
                        render();
                    });
                    tab.Secondary = true; tab.Width = 185;
                    tab.Active = statusFilter == filter;
                    tabButtons.Add(tab); tabs.Controls.Add(tab);
                }
                if (page == "issues")
                {
                    var create = Button("פתיחת תקלה", async (_, _) => await ShowCreateFault()); create.Width = 185; tabs.Controls.Add(create);
                    AddTab("תקלות פתוחות", "פתוח");
                    AddTab("תקלות לסגירה", "ממתין לסגירה");
                    AddTab("תקלות סגורות", "סגור");
                }
                else
                {
                    var create = Button("הטענה חדשה", async (_, _) => await ShowCreateKey()); create.Width = 185; tabs.Controls.Add(create);
                    var history = Button("היסטוריית מפתחות", async (_, _) => await OpenKeyHistory()); history.Width = 185; tabs.Controls.Add(history);
                    AddTab("הטענות פתוחות", "פתוח");
                    AddTab("הטענות סגורות", "סגור");
                }
                stack.Controls.Add(tabs);
            }
            var search = Input("חפש מספר סידורי", 20);
            search.Text = searchTerm;
            stack.Controls.Add(Label(page == "issues" ? "חיפוש לפי מספר תקלה" : "חיפוש לפי מספר הטענה", 32));
            stack.Controls.Add(search);
            var ownerCaption = Label("סינון תקלות סגורות לפי טכנאי", 30);
            var ownerFilter = new NativeChoice { Width = 440, Height = 42, Margin = new Padding(0, 0, 0, 13) };
            ownerFilter.Items.Add("כל הטכנאים");
            foreach (var technician in technicians) ownerFilter.Items.Add(new PersonChoice(new Account(
                technician.Id, technician.Name, technician.PersonalNumber, "mechanic")));
            ownerFilter.SelectedIndex = Math.Max(0, technicians.FindIndex(x => x.Id == closedOwnerId) + 1);
            ownerFilter.SelectedIndexChanged += (_, _) =>
            {
                closedOwnerId = ownerFilter.SelectedItem is PersonChoice person ? person.User.Id : "";
                render();
            };
            stack.Controls.Add(ownerCaption); stack.Controls.Add(ownerFilter);
            var cards = new FlowLayoutPanel { Width = 745, Height = 450, AutoScroll = true,
                FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Color.White };
            stack.Controls.Add(cards);
            void Render()
            {
                ownerCaption.Visible = ownerFilter.Visible = page == "issues" && statusFilter == "סגור";
                cards.Controls.Clear();
                var matching = data.Where(item =>
                    (item["serial"]?.ToString() ?? item["sheet"]?["serial"]?.ToString() ?? "").Contains(search.Text.Trim(), StringComparison.OrdinalIgnoreCase) &&
                    (closedOwnerId.Length == 0 || statusFilter != "סגור" ||
                        (item["assigneeUids"] as JsonArray ?? new()).Any(x => x?.ToString() == closedOwnerId)) &&
                    (statusFilter == "הכל" || statusFilter == "פתוח" && item["status"]?.ToString() == "open" ||
                     statusFilter == "סגור" && item["status"]?.ToString() == "closed" ||
                     statusFilter == "ממתין לסגירה" && item["status"]?.ToString() == "pendingReview")).ToArray();
                cards.Height = Math.Min(450, Math.Max(88, matching.Length * 128 + 12));
                if (matching.Length == 0)
                    cards.Controls.Add(Label(search.TextLength > 0 ? "לא נמצאו טפסים למספר שחיפשת." :
                        statusFilter == "ממתין לסגירה" ? "אין תקלות שממתינות לסגירה." : "אין טפסים להצגה במצב זה.", 66));
                foreach (var item in matching)
                {
                    var id = item["id"]?.ToString() ?? "";
                    var sheet = item["sheet"] ?? item;
                    var serial = item["serial"]?.ToString() ?? sheet["serial"]?.ToString() ?? "";
                    var aircraft = sheet["aircraft"]?.ToString() ?? string.Join(", ", (item["rows"] as JsonArray ?? new()).Select(x => x?["aircraft"]?.ToString()));
                    var title = $"{(page == "issues" ? "תקלה " : "הטענת מפתחות ")}{serial}";
                    var detail = $"טייסת {V(sheet, "squadron")} · {V(sheet, "shelter")} · מטוס {aircraft}";
                    var date = V(item, "reviewedAt").Length > 0 ? V(item, "reviewedAt") :
                        V(item, "completedAt").Length > 0 ? V(item, "completedAt") : V(item, "createdAt");
                    cards.Controls.Add(FormListCard(title, detail, DisplayDate(date), V(item, "status"),
                        async (_, _) => await OpenDocument(page, id)));
                }
            }
            render = Render;
            search.TextChanged += (_, _) => { searchTerm = search.Text; Render(); };
            Render();
            Present(stack);
            status.Text = $"{data.Count} טפסים · עודכן {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception ex) { Error(ex); }
    }

    private async Task OpenTechnicianQueue(bool silent = false)
    {
        var user = account;
        if (user is null) { Login(); return; }
        if (!silent) Loading("טוען משימות צוות…");
        try
        {
            var (issues, keys, version) = await Work(() =>
                (store.List("issues", user), store.List("keyLoads", user),
                 HashCode.Combine(store.Version("issues"), store.Version("keyLoads"))));
            if (account != user || silent && currentPage != "queue") return;
            currentPage = "queue"; lastListVersion = version; refreshTimer.Start();
            var stack = Stack();
            Section(stack, "רשימת משימות", "משימות שהתקבלו");
            stack.Controls.Add(Label("בחר תקלה או הטענת מפתחות פתוחה מהרשימה כדי למלא את הטופס שלה.", 52));
            var search = Input("חפש מספר טופס", 20);
            stack.Controls.Add(search);
            var cards = new FlowLayoutPanel { Width = 745, Height = 520, AutoScroll = true,
                FlowDirection = FlowDirection.TopDown, WrapContents = false };
            stack.Controls.Add(cards);
            void Render()
            {
                cards.Controls.Clear();
                var tasks = issues.Where(x => x["status"]?.ToString() == "open")
                    .Select(x => (page: "issues", doc: x))
                    .Concat(keys.Where(x => x["status"]?.ToString() == "open").Select(x => (page: "keys", doc: x)))
                    .Where(x => (x.doc["serial"]?.ToString() ?? "").Contains(search.Text.Trim(), StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                cards.Height = Math.Min(520, Math.Max(88, tasks.Length * 128 + 12));
                if (tasks.Length == 0) cards.Controls.Add(Label("אין משימות פתוחות במכשיר הזה.", 58));
                foreach (var (page, doc) in tasks)
                {
                    var id = doc["id"]?.ToString() ?? "";
                    var serial = doc["serial"]?.ToString() ?? "";
                    var sheet = doc["sheet"] ?? doc;
                    var aircraft = page == "issues" ? sheet["aircraft"]?.ToString() ?? ""
                        : string.Join(", ", (doc["rows"] as JsonArray ?? new()).Select(x => x?["aircraft"]?.ToString()));
                    cards.Controls.Add(FormListCard($"{(page == "issues" ? "תקלה" : "הטענת מפתחות")} {serial}",
                        $"טייסת {V(sheet, "squadron")} · מטוס {aircraft} · משימה פתוחה",
                        DisplayDate(V(doc, "createdAt")), "open", async (_, _) => await OpenDocument(page, id)));
                }
            }
            search.TextChanged += (_, _) => Render();
            Render(); Present(stack);
            status.Text = $"{issues.Count + keys.Count} דפים משויכים · עודכן {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception ex) { Error(ex); }
    }

    private async Task OpenUsers()
    {
        var user = account;
        if (role != AppRole.Manager || user is null) return;
        refreshTimer.Stop();
        Loading("טוען משתמשים…");
        try
        {
            var (users, issues, keys) = await Work(() =>
                (database.AllTechnicians(), store.List("issues", user), store.List("keyLoads", user)));
            currentPage = "users";
            var stack = Stack();
            Section(stack, "ניהול משתמשים");
            stack.Controls.Add(Label($"{users.Count(x => x.Active)} טכנאים פעילים · {users.Count(x => !x.Active)} משתמשים שהוסרו", 40));
            stack.Controls.Add(Label("חיפוש לפי שם או מספר אישי", 30));
            var search = Input("חפש שם או מספר אישי", 80);
            stack.Controls.Add(search);
            var list = new FlowLayoutPanel { Width = 745, Height = Math.Min(480, Math.Max(125, users.Count * 126 + 12)),
                AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
            stack.Controls.Add(list);
            void Render()
            {
                list.Controls.Clear();
                var matching = users.Where(x => x.Name.Contains(search.Text, StringComparison.CurrentCultureIgnoreCase) || x.PersonalNumber.Contains(search.Text)).ToArray();
                if (matching.Length == 0) list.Controls.Add(Label("לא נמצאו משתמשים מתאימים.", 55));
                foreach (var person in matching)
                {
                    var assigned = issues.Where(x => (x["assigneeUids"] as JsonArray ?? new())
                        .Any(id => id?.ToString() == person.Id)).ToArray();
                    var openCount = assigned.Count(x => V(x, "status") == "open");
                    var pendingCount = assigned.Count(x => V(x, "status") == "pendingReview");
                    var closedCount = assigned.Count(x => V(x, "status") == "closed") +
                        keys.Count(x => V(x, "status") == "closed" &&
                            (x["teamUids"] as JsonArray ?? new()).Any(id => id?.ToString() == person.Id));
                    var row = new NativeCard { Width = 720, Height = 115, Margin = new Padding(0, 0, 0, 9) };
                    row.Controls.Add(new Label { Text = person.Name, Location = new Point(350, 9), Width = 350, Height = 30,
                        TextAlign = ContentAlignment.MiddleLeft, Font = NativeFonts.Create(11, FontStyle.Bold), ForeColor = Navy });
                    row.Controls.Add(new Label { Text = $"מספר אישי {person.PersonalNumber}  ·  {(person.Active ? "פעיל" : "הוסר")}",
                        Location = new Point(350, 40), Width = 350, Height = 23, TextAlign = ContentAlignment.MiddleLeft,
                        Font = NativeFonts.Create(9), ForeColor = person.Active ? Color.FromArgb(22, 139, 121) : Muted });
                    row.Controls.Add(new Label { Text = $"פתוחות {openCount} · לסגירה {pendingCount} · סגורות {closedCount}",
                        Location = new Point(335, 67), Width = 365, Height = 23, TextAlign = ContentAlignment.MiddleLeft,
                        Font = NativeFonts.Create(8.75f), ForeColor = Muted });
                    var history = (NativeButton)Button("טפסים סגורים", async (_, _) => await OpenUserHistory(person)); history.Width = 160;
                    history.Height = 38; history.Location = new Point(12, 48); history.Secondary = true; row.Controls.Add(history);
                    var active = (NativeButton)Button(person.Active ? "הסר" : "שחזר", async (_, _) =>
                    {
                        if (person.Active && openCount > 0)
                        { status.Text = $"יש לשייך מחדש {openCount} תקלות פתוחות לפני הסרת {person.Name}."; return; }
                        try { await Work(() => { database.SetTechnicianActive(person.Id, !person.Active); return true; }); await OpenUsers(); }
                        catch (Exception ex) { Error(ex); }
                    }); active.Width = 145; active.Height = 38; active.Location = new Point(180, 48);
                    active.Secondary = true; row.Controls.Add(active);
                    if (!person.Active)
                    {
                        var delete = (NativeButton)Button("מחק לצמיתות", async (_, _) =>
                        {
                        if (MessageBox.Show(this, $"למחוק לצמיתות את {person.Name}?", "מחיקה סופית", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
                        try { await Work(() => { database.DeleteTechnician(person.Id); return true; }); await OpenUsers(); }
                        catch (Exception ex) { Error(ex); }
                        }); delete.Width = 145; delete.Height = 38; delete.Location = new Point(180, 7);
                        delete.Secondary = true; delete.Danger = true; row.Controls.Add(delete);
                    }
                    list.Controls.Add(row);
                }
            }
            search.TextChanged += (_, _) => Render();
            Render();
            Section(stack, "יצירת טכנאי");
            stack.Controls.Add(Label("שם מלא", 30));
            var name = Input("שם מלא", 100); stack.Controls.Add(name);
            stack.Controls.Add(Label("מספר אישי", 30));
            var number = Input("7 ספרות", 7); stack.Controls.Add(number);
            stack.Controls.Add(Label("סיסמה ראשונית", 30));
            var password = Input("6 ספרות", 6, true); stack.Controls.Add(password);
            stack.Controls.Add(Button("צור טכנאי", async (_, _) =>
            {
                try
                {
                    if (number.Text.Length != 7 || !number.Text.All(char.IsAsciiDigit) || password.Text.Length != 6 || !password.Text.All(char.IsAsciiDigit))
                        throw new ArgumentException("מספר אישי חייב להיות בן 7 ספרות וסיסמה בת 6 ספרות.");
                    await Work(() => database.CreateAccount(name.Text, number.Text, "mechanic", password.Text));
                    await OpenUsers();
                }
                catch (Exception ex) { Error(ex); }
            }));
            Present(stack);
            status.Text = $"{users.Count} משתמשים במסד המקומי";
        }
        catch (Exception ex) { Error(ex); }
    }

    private async Task OpenUserHistory(Database.TechnicianRecord person)
    {
        if (role != AppRole.Manager || account is null) return;
        var user = account;
        Loading("טוען טפסים של טכנאי…");
        try
        {
            var (issues, keys) = await Work(() => (store.List("issues", user), store.List("keyLoads", user)));
            currentPage = "user-history";
            historyPerson = person;
            var stack = Stack();
            Section(stack, "טפסים סגורים · " + person.Name);
            var documents = issues.Where(x => V(x, "status") == "closed" &&
                    (x["assigneeUids"] as JsonArray)?.Any(id => id?.ToString() == person.Id) == true)
                .Select(x => (page: "issues", doc: x))
                .Concat(keys.Where(x => V(x, "status") == "closed" &&
                    (x["teamUids"] as JsonArray)?.Any(id => id?.ToString() == person.Id) == true)
                    .Select(x => (page: "keys", doc: x)))
                .OrderByDescending(x => V(x.doc, "completedAt")).ToArray();
            stack.Controls.Add(Label($"{documents.Length} טפסים סגורים · מספרי הטפסים נשמרים גם לאחר הסרת המשתמש.", 48));
            if (documents.Length == 0) stack.Controls.Add(Label("לא נמצאו טפסים סגורים לטכנאי הזה.", 60));
            foreach (var (page, doc) in documents)
            {
                var id = V(doc, "id");
                var sheet = doc["sheet"] ?? doc;
                var serial = V(doc, "serial").Length > 0 ? V(doc, "serial") : V(sheet, "serial");
                var aircraft = V(sheet, "aircraft");
                if (aircraft.Length == 0) aircraft = string.Join(", ", (doc["rows"] as JsonArray ?? new()).Select(x => V(x, "aircraft")));
                var completed = V(doc, "reviewedAt").Length > 0 ? V(doc, "reviewedAt") : V(doc, "completedAt");
                stack.Controls.Add(FormListCard($"{(page == "issues" ? "תקלה" : "הטענת מפתחות")} {serial}",
                    $"טייסת {V(sheet, "squadron")} · מטוס {aircraft}", DisplayDate(completed), "closed",
                    async (_, _) => await OpenDocument(page, id)));
            }
            Present(stack);
            status.Text = $"היסטוריית הטפסים של {person.Name}";
        }
        catch (Exception ex) { Error(ex); }
    }

    private async Task OpenDocument(string page, string id)
    {
        if (account is null) return;
        if (currentPage != "user-history") historyPerson = null;
        refreshTimer.Stop();
        Loading("טוען טופס…");
        try
        {
            var doc = await Work(() => store.Get(page == "issues" ? "issues" : "keyLoads", id, account));
            if (doc is null) throw new InvalidOperationException("הטופס לא נמצא במסד.");
            documentSection = page;
            currentPage = "document";
            var stack = Stack();
            var sheet = page == "issues" ? doc["sheet"] ?? doc : doc;
            Section(stack, page == "issues" ? "נתוני התקלה" : "פרטי הטענת מפתחות");
            var documentStatus = V(doc, "status");
            var summary = new Panel { Width = 745, Height = 59, BackColor = Color.White };
            summary.Controls.Add(new Label { Text = $"{(page == "issues" ? "תקלה" : "הטענת מפתחות")} {doc["serial"] ?? sheet["serial"]}",
                Location = new Point(190, 0), Size = new Size(525, 51), TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Navy, Font = NativeFonts.Create(14.25f, FontStyle.Bold) });
            summary.Controls.Add(new NativeStatusBadge { State = documentStatus,
                Text = documentStatus switch { "closed" => "סגור", "pendingReview" => "ממתין לסגירה", _ => "פתוח" },
                Location = new Point(12, 12), Size = new Size(155, 33) });
            stack.Controls.Add(summary);
            if (doc["createdAt"] is not null)
            {
                var completed = V(doc, "reviewedAt").Length > 0 ? V(doc, "reviewedAt") : V(doc, "completedAt");
                var times = $"שעת פתיחה: {DisplayDate(V(doc, "createdAt"))}";
                if (completed.Length > 0) times += $"     שעת סגירה: {DisplayDate(completed)}";
                stack.Controls.Add(Label(times, 38));
            }
            var details = new FlowLayoutPanel { Width = 780, Height = page == "issues" ? 165 : 84,
                WrapContents = true, FlowDirection = FlowDirection.LeftToRight };
            foreach (var (title, key) in new[] { ("טייסת", "squadron"), ("דת״ק", "shelter"), ("מספר מטוס", "aircraft"),
                       ("קוד מערכת", "systemCode"), ("שם מערכת", "systemName"), ("חליפת ל״א", "ewSystem") })
                if (sheet[key] is not null) details.Controls.Add(DetailTile(title, sheet[key]?.ToString() ?? ""));
            stack.Controls.Add(details);
            if (role == AppRole.Manager && sheet["fault"] is not null)
            {
                stack.Controls.Add(Label("תיאור תקלה", 35, true));
                stack.Controls.Add(Label(sheet["fault"]?.ToString() ?? "", 76));
            }
            if (role == AppRole.Manager && sheet["managerNotes"] is not null)
            {
                stack.Controls.Add(Label("הערות מנהל", 35, true));
                stack.Controls.Add(Label(sheet["managerNotes"]?.ToString() ?? "", 68));
            }
            if (page == "keys")
            {
                var rows = doc["rows"] as JsonArray ?? new();
                var grid = new DataGridView { Width = 740, Height = Math.Min(300, 45 + rows.Count * 38),
                    ReadOnly = true, RowHeadersVisible = false, AllowUserToAddRows = false,
                    AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, BackgroundColor = Color.White };
                grid.Columns.Add("aircraft", "מספר מטוס");
                grid.Columns.Add("blue", "ארז כחול");
                grid.Columns.Add("shach", "שחק");
                grid.Columns.Add("notes", "הערות");
                foreach (var row in rows)
                    grid.Rows.Add(row?["aircraft"]?.ToString(), row?["blue"]?.ToString() == "true" ? "✓" : "—",
                        row?["shach"]?.ToString() == "true" ? "✓" : "—", row?["notes"]?.ToString());
                stack.Controls.Add(grid);
            }
            await AddDocumentActions(stack, page, doc);
            if (role == AppRole.Manager)
            {
                Section(stack, "היסטוריית הטופס");
                stack.Controls.Add(Label("נפתח · " + DisplayDate(V(doc, "createdAt")), 38));
                var events = doc["reopenEvents"] as JsonArray ?? new();
                if (events.Count == 0 && V(doc, "completedAt").Length > 0)
                    stack.Controls.Add(Label("נסגר · " + DisplayDate(V(doc, "reviewedAt").Length > 0 ? V(doc, "reviewedAt") : V(doc, "completedAt")), 38));
                foreach (var entry in events)
                    stack.Controls.Add(Label("נפתח לעריכה · " + DisplayDate(V(entry, "at")), 38));
                if (events.Count > 0 && V(doc, "completedAt").Length > 0)
                    stack.Controls.Add(Label("נסגר לאחר עריכה · " + DisplayDate(V(doc, "completedAt")), 38));
            }
            Present(stack);
            status.Text = "הטופס נטען מתוך Access";
        }
        catch (Exception ex) { Error(ex); await OpenList(page); }
    }
}
