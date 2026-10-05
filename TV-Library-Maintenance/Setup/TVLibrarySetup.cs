using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using System.Reflection;
using System.Text.RegularExpressions;

[assembly: AssemblyTitle("TV Library Maintenance Setup")]
[assembly: AssemblyDescription("Configure and run read-only TV Library Analyze")]
[assembly: AssemblyCompany("Home-Server-Tools")]
[assembly: AssemblyProduct("TV Library Maintenance")]
[assembly: AssemblyVersion("0.2.3.0")]
[assembly: AssemblyFileVersion("0.2.3.0")]

namespace TVLibrarySetup
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            string scheduledConfig = null;
            for (int i = 0; i + 1 < args.Length; i++)
            {
                if (String.Equals(args[i], "--scheduled-analyze", StringComparison.OrdinalIgnoreCase))
                {
                    scheduledConfig = args[i + 1];
                    break;
                }
            }
            Application.Run(new SetupForm(scheduledConfig));
        }
    }

    internal sealed class SetupForm : Form
    {
        private readonly TextBox appRoot = new TextBox();
        private readonly TextBox homeCsv = new TextBox();
        private readonly TextBox downloads = new TextBox();
        private readonly TextBox quarantine = new TextBox();
        private readonly List<TextBox> rootPaths = new List<TextBox>();
        private Panel storageRows;
        private Button addStorage;
        private CheckBox scheduleEnabled;
        private ComboBox scheduleDay;
        private DateTimePicker scheduleTime;
        private bool weeklySchedulePreviouslyEnabled;
        private readonly string scheduledConfigPath;
        private readonly NumericUpDown stability = new NumericUpDown();
        private readonly NumericUpDown rebalance = new NumericUpDown();
        private readonly NumericUpDown minFree = new NumericUpDown();
        private readonly Label status = new Label();
        private readonly Button save = new Button();
        private readonly Button analyze = new Button();
        private string savedConfigPath;

        private static readonly string DefaultHome = @"\\YourServer\TV Shows\_Home locations\TV_Home_Locations.csv";
        private static readonly string[] DefaultRoots = new string[] {
            @"\\YourServer\TV Shows",
            @"\\YourServer\TV Shows 2",
            @"\\YourServer\TV Shows 3"
        };

        public SetupForm(string scheduledConfigPath)
        {
            this.scheduledConfigPath = scheduledConfigPath;
            Text = "TV Library Maintenance Setup";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(760, 720);
            Size = new Size(850, 790);
            Font = new Font("Segoe UI", 9F);
            BackColor = Color.White;

            Panel page = new Panel();
            page.Dock = DockStyle.Fill;
            page.AutoScroll = true;
            Controls.Add(page);

            int y = 22;
            Label title = new Label();
            title.Text = "Set up TV Library Analyze";
            title.Font = new Font("Segoe UI Semibold", 19F, FontStyle.Bold);
            title.Location = new Point(28, y);
            title.Size = new Size(760, 38);
            page.Controls.Add(title);
            y += 44;

            Label intro = new Label();
            intro.Text = "Choose your folders and save your settings. Analyze only reads your library and downloads; it does not move or change media files.";
            intro.Location = new Point(30, y);
            intro.Size = new Size(770, 42);
            page.Controls.Add(intro);
            y += 56;

            AddSection(page, "1. Find the program folder", ref y);
            AddBrowseRow(page, "Program folder", appRoot, ref y, BrowseKind.ToolFolder);
            AddHint(page, "Choose the Home-Server-Tools folder from the download. It contains the TV library program. Your settings will be saved here so the program remembers them next time.", ref y);

            AddSection(page, "2. Your TV folders", ref y);
            AddBrowseRow(page, "Home locations CSV", homeCsv, ref y, BrowseKind.CsvFile);
            AddBrowseRow(page, "Downloads folder", downloads, ref y, BrowseKind.Folder);
            AddBrowseRow(page, "Review folder (optional)", quarantine, ref y, BrowseKind.Folder);
            AddHint(page, "The Home CSV tells the tool which storage folder belongs to each show. The review folder is skipped during Analyze.", ref y);

            AddSection(page, "3. Storage locations", ref y);
            storageRows = new Panel();
            storageRows.Location = new Point(16, y);
            storageRows.Size = new Size(800, 35);
            page.Controls.Add(storageRows);
            SetStoragePaths(new List<string> { DefaultRoots[0] });
            addStorage = new Button();
            addStorage.Text = "+ Add another location";
            addStorage.Location = new Point(30, y + storageRows.Height + 3);
            addStorage.Size = new Size(190, 29);
            addStorage.Click += AddStorageClicked;
            page.Controls.Add(addStorage);
            y += storageRows.Height + 36;
            AddHint(page, "Add every folder where your TV shows are stored. Start with one; use + to add another.", ref y);

            AddSection(page, "4. Analyze settings", ref y);
            AddNumberRow(page, "Treat files changed within this many minutes as recent", stability, 0, 10080, 10, "minutes", ref y);
            AddNumberRow(page, "Flag a storage usage difference above", rebalance, 0, 100, 10, "%", ref y);
            AddNumberRow(page, "Flag storage with less than", minFree, 0, 1000000, 500, "GB free", ref y);

            AddSection(page, "5. Optional weekly schedule", ref y);
            scheduleEnabled = new CheckBox();
            scheduleEnabled.Text = "Run Analyze automatically once a week";
            scheduleEnabled.Location = new Point(34, y);
            scheduleEnabled.Size = new Size(355, 28);
            scheduleEnabled.CheckedChanged += ScheduleEnabledChanged;
            page.Controls.Add(scheduleEnabled);
            y += 34;
            Label dayLabel = new Label();
            dayLabel.Text = "Day";
            dayLabel.Location = new Point(34, y + 5);
            dayLabel.Size = new Size(45, 24);
            page.Controls.Add(dayLabel);
            scheduleDay = new ComboBox();
            scheduleDay.DropDownStyle = ComboBoxStyle.DropDownList;
            scheduleDay.Items.AddRange(new object[] { "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday" });
            scheduleDay.SelectedItem = "Sunday";
            scheduleDay.Location = new Point(82, y);
            scheduleDay.Size = new Size(150, 26);
            page.Controls.Add(scheduleDay);
            Label timeLabel = new Label();
            timeLabel.Text = "Time";
            timeLabel.Location = new Point(260, y + 5);
            timeLabel.Size = new Size(48, 24);
            page.Controls.Add(timeLabel);
            scheduleTime = new DateTimePicker();
            scheduleTime.Format = DateTimePickerFormat.Time;
            scheduleTime.ShowUpDown = true;
            scheduleTime.CustomFormat = "h:mm tt";
            scheduleTime.Value = DateTime.Today.AddHours(20);
            scheduleTime.Location = new Point(310, y);
            scheduleTime.Size = new Size(125, 26);
            page.Controls.Add(scheduleTime);
            y += 40;
            SetScheduleControlsEnabled(false);
            AddHint(page, "Choose one day and time. The scheduled check opens on screen while you are signed in to Windows; it does not save a report file.", ref y);

            homeCsv.Text = DefaultHome;
            downloads.Text = @"C:\TV-Downloads";
            quarantine.Text = @"C:\TV-Downloads\Needs Review";

            save.Text = "Save settings";
            save.Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold);
            save.Size = new Size(150, 38);
            save.Location = new Point(30, y + 12);
            save.Click += SaveClicked;
            page.Controls.Add(save);

            analyze.Text = "Run Analyze";
            analyze.Size = new Size(135, 38);
            analyze.Location = new Point(190, y + 12);
            analyze.Enabled = false;
            analyze.Click += AnalyzeClicked;
            page.Controls.Add(analyze);

            status.Text = "Settings are saved only when you click Save settings.";
            status.Location = new Point(340, y + 19);
            status.Size = new Size(450, 34);
            status.ForeColor = Color.FromArgb(75, 75, 75);
            page.Controls.Add(status);

            Shown += delegate
            {
                LoadSavedSettings();
                if (!String.IsNullOrWhiteSpace(this.scheduledConfigPath))
                {
                    if (!File.Exists(this.scheduledConfigPath))
                    {
                        MessageBox.Show(this, "The saved settings file could not be found. Open TV Library Setup and save your settings again.", "Analyze could not run", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        Close();
                        return;
                    }
                    savedConfigPath = this.scheduledConfigPath;
                    Hide();
                    AnalyzeClicked(this, EventArgs.Empty);
                    Close();
                }
            };
        }

        private enum BrowseKind { Folder, CsvFile, ToolFolder }

        private static void AddSection(Control parent, string text, ref int y)
        {
            Label label = new Label();
            label.Text = text;
            label.Font = new Font("Segoe UI Semibold", 11F, FontStyle.Bold);
            label.Location = new Point(30, y);
            label.Size = new Size(740, 27);
            parent.Controls.Add(label);
            y += 34;
        }

        private void AddBrowseRow(Control parent, string caption, TextBox box, ref int y, BrowseKind kind)
        {
            Label label = new Label();
            label.Text = caption;
            label.Location = new Point(34, y + 4);
            label.Size = new Size(190, 24);
            parent.Controls.Add(label);

            box.Location = new Point(228, y);
            box.Size = new Size(485, 25);
            box.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            parent.Controls.Add(box);

            Button browse = new Button();
            browse.Text = "Browse...";
            browse.Location = new Point(722, y - 1);
            browse.Size = new Size(80, 28);
            browse.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            browse.Click += delegate { Browse(box, kind); };
            parent.Controls.Add(browse);
            y += 35;
        }

        private static void AddHint(Control parent, string text, ref int y)
        {
            Label hint = new Label();
            hint.Text = text;
            hint.ForeColor = Color.FromArgb(90, 90, 90);
            hint.Location = new Point(34, y);
            hint.Size = new Size(755, 32);
            parent.Controls.Add(hint);
            y += 37;
        }

        private static void AddNumberRow(Control parent, string caption, NumericUpDown number, decimal minimum, decimal maximum, decimal value, string unit, ref int y)
        {
            Label label = new Label();
            label.Text = caption;
            label.Location = new Point(34, y + 4);
            label.Size = new Size(500, 24);
            parent.Controls.Add(label);
            number.Minimum = minimum;
            number.Maximum = maximum;
            number.Value = value;
            number.Location = new Point(545, y);
            number.Size = new Size(105, 26);
            parent.Controls.Add(number);
            Label suffix = new Label();
            suffix.Text = unit;
            suffix.Location = new Point(660, y + 4);
            suffix.Size = new Size(120, 24);
            parent.Controls.Add(suffix);
            y += 34;
        }

        private void AddStorageClicked(object sender, EventArgs args)
        {
            List<string> paths = new List<string>();
            foreach (TextBox box in rootPaths) paths.Add(box.Text);
            paths.Add(String.Empty);
            SetStoragePaths(paths);
        }

        private void RemoveStorageClicked(int index)
        {
            if (rootPaths.Count <= 1) return;
            List<string> paths = new List<string>();
            foreach (TextBox box in rootPaths) paths.Add(box.Text);
            paths.RemoveAt(index);
            SetStoragePaths(paths);
        }

        private void SetStoragePaths(List<string> paths)
        {
            if (paths == null || paths.Count == 0) paths = new List<string> { DefaultRoots[0] };
            int oldHeight = rootPaths.Count * 35;
            int newHeight = paths.Count * 35;
            if (storageRows != null && addStorage != null && newHeight != oldHeight)
            {
                int boundary = storageRows.Top + oldHeight;
                int shift = newHeight - oldHeight;
                foreach (Control control in storageRows.Parent.Controls)
                {
                    if (control != storageRows && control.Top >= boundary) control.Top += shift;
                }
            }
            foreach (Control control in storageRows.Controls) control.Dispose();
            storageRows.Controls.Clear();
            rootPaths.Clear();
            storageRows.Height = Math.Max(35, newHeight);
            for (int i = 0; i < paths.Count; i++)
            {
                Panel row = new Panel();
                row.Location = new Point(0, i * 35);
                row.Size = new Size(800, 35);

                Label label = new Label();
                label.Text = "Library folder " + (i + 1);
                label.Location = new Point(0, 6);
                label.Size = new Size(175, 23);
                row.Controls.Add(label);

                TextBox box = new TextBox();
                box.Text = paths[i];
                box.Location = new Point(190, 1);
                box.Size = new Size(465, 25);
                box.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
                row.Controls.Add(box);
                rootPaths.Add(box);

                Button browse = new Button();
                browse.Text = "Browse...";
                browse.Location = new Point(660, 0);
                browse.Size = new Size(82, 28);
                browse.Click += delegate { Browse(box, BrowseKind.Folder); };
                row.Controls.Add(browse);

                Button remove = new Button();
                remove.Text = "−";
                remove.AccessibleName = "Remove this storage location";
                remove.Enabled = paths.Count > 1;
                remove.Location = new Point(747, 0);
                remove.Size = new Size(34, 28);
                int selectedIndex = i;
                remove.Click += delegate { RemoveStorageClicked(selectedIndex); };
                row.Controls.Add(remove);

                storageRows.Controls.Add(row);
            }
            if (addStorage != null) addStorage.Top = storageRows.Top + newHeight + 3;
        }

        private void ScheduleEnabledChanged(object sender, EventArgs args)
        {
            SetScheduleControlsEnabled(scheduleEnabled.Checked);
        }

        private void SetScheduleControlsEnabled(bool enabled)
        {
            if (scheduleDay != null) scheduleDay.Enabled = enabled;
            if (scheduleTime != null) scheduleTime.Enabled = enabled;
        }

        private static string PowerShellLiteral(string value)
        {
            return "'" + value.Replace("'", "''") + "'";
        }

        private static string RunPowerShell(string command)
        {
            ProcessStartInfo start = new ProcessStartInfo();
            start.FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe");
            start.Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(command));
            start.UseShellExecute = false;
            start.CreateNoWindow = true;
            start.RedirectStandardOutput = true;
            start.RedirectStandardError = true;
            using (Process process = Process.Start(start))
            {
                string stdout = process.StandardOutput.ReadToEnd();
                string stderr = process.StandardError.ReadToEnd();
                process.WaitForExit();
                if (process.ExitCode != 0) throw new InvalidOperationException(String.IsNullOrWhiteSpace(stderr) ? stdout : stderr);
                return stdout.Trim();
            }
        }

        private string ConfigureWeeklySchedule(string configPath)
        {
            string taskName = "TV Library Maintenance - Analyze";
            if (!scheduleEnabled.Checked)
            {
                if (weeklySchedulePreviouslyEnabled)
                    RunPowerShell("Unregister-ScheduledTask -TaskName " + PowerShellLiteral(taskName) + " -Confirm:$false -ErrorAction SilentlyContinue");
                weeklySchedulePreviouslyEnabled = false;
                return "Automatic weekly Analyze is off.";
            }

            string toolPath = appRoot.Text.Trim();
            string stableExe = Path.Combine(toolPath, "TV-Library-Maintenance", "Setup", "TVLibrarySetup.exe");
            if (!String.Equals(Path.GetFullPath(Application.ExecutablePath), Path.GetFullPath(stableExe), StringComparison.OrdinalIgnoreCase))
                File.Copy(Application.ExecutablePath, stableExe, true);

            string day = Convert.ToString(scheduleDay.SelectedItem);
            string hour = scheduleTime.Value.Hour.ToString(CultureInfo.InvariantCulture);
            string minute = scheduleTime.Value.Minute.ToString(CultureInfo.InvariantCulture);
            string action = "$a = New-ScheduledTaskAction -Execute " + PowerShellLiteral(stableExe) + " -Argument ('--scheduled-analyze ' + [char]34 + " + PowerShellLiteral(configPath) + " + [char]34); ";
            string trigger = "$t = New-ScheduledTaskTrigger -Weekly -DaysOfWeek " + day + " -At ([datetime]::Today.AddHours(" + hour + ").AddMinutes(" + minute + ")); ";
            string principal = "$p = New-ScheduledTaskPrincipal -UserId ([Security.Principal.WindowsIdentity]::GetCurrent().Name) -LogonType Interactive -RunLevel Limited; ";
            string settings = "$s = New-ScheduledTaskSettingsSet -StartWhenAvailable -MultipleInstances IgnoreNew; ";
            string register = "Register-ScheduledTask -TaskName " + PowerShellLiteral(taskName) + " -Action $a -Trigger $t -Principal $p -Settings $s -Description 'Runs read-only TV library Analyze weekly.' -Force | Out-Null; 'Scheduled for " + day + " at " + scheduleTime.Value.ToString("h:mm tt", CultureInfo.CurrentCulture) + ".'";
            string result = RunPowerShell(action + trigger + principal + settings + register);
            weeklySchedulePreviouslyEnabled = true;
            return result;
        }

        private void Browse(TextBox target, BrowseKind kind)
        {
            if (kind == BrowseKind.CsvFile)
            {
                using (OpenFileDialog dialog = new OpenFileDialog())
                {
                    dialog.Title = "Choose the TV Home locations CSV";
                    dialog.Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*";
                    dialog.FileName = target.Text;
                    if (dialog.ShowDialog(this) == DialogResult.OK) target.Text = dialog.FileName;
                }
                return;
            }

            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = kind == BrowseKind.ToolFolder
                    ? "Select the extracted Home-Server-Tools folder"
                    : "Choose a folder";
                dialog.ShowNewFolderButton = false;
                if (Directory.Exists(target.Text)) dialog.SelectedPath = target.Text;
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    target.Text = dialog.SelectedPath;
                    if (kind == BrowseKind.ToolFolder)
                    {
                        string config = Path.Combine(dialog.SelectedPath, "TV-Library-Maintenance", "Config", "config.json");
                        if (File.Exists(config)) LoadConfig(config);
                    }
                }
            }
        }

        private void LoadSavedSettings()
        {
            string root = Path.GetDirectoryName(Application.ExecutablePath);
            string[] candidates = new string[] {
                Path.Combine(root, "Home-Server-Tools"),
                Path.GetFullPath(Path.Combine(root, "..")),
                root
            };
            foreach (string candidate in candidates)
            {
                if (File.Exists(Path.Combine(candidate, "TV-Library-Maintenance", "Invoke-TVLibraryMaintenance.ps1")))
                {
                    appRoot.Text = candidate;
                    string config = Path.Combine(candidate, "TV-Library-Maintenance", "Config", "config.json");
                    if (File.Exists(config)) LoadConfig(config);
                    break;
                }
            }
        }

        private void LoadConfig(string path)
        {
            try
            {
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                string json = NormalizeWindowsPathEscapes(File.ReadAllText(path));
                Dictionary<string, object> config = serializer.Deserialize<Dictionary<string, object>>(json);
                SetText(config, "HomeMapPath", homeCsv);
                SetText(config, "DownloadsPath", downloads);
                SetText(config, "QuarantinePath", quarantine);
                SetNumber(config, "FileStabilityMinutes", stability);
                SetNumber(config, "RebalanceThresholdPercent", rebalance);
                SetNumber(config, "MinimumFreeSpaceGB", minFree);
                if (config.ContainsKey("ScheduleEnabled"))
                {
                    weeklySchedulePreviouslyEnabled = Convert.ToBoolean(config["ScheduleEnabled"]);
                    scheduleEnabled.Checked = weeklySchedulePreviouslyEnabled;
                }
                if (config.ContainsKey("AnalyzeDay"))
                {
                    string day = Convert.ToString(config["AnalyzeDay"]);
                    if (scheduleDay.Items.Contains(day)) scheduleDay.SelectedItem = day;
                }
                if (config.ContainsKey("AnalyzeTime"))
                {
                    DateTime time;
                    if (DateTime.TryParseExact(Convert.ToString(config["AnalyzeTime"]), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out time)) scheduleTime.Value = DateTime.Today.Add(time.TimeOfDay);
                }
                IList roots = config.ContainsKey("StorageRoots") ? config["StorageRoots"] as IList : null;
                if (roots != null)
                {
                    List<string> paths = new List<string>();
                    for (int i = 0; i < roots.Count; i++)
                    {
                        Dictionary<string, object> item = roots[i] as Dictionary<string, object>;
                        if (item != null && item.ContainsKey("Path")) paths.Add(Convert.ToString(item["Path"]));
                    }
                    SetStoragePaths(paths);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "The existing settings could not be loaded. Review the paths in this window and click Save settings to replace the file with a valid configuration.\r\n\r\n" + ex.Message, "Settings not loaded", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private static string NormalizeWindowsPathEscapes(string json)
        {
            // Repair older Windows JSON files that wrote Windows paths with single backslashes.
            Regex pathProperty = new Regex("(?<prefix>\\\"(?:HomeMapPath|DownloadsPath|QuarantinePath|Path)\\\"\\s*:\\s*\\\")(?<value>(?:\\\\.|[^\\\"\\\\])*)(?<suffix>\\\")");
            return pathProperty.Replace(json, delegate(Match match)
            {
                string value = match.Groups["value"].Value;
                StringBuilder repaired = new StringBuilder(value.Length + 16);
                for (int i = 0; i < value.Length; )
                {
                    if (value[i] != '\\')
                    {
                        repaired.Append(value[i++]);
                        continue;
                    }
                    int start = i;
                    while (i < value.Length && value[i] == '\\') i++;
                    int count = i - start;
                    repaired.Append('\\', count % 2 == 0 ? count : count + 1);
                }
                return match.Groups["prefix"].Value + repaired.ToString() + match.Groups["suffix"].Value;
            });
        }

        private static void SetText(Dictionary<string, object> config, string key, TextBox target)
        {
            if (config.ContainsKey(key) && config[key] != null) target.Text = Convert.ToString(config[key]);
        }

        private static void SetNumber(Dictionary<string, object> config, string key, NumericUpDown target)
        {
            decimal value;
            if (config.ContainsKey(key) && Decimal.TryParse(Convert.ToString(config[key]), out value))
                target.Value = Math.Min(target.Maximum, Math.Max(target.Minimum, value));
        }

        private void SaveClicked(object sender, EventArgs args)
        {
            string toolPath = appRoot.Text.Trim();
            string entry = Path.Combine(toolPath, "TV-Library-Maintenance", "Invoke-TVLibraryMaintenance.ps1");
            if (!File.Exists(entry))
            {
                MessageBox.Show(this, "Choose the Home-Server-Tools folder from the download. It contains the TV library program. If you opened the download as a ZIP file, extract it first, then choose the Home-Server-Tools folder that appears.", "Program folder not found", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (!File.Exists(homeCsv.Text.Trim()))
            {
                MessageBox.Show(this, "I can't find the Home locations CSV at that path. Use Browse to select TV_Home_Locations.csv.", "Home CSV not found", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (!Directory.Exists(downloads.Text.Trim()))
            {
                MessageBox.Show(this, "I can't find the downloads folder. Use Browse to select the folder where completed TV downloads appear.", "Downloads folder not found", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (String.IsNullOrWhiteSpace(quarantine.Text))
                quarantine.Text = Path.Combine(downloads.Text.Trim(), "Needs Review");

            List<Dictionary<string, object>> roots = new List<Dictionary<string, object>>();
            for (int i = 0; i < rootPaths.Count; i++)
            {
                string path = rootPaths[i].Text.Trim();
                if (String.IsNullOrWhiteSpace(path))
                {
                    MessageBox.Show(this, "Choose a folder for every storage location, or remove any extra blank locations with the minus button.", "Storage folder required", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                roots.Add(new Dictionary<string, object> { { "Name", "TV" + (i + 1) }, { "Path", path } });
            }
            if (roots.Count == 0)
            {
                MessageBox.Show(this, "Enter at least one TV storage folder.", "Storage folder required", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Dictionary<string, object> config = new Dictionary<string, object>();
            config["Mode"] = "Analyze";
            config["DownloadsPath"] = downloads.Text.Trim();
            config["QuarantinePath"] = quarantine.Text.Trim();
            config["HomeMapPath"] = homeCsv.Text.Trim();
            config["StorageRoots"] = roots;
            config["FileStabilityMinutes"] = Decimal.ToInt32(stability.Value);
            config["RebalanceThresholdPercent"] = Decimal.ToInt32(rebalance.Value);
            config["MinimumFreeSpaceGB"] = Decimal.ToInt32(minFree.Value);
            config["ScheduleEnabled"] = scheduleEnabled.Checked;
            config["AnalyzeDay"] = Convert.ToString(scheduleDay.SelectedItem);
            config["AnalyzeTime"] = scheduleTime.Value.ToString("HH:mm", CultureInfo.InvariantCulture);

            string configPath = Path.Combine(toolPath, "TV-Library-Maintenance", "Config", "config.json");
            if (!Directory.Exists(Path.GetDirectoryName(configPath)))
            {
                MessageBox.Show(this, "The Config folder is missing from the selected tool folder.", "Tool folder incomplete", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            if (File.Exists(configPath) && MessageBox.Show(this, "Replace the existing TV Library settings?\r\n\r\n" + configPath, "Confirm settings change", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            try
            {
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                File.WriteAllText(configPath, serializer.Serialize(config) + Environment.NewLine, new UTF8Encoding(false));
                savedConfigPath = configPath;
                analyze.Enabled = true;
                string scheduleMessage = ConfigureWeeklySchedule(configPath);
                status.Text = "Settings saved. " + scheduleMessage;
                status.ForeColor = Color.FromArgb(0, 110, 65);
                MessageBox.Show(this, "Your settings have been saved.\r\n\r\n" + scheduleMessage + "\r\n\r\n" + (scheduleEnabled.Checked ? "The scheduled run appears on screen when you are signed in to Windows. Keep the Home-Server-Tools folder in the same place." : "") + "\r\n\r\nAnalyze only looks at your files and folders; it does not move or change your media.\r\n\r\nSaved to:\r\n" + configPath, "Setup complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Your settings file was saved, but the weekly schedule could not be updated.\r\n\r\n" + ex.Message + "\r\n\r\nTry Save settings again, or turn off the weekly schedule and save.", "Schedule not updated", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void AnalyzeClicked(object sender, EventArgs args)
        {
            if (String.IsNullOrEmpty(savedConfigPath) || !File.Exists(savedConfigPath)) return;
            Form results = new Form();
            results.Text = "TV Library Analyze Results";
            results.StartPosition = FormStartPosition.CenterParent;
            results.Size = new Size(780, 520);
            results.MinimizeBox = false;
            TextBox output = new TextBox();
            output.Multiline = true;
            output.ReadOnly = true;
            output.ScrollBars = ScrollBars.Both;
            output.WordWrap = false;
            output.Dock = DockStyle.Fill;
            output.Font = new Font("Consolas", 9F);
            output.Text = "Running read-only Analyze...\r\n";
            results.Controls.Add(output);

            string modulePath = Path.Combine(Path.GetDirectoryName(savedConfigPath), "..", "TVLibraryMaintenance.psm1");
            modulePath = Path.GetFullPath(modulePath);
            string quotedModule = "'" + modulePath.Replace("'", "''") + "'";
            string quotedConfig = "'" + savedConfigPath.Replace("'", "''") + "'";
            string command = "$ErrorActionPreference = 'Stop'; $WarningPreference = 'SilentlyContinue'; $ProgressPreference = 'SilentlyContinue'; try { Import-Module -Name " + quotedModule + " -Force -ErrorAction Stop; $cfg = Get-Content -LiteralPath " + quotedConfig + " -Raw | ConvertFrom-Json; $r = Invoke-TVLibraryAnalysis -Config $cfg; 'HOME MAP'; $r.HomeSummary | Format-Table -AutoSize | Out-String; 'DOWNLOAD RESULTS'; $r.Summary | Format-Table -AutoSize | Out-String; 'STORAGE'; $r.Storage.Stats | Select-Object Name,Reachable,CapacityAvailable,UsedPercent,FreeGB,Error | Format-Table -AutoSize | Out-String; 'ITEMS FOR REVIEW'; $r.Downloads | Where-Object { $_.Status -ne 'DESTINATION_EPISODE_PRESENT' } | Select-Object Status,ShowName,EpisodeCode,SourcePath,Detail | Format-Table -Wrap -AutoSize | Out-String; exit 0 } catch { [Console]::Error.WriteLine($_.Exception.ToString()); exit 1 }";
            string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(command));
            ProcessStartInfo start = new ProcessStartInfo();
            start.FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe");
            start.Arguments = "-NoProfile -ExecutionPolicy Bypass -EncodedCommand " + encoded;
            start.UseShellExecute = false;
            start.CreateNoWindow = true;
            start.RedirectStandardOutput = true;
            start.RedirectStandardError = true;
            Process worker = new Process();
            worker.StartInfo = start;
            worker.EnableRaisingEvents = true;
            worker.OutputDataReceived += delegate(object source, DataReceivedEventArgs line)
            {
                if (!String.IsNullOrEmpty(line.Data)) results.BeginInvoke((Action)delegate { output.AppendText(line.Data + Environment.NewLine); });
            };
            bool suppressingClixml = false;
            worker.ErrorDataReceived += delegate(object source, DataReceivedEventArgs line)
            {
                if (String.IsNullOrEmpty(line.Data)) return;
                if (suppressingClixml)
                {
                    if (line.Data.IndexOf("</Objs>", StringComparison.OrdinalIgnoreCase) >= 0) suppressingClixml = false;
                    return;
                }
                if (line.Data.IndexOf("#< CLIXML", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    suppressingClixml = line.Data.IndexOf("</Objs>", StringComparison.OrdinalIgnoreCase) < 0;
                    return;
                }
                results.BeginInvoke((Action)delegate { output.AppendText("ERROR: " + line.Data + Environment.NewLine); });
            };
            worker.Exited += delegate
            {
                int code = worker.ExitCode;
                results.BeginInvoke((Action)delegate { output.AppendText(code == 0 ? "Analyze complete. No media files were changed.\r\n" : "Analyze stopped with errors (exit code " + code + ").\r\n"); });
            };
            try
            {
                results.Shown += delegate
                {
                    try
                    {
                        worker.Start();
                        worker.BeginOutputReadLine();
                        worker.BeginErrorReadLine();
                    }
                    catch (Exception ex) { output.AppendText("Could not start Analyze: " + ex.Message + "\r\n"); }
                };
                results.FormClosed += delegate { try { if (!worker.HasExited) worker.Kill(); } catch { } worker.Dispose(); };
                results.ShowDialog(this);
            }
            catch (Exception ex)
            {
                worker.Dispose();
                MessageBox.Show(this, "Analyze could not start.\r\n\r\n" + ex.Message, "Analyze error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}





