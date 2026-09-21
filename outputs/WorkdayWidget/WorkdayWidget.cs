using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

internal static class Program
{
    private const int WorkHours = 8;
    private const int BreakHours = 1;
    private static readonly string DataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Workday Widget");
    private static readonly string StatePath = Path.Combine(DataDirectory, "today.txt");
    private static readonly string HistoryPath = Path.Combine(DataDirectory, "workday-history.csv");

    [STAThread]
    private static void Main()
    {
        bool createdNew;
        using (var mutex = new Mutex(true, "WorkdayWidget.SingleInstance", out createdNew))
        {
            if (!createdNew) return;
            Directory.CreateDirectory(DataDirectory);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new WidgetForm());
        }
    }

    private sealed class WidgetForm : Form
    {
        private readonly Label clockInLabel;
        private readonly Label finishLabel;
        private readonly Label remainingLabel;
        private readonly Button clockButton;
        private readonly System.Windows.Forms.Timer timer;

        public WidgetForm()
        {
            Text = "Workday Widget";
            Size = new Size(310, 225);
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            MaximizeBox = false;
            MinimizeBox = true;
            TopMost = true;
            BackColor = Color.FromArgb(30, 33, 39);
            ForeColor = Color.White;
            StartPosition = FormStartPosition.Manual;
            var area = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(area.Right - Width - 24, 36);

            Controls.Add(Label("WORKDAY", 18, 14, 260, 24, 11, FontStyle.Bold));
            var subtitle = Label("8 hours + 1 hour break", 18, 38, 260, 20, 9, FontStyle.Regular);
            subtitle.ForeColor = Color.FromArgb(177, 190, 205);
            Controls.Add(subtitle);

            clockInLabel = Label("", 18, 70, 270, 25, 12, FontStyle.Regular);
            finishLabel = Label("", 18, 97, 270, 25, 12, FontStyle.Regular);
            remainingLabel = Label("", 18, 125, 270, 29, 15, FontStyle.Bold);
            Controls.Add(clockInLabel);
            Controls.Add(finishLabel);
            Controls.Add(remainingLabel);

            clockButton = new Button
            {
                Location = new Point(18, 166), Size = new Size(132, 29),
                FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(65, 132, 235),
                ForeColor = Color.White, Font = new Font("Segoe UI", 9, FontStyle.Bold)
            };
            clockButton.FlatAppearance.BorderSize = 0;
            clockButton.Click += ClockButton_Click;
            Controls.Add(clockButton);

            var history = new Button
            {
                Text = "Open History", Location = new Point(160, 166), Size = new Size(132, 29),
                FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(46, 50, 58),
                ForeColor = Color.White, Font = new Font("Segoe UI", 9, FontStyle.Regular)
            };
            history.FlatAppearance.BorderColor = Color.FromArgb(95, 105, 120);
            history.Click += (sender, args) => OpenHistory();
            Controls.Add(history);

            timer = new System.Windows.Forms.Timer { Interval = 1000 };
            timer.Tick += (sender, args) => RefreshWidget();
            Shown += (sender, args) => { RefreshWidget(); timer.Start(); };
            FormClosed += (sender, args) => timer.Stop();
        }

        private static Label Label(string text, int x, int y, int width, int height, float size, FontStyle style)
        {
            return new Label
            {
                Text = text, Location = new Point(x, y), Size = new Size(width, height),
                Font = new Font("Segoe UI", size, style), ForeColor = Color.White, BackColor = Color.Transparent
            };
        }

        private void ClockButton_Click(object sender, EventArgs args)
        {
            var clockIn = ReadToday();
            if (clockIn == null)
            {
                SaveToday(DateTime.Now);
                AddHistory(DateTime.Now);
            }
            else EditClockIn(clockIn.Value);
            RefreshWidget();
        }

        private void RefreshWidget()
        {
            var clockIn = ReadToday();
            if (clockIn == null)
            {
                clockInLabel.Text = "Not clocked in yet";
                finishLabel.Text = "Expected finish: -";
                remainingLabel.Text = "Press Clock In Now to begin";
                remainingLabel.ForeColor = Color.FromArgb(177, 190, 205);
                clockButton.Text = "Clock In Now";
                return;
            }
            var finish = clockIn.Value.AddHours(WorkHours + BreakHours);
            var remaining = finish - DateTime.Now;
            clockInLabel.Text = "Clocked in: " + clockIn.Value.ToString("HH:mm");
            finishLabel.Text = "Expected finish: " + finish.ToString("HH:mm");
            clockButton.Text = "Edit Clock-in";
            remainingLabel.ForeColor = Color.FromArgb(109, 220, 160);
            remainingLabel.Text = remaining.TotalSeconds > 0
                ? "Time left: " + remaining.ToString(@"hh\:mm\:ss")
                : "Workday complete";
        }

        private static DateTime? ReadToday()
        {
            if (!File.Exists(StatePath)) return null;
            try
            {
                var parts = File.ReadAllText(StatePath).Split('|');
                if (parts.Length != 2 || parts[0] != DateTime.Today.ToString("yyyy-MM-dd")) return null;
                return DateTime.Parse(parts[1], null, DateTimeStyles.RoundtripKind);
            }
            catch { return null; }
        }

        private static void SaveToday(DateTime clockIn)
        {
            File.WriteAllText(StatePath, clockIn.ToString("yyyy-MM-dd") + "|" + clockIn.ToString("o"));
        }

        private static void AddHistory(DateTime clockIn)
        {
            var date = clockIn.ToString("yyyy-MM-dd");
            var existing = File.Exists(HistoryPath)
                ? File.ReadAllLines(HistoryPath).Where(line => !line.StartsWith(date + ",")).ToArray()
                : new[] { "Date,Clock In,Expected Finish" };
            File.WriteAllLines(HistoryPath, existing.Concat(new[]
            {
                date + "," + clockIn.ToString("HH:mm") + "," + clockIn.AddHours(WorkHours + BreakHours).ToString("HH:mm")
            }));
        }

        private static void EditClockIn(DateTime currentClockIn)
        {
            using (var edit = new Form())
            {
                edit.Text = "Edit clock-in";
                edit.Size = new Size(280, 155);
                edit.FormBorderStyle = FormBorderStyle.FixedDialog;
                edit.MaximizeBox = false;
                edit.MinimizeBox = false;
                edit.StartPosition = FormStartPosition.CenterScreen;

                var prompt = new Label { Text = "Clock-in time for today", Location = new Point(20, 18), Size = new Size(220, 20) };
                var time = new DateTimePicker
                {
                    Value = currentClockIn, Format = DateTimePickerFormat.Custom,
                    CustomFormat = "HH:mm", ShowUpDown = true,
                    Location = new Point(20, 43), Size = new Size(105, 25)
                };
                var save = new Button { Text = "Save", DialogResult = DialogResult.OK, Location = new Point(75, 80), Size = new Size(85, 28) };
                var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(165, 80), Size = new Size(85, 28) };
                edit.Controls.AddRange(new Control[] { prompt, time, save, cancel });
                edit.AcceptButton = save;
                edit.CancelButton = cancel;

                if (edit.ShowDialog() == DialogResult.OK)
                {
                    var updated = DateTime.Today.Add(time.Value.TimeOfDay);
                    SaveToday(updated);
                    AddHistory(updated);
                }
            }
        }

        private static void OpenHistory()
        {
            if (!File.Exists(HistoryPath))
            {
                MessageBox.Show("No history yet.", "Workday Widget");
                return;
            }
            Process.Start(new ProcessStartInfo(HistoryPath) { UseShellExecute = true });
        }
    }
}
