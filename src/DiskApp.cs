using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

[assembly: AssemblyTitle("Crecimiento del disco")]
[assembly: AssemblyProduct("Crecimiento del disco")]
[assembly: AssemblyVersion("1.8.0.0")]

namespace DiskGrowth
{
    internal static class Program
    {
        internal static readonly string DataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CrecimientoDisco");

        [STAThread]
        private static void Main()
        {
            AppContext.SetSwitch("Switch.System.IO.UseLegacyPathHandling", false);
            AppContext.SetSwitch("Switch.System.IO.BlockLongPaths", false);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            string local = DataDirectory;
            var preferences = new LanguagePreferences(local);
            using (var window = new DiskWindow(new History(Path.Combine(local, "Historial")), preferences))
                Application.Run(window);
        }
    }

    internal sealed partial class DiskWindow : Form
    {
        private static readonly Color Background = Color.FromArgb(244, 247, 252);
        private static readonly Color Ink = Color.FromArgb(18, 33, 57);
        private static readonly Color Muted = Color.FromArgb(76, 93, 118);
        private static readonly Color Border = Color.FromArgb(206, 217, 232);
        private static readonly Color Hero = Color.FromArgb(19, 38, 70);
        private static readonly Color Blue = Color.FromArgb(37, 75, 183);
        private static readonly Color Tint = Color.FromArgb(234, 241, 255);
        private static readonly Color Growth = Color.FromArgb(157, 62, 29);
        private static readonly Color Reduction = Color.FromArgb(18, 105, 76);
        private static readonly Color WarningInk = Color.FromArgb(119, 64, 13);
        private static readonly Color WarningFill = Color.FromArgb(255, 245, 225);
        private const int DisplayLimit = 3000;

        private readonly History history;
        private readonly FileHistory fileHistory;
        private readonly LanguagePreferences preferences;
        private readonly UiLanguage language = new UiLanguage();
        private readonly List<Action> translations = new List<Action>();
        private readonly ComboBox languagePicker = new ComboBox();
        private readonly Button updateButton = new Button();
        private CancellationTokenSource updateCancellation;
        private readonly ComboBox location = new ComboBox();
        private readonly ComboBox baselinePicker = new ComboBox();
        private readonly TextBox search = new TextBox();
        private readonly CheckBox includeDescendants = new CheckBox();
        private readonly CheckBox growthOnly = new CheckBox();
        private readonly System.Windows.Forms.Timer filterTimer = new System.Windows.Forms.Timer { Interval = 180 };
        private readonly System.Windows.Forms.Timer copyTimer = new System.Windows.Forms.Timer { Interval = 1700 };
        private readonly Button browse = new Button();
        private readonly Button scan = new Button();
        private readonly Button up = new Button();
        private readonly Button home = new Button();
        private readonly Button reveal = new Button();
        private readonly Button copy = new Button();
        private readonly Button measureFolder = new Button();
        private readonly Button largestFiles = new Button();
        private readonly Button explainFolder = new Button();
        private readonly Button compareFiles = new Button();
        private readonly Button clearFilters = new Button();
        private readonly Label breadcrumb = new Label();
        private readonly Label currentValue = new Label();
        private readonly Label changeValue = new Label();
        private readonly Label availableValue = new Label();
        private readonly Label currentDate = new Label();
        private readonly Label changeDescription = new Label();
        private readonly Label capacityNote = new Label();
        private readonly CapacityBar capacityMeter = new CapacityBar();
        private readonly Label notice = new Label();
        private readonly Label quality = new Label();
        private readonly Label detailTitle = new Label();
        private readonly Label detailPath = new PathLabel();
        private readonly Panel emptyState = new Panel();
        private readonly Label emptyTitle = new Label();
        private readonly Label emptyDescription = new Label();
        private readonly Label footer = new Label();
        private readonly ProgressBar progressBar = new ProgressBar();
        private readonly ResultsList listing = new ResultsList("Carpeta", "Cambio", "Tamaño medido", "Antes");
        private readonly ToolTip pathTip = new ToolTip();
        private readonly string[] drives;

        private Snapshot latest;
        private Snapshot baseline;
        private string scope;
        private string relative = "";
        private bool loading;
        private bool refreshingRows;
        private bool closeRequested;
        private CancellationTokenSource activeScan;
        private int sortColumn = -1;
        private bool descending;
        private DriveCapacity displayedCapacity;
        private string footerKey = "";
        private object[] footerArguments = new object[0];

        public DiskWindow(History history, LanguagePreferences preferences, FileHistory fileHistory = null)
        {
            this.history = history;
            this.fileHistory = fileHistory ?? new FileHistory(Path.Combine(Program.DataDirectory, "HistorialArchivos"));
            this.preferences = preferences;
            language.Select(preferences.Load());
            BindText(this, "¿Qué me está llenando el disco?");
            ClientSize = new Size(1045, 690);
            MinimumSize = new Size(860, 655);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 10F);
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = Background;
            ForeColor = Ink;
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            BuildLayout();
            BindEvents();
            ShowCapacity(null);

            drives = DriveInfo.GetDrives()
                .Where(d => d.DriveType == DriveType.Fixed || d.DriveType == DriveType.Removable)
                .Select(drive => drive.Name).ToArray();
            location.Items.AddRange(drives);
            location.Text = Path.GetPathRoot(Environment.SystemDirectory);
            Shown += async (sender, args) =>
            {
                ResizeColumns();
                loading = true;
                UpdateControls();
                string[] recent = (await Task.Run(() => history.RecentLocations())).ToArray();
                if (IsDisposed)
                    return;
                SetLocationChoices(recent);
                if (recent.Length > 0)
                    location.Text = recent[0];
                loading = false;
                await ChooseScope();
            };
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                pathTip.Dispose();
                filterTimer.Dispose();
                copyTimer.Dispose();
            }
            base.Dispose(disposing);
        }

        protected override bool ProcessCmdKey(ref Message message, Keys keyData)
        {
            if (keyData == Keys.F5 && scan.Enabled && activeScan == null)
            {
                scan.PerformClick();
                return true;
            }
            if (keyData == (Keys.Alt | Keys.Up) && up.Enabled)
            {
                up.PerformClick();
                return true;
            }
            if (keyData == (Keys.Control | Keys.Shift | Keys.C) && copy.Enabled)
            {
                copy.PerformClick();
                return true;
            }
            if (keyData == (Keys.Control | Keys.F) && search.Enabled)
            {
                search.Focus();
                search.SelectAll();
                return true;
            }
            return base.ProcessCmdKey(ref message, keyData);
        }

        private void BuildLayout()
        {
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 11,
                Padding = new Padding(22, 17, 22, 8)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            foreach (int height in new[] { 73, 54, 88, 33, 40, 27 })
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 57F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 9F));
            Controls.Add(layout);

            var heading = new Panel { Dock = DockStyle.Fill, BackColor = Hero, Margin = new Padding(0, 0, 0, 11) };
            var title = new Label
            {
                Font = new Font("Segoe UI", 20F, FontStyle.Bold),
                Location = new Point(15, 4), ForeColor = Color.White, AutoSize = true
            };
            BindText(title, "¿Qué me está llenando el disco?");
            heading.Controls.Add(title);
            var subtitle = new Label
            {
                Location = new Point(18, 38), ForeColor = Color.FromArgb(210, 224, 250), AutoSize = true
            };
            BindText(subtitle, "Uso del disco y cambios entre mediciones");
            heading.Controls.Add(subtitle);
            var languagePanel = new TableLayoutPanel
            {
                Dock = DockStyle.Right, Width = 333, ColumnCount = 2, RowCount = 2, Padding = new Padding(0, 6, 14, 5)
            };
            languagePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            languagePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 172F));
            languagePanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));
            languagePanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            var languageLabel = new Label { Dock = DockStyle.Fill, Margin = Padding.Empty, ForeColor = Color.White };
            BindText(languageLabel, "Idioma");
            languagePanel.Controls.Add(languageLabel, 0, 0);
            languagePicker.Dock = DockStyle.Top;
            languagePicker.Margin = Padding.Empty;
            languagePicker.DropDownStyle = ComboBoxStyle.DropDownList;
            languagePicker.Items.AddRange(new object[] { "Español", "English" });
            languagePicker.SelectedIndex = language.Code == "es" ? 0 : 1;
            BindAccessible(languagePicker, "Idioma");
            languagePanel.Controls.Add(languagePicker, 0, 1);
            StyleLocalButton(updateButton, "Buscar actualizaciones", false);
            updateButton.Dock = DockStyle.Top;
            updateButton.Height = 26;
            updateButton.Margin = new Padding(10, 0, 0, 0);
            languagePanel.Controls.Add(updateButton, 1, 1);
            heading.Controls.Add(languagePanel);
            layout.Controls.Add(heading, 0, 0);

            var scopeRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 2, Margin = Padding.Empty };
            scopeRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            scopeRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 136F));
            scopeRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130F));
            scopeRow.RowStyles.Add(new RowStyle(SizeType.Absolute, 21F));
            scopeRow.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
            scopeRow.Controls.Add(LocalLabel("Unidad o carpeta"), 0, 0);
            location.Dock = DockStyle.Fill;
            location.Margin = new Padding(0, 2, 8, 0);
            BindAccessible(location, "Unidad o carpeta");
            location.DropDownWidth = 690;
            BindTip(location, "Abre la lista para ver unidades y ubicaciones medidas recientemente.");
            scopeRow.Controls.Add(location, 0, 1);
            StyleLocalButton(browse, "Elegir carpeta", false);
            StyleLocalButton(scan, "Medir ahora", true);
            BindTip(scan, "Medir la ubicación seleccionada · F5");
            scopeRow.Controls.Add(browse, 1, 1);
            scopeRow.Controls.Add(scan, 2, 1);
            layout.Controls.Add(scopeRow, 0, 1);

            var metrics = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty };
            for (int i = 0; i < 3; i++)
                metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3333F));
            metrics.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            AddMetric(metrics, 0, "Tamaño de la carpeta", currentValue, currentDate);
            AddMetric(metrics, 1, "Cambio de tamaño", changeValue, changeDescription);
            AddMetric(metrics, 2, "Espacio libre en la unidad", availableValue, capacityNote, capacityMeter);
            layout.Controls.Add(metrics, 0, 2);

            var navigation = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 6, RowCount = 1, Margin = Padding.Empty
            };
            navigation.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 79F));
            navigation.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 76F));
            navigation.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            navigation.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 151F));
            navigation.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 108F));
            navigation.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 204F));
            StyleLocalButton(up, "↑ Subir", false);
            StyleLocalButton(home, "Raíz", false);
            BindTip(up, "Subir un nivel · Alt+↑");
            BindTip(home, "Volver a la ubicación medida");
            navigation.Controls.Add(up, 0, 0);
            navigation.Controls.Add(home, 1, 0);
            breadcrumb.Dock = DockStyle.Fill;
            breadcrumb.AutoEllipsis = true;
            breadcrumb.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            breadcrumb.TextAlign = ContentAlignment.MiddleLeft;
            breadcrumb.Margin = new Padding(4, 0, 12, 7);
            navigation.Controls.Add(breadcrumb, 2, 0);
            StyleLocalButton(compareFiles, "Comparar archivos", false);
            compareFiles.Margin = new Padding(0, 0, 14, 6);
            BindTip(compareFiles, "Compara los archivos de la carpeta seleccionada entre dos mediciones.");
            navigation.Controls.Add(compareFiles, 3, 0);
            var compareLabel = LocalLabel("Comparar con");
            compareLabel.TextAlign = ContentAlignment.MiddleLeft;
            compareLabel.Margin = new Padding(0, 0, 0, 7);
            navigation.Controls.Add(compareLabel, 4, 0);
            baselinePicker.DropDownStyle = ComboBoxStyle.DropDownList;
            BindAccessible(baselinePicker, "Medición anterior");
            baselinePicker.FormattingEnabled = true;
            baselinePicker.Format += (sender, args) =>
            {
                var entry = args.ListItem as HistoryEntry;
                args.Value = entry == null ? T("Sin comparación") : language.Date(entry.Date, true);
            };
            baselinePicker.Dock = DockStyle.Top;
            baselinePicker.Margin = new Padding(0, 1, 0, 0);
            navigation.Controls.Add(baselinePicker, 5, 0);
            layout.Controls.Add(navigation, 0, 3);

            var filters = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 1, Margin = Padding.Empty };
            filters.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 109F));
            filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            filters.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 166F));
            filters.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 128F));
            filters.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 119F));
            var searchLabel = LocalLabel("Buscar carpeta");
            searchLabel.TextAlign = ContentAlignment.TopLeft;
            searchLabel.Padding = new Padding(0, 6, 0, 0);
            filters.Controls.Add(searchLabel, 0, 0);
            search.Dock = DockStyle.Top;
            search.Margin = new Padding(0, 5, 14, 0);
            BindAccessible(search, "Buscar carpeta");
            BindTip(search, "Busca por nombre en las subcarpetas. Ctrl+F para buscar, Esc para limpiar.");
            filters.Controls.Add(search, 1, 0);
            BindText(includeDescendants, "Incluir subcarpetas");
            BindText(growthOnly, "Solo aumentos");
            foreach (var toggle in new[] { includeDescendants, growthOnly })
            {
                toggle.Dock = DockStyle.Fill;
                toggle.TextAlign = ContentAlignment.MiddleLeft;
                toggle.Margin = new Padding(0, 0, 3, 8);
            }
            filters.Controls.Add(includeDescendants, 2, 0);
            filters.Controls.Add(growthOnly, 3, 0);
            StyleLocalButton(clearFilters, "Borrar filtros", false);
            clearFilters.Margin = new Padding(0, 2, 0, 12);
            filters.Controls.Add(clearFilters, 4, 0);
            layout.Controls.Add(filters, 0, 4);

            notice.Dock = DockStyle.Fill;
            notice.ForeColor = Muted;
            notice.TextAlign = ContentAlignment.MiddleLeft;
            notice.AutoEllipsis = true;
            notice.Margin = Padding.Empty;
            layout.Controls.Add(notice, 0, 5);

            quality.AutoSize = true;
            quality.Dock = DockStyle.Fill;
            quality.Padding = new Padding(10, 5, 10, 5);
            quality.Margin = new Padding(0, 0, 0, 6);
            quality.Visible = false;
            layout.Controls.Add(quality, 0, 6);

            listing.Dock = DockStyle.Fill;
            listing.Margin = Padding.Empty;
            listing.View = View.Details;
            listing.FullRowSelect = true;
            listing.MultiSelect = false;
            listing.GridLines = false;
            listing.HideSelection = false;
            listing.ShowItemToolTips = true;
            listing.HeaderStyle = ColumnHeaderStyle.Clickable;
            listing.BorderStyle = BorderStyle.None;
            listing.BackColor = Color.White;
            listing.ForeColor = Ink;
            BindAccessible(listing, "Carpetas y cambio de tamaño");
            listing.Columns.Add(T("Carpeta"), 390);
            listing.Columns.Add(T("Cambio"), 137, HorizontalAlignment.Right);
            listing.Columns.Add(T("Tamaño medido"), 155, HorizontalAlignment.Right);
            listing.Columns.Add(T("Antes"), 133, HorizontalAlignment.Right);
            var resultsPanel = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty, BackColor = Border, Padding = new Padding(1) };
            resultsPanel.Controls.Add(listing);
            BuildEmptyState();
            resultsPanel.Controls.Add(emptyState);
            layout.Controls.Add(resultsPanel, 0, 7);

            layout.Controls.Add(BuildInspector(), 0, 8);
            footer.Dock = DockStyle.Fill;
            footer.ForeColor = Muted;
            footer.TextAlign = ContentAlignment.MiddleLeft;
            footer.AutoEllipsis = true;
            layout.Controls.Add(footer, 0, 9);
            progressBar.Dock = DockStyle.Fill;
            progressBar.Margin = Padding.Empty;
            progressBar.Style = ProgressBarStyle.Marquee;
            progressBar.MarqueeAnimationSpeed = 25;
            progressBar.Visible = false;
            layout.Controls.Add(progressBar, 0, 10);
        }

        private void BuildEmptyState()
        {
            emptyState.Dock = DockStyle.Fill;
            emptyState.BackColor = Color.White;
            var message = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
            message.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            message.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
            message.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
            message.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            emptyTitle.Text = T("Primera medición");
            emptyTitle.Font = new Font("Segoe UI", 15F, FontStyle.Bold);
            emptyTitle.Dock = emptyDescription.Dock = DockStyle.Fill;
            emptyTitle.TextAlign = emptyDescription.TextAlign = ContentAlignment.MiddleCenter;
            emptyDescription.ForeColor = Muted;
            message.Controls.Add(emptyTitle, 0, 1);
            message.Controls.Add(emptyDescription, 0, 2);
            emptyState.Controls.Add(message);
        }

        private Control BuildInspector()
        {
            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 6, RowCount = 2, Margin = new Padding(0, 7, 0, 0),
                Padding = new Padding(11, 2, 9, 2), BackColor = Tint
            };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104F));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 107F));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 122F));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 146F));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 128F));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 47F));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 53F));
            detailTitle.Dock = detailPath.Dock = DockStyle.Fill;
            detailTitle.Margin = new Padding(0, 0, 6, 0);
            detailTitle.TextAlign = ContentAlignment.BottomLeft;
            detailTitle.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            detailTitle.AutoEllipsis = true;
            detailPath.Margin = new Padding(0, 0, 12, 2);
            detailPath.ForeColor = Muted;
            detailPath.TextAlign = ContentAlignment.MiddleLeft;
            panel.Controls.Add(detailTitle, 0, 0);
            panel.Controls.Add(detailPath, 0, 1);
            StyleLocalButton(copy, "Copiar ruta", false);
            StyleLocalButton(reveal, "Explorador", false);
            StyleLocalButton(measureFolder, "Medir carpeta", false);
            StyleLocalButton(largestFiles, "Archivos grandes", false);
            StyleLocalButton(explainFolder, "¿Qué es esto?", false);
            copy.Margin = reveal.Margin = measureFolder.Margin = largestFiles.Margin = new Padding(0, 7, 8, 7);
            explainFolder.Margin = new Padding(0, 7, 0, 7);
            BindTip(copy, "Copiar la ruta completa · Ctrl+Mayús+C");
            BindTip(reveal, "Abrir la carpeta en el Explorador de archivos.");
            BindTip(measureFolder, "Guardar una medición para esta carpeta como ubicación independiente.");
            BindTip(largestFiles, "Buscar los archivos más grandes de esta carpeta y sus subcarpetas.");
            BindTip(explainFolder, "Explicar para qué sirve el elemento seleccionado.");
            panel.Controls.Add(copy, 1, 0);
            panel.SetRowSpan(copy, 2);
            panel.Controls.Add(reveal, 2, 0);
            panel.SetRowSpan(reveal, 2);
            panel.Controls.Add(measureFolder, 3, 0);
            panel.SetRowSpan(measureFolder, 2);
            panel.Controls.Add(largestFiles, 4, 0);
            panel.SetRowSpan(largestFiles, 2);
            panel.Controls.Add(explainFolder, 5, 0);
            panel.SetRowSpan(explainFolder, 2);
            return panel;
        }

        private static Label DockedLabel(string value)
        {
            return new Label { Text = value, Dock = DockStyle.Fill, Margin = Padding.Empty, ForeColor = Muted };
        }

        private string T(string key, params object[] args)
        {
            return language.Text(key, args);
        }

        private void BindText(Control control, string key)
        {
            Action update = () => control.Text = T(key);
            translations.Add(update);
            update();
        }

        private void BindAccessible(Control control, string key)
        {
            Action update = () => control.AccessibleName = T(key);
            translations.Add(update);
            update();
        }

        private void BindTip(Control control, string key)
        {
            Action update = () => pathTip.SetToolTip(control, T(key));
            translations.Add(update);
            update();
        }

        private Label LocalLabel(string key)
        {
            var label = DockedLabel("");
            BindText(label, key);
            return label;
        }

        private void StyleLocalButton(Button button, string key, bool primary)
        {
            StyleButton(button, "", primary);
            BindText(button, key);
        }

        private void SetStatus(string key, params object[] args)
        {
            footerKey = key;
            footerArguments = args;
            footer.Text = TranslateStatus();
        }

        private string TranslateStatus()
        {
            return T(footerKey, footerArguments.Select(argument => argument is string ? T((string)argument) : argument).ToArray());
        }

        private void ChangeLanguage()
        {
            if (languagePicker.SelectedIndex < 0)
                return;
            string code = languagePicker.SelectedIndex == 0 ? "es" : "en";
            if (code == language.Code)
                return;
            language.Select(code);
            foreach (Action update in translations)
                update();

            int previous = baselinePicker.SelectedIndex;
            var choices = baselinePicker.Items.Cast<object>().ToArray();
            baselinePicker.BeginUpdate();
            try
            {
                baselinePicker.Items.Clear();
                baselinePicker.Items.AddRange(choices);
                baselinePicker.SelectedIndex = previous;
            }
            finally
            {
                baselinePicker.EndUpdate();
            }

            footer.Text = TranslateStatus();
            ShowCapacity(displayedCapacity);
            ResetCopyButton();
            Render();
            UpdateControls();
            if (!preferences.Save(code))
                SetStatus("No se pudo guardar el idioma. El cambio se aplicó a esta sesión.");
        }

        private void SetHistoryStatus()
        {
            if (latest == null)
                SetStatus("Pulsa Medir ahora para guardar la primera medición.");
            else
                SetStatus("Historial local · hasta {0} mediciones por ubicación", History.Limit);
        }

        private static void StyleButton(Button button, string value, bool primary)
        {
            button.Text = value;
            button.Dock = DockStyle.Fill;
            button.Margin = new Padding(0, 0, primary ? 0 : 8, 6);
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(31, 63, 155) : Tint;
            button.FlatAppearance.MouseDownBackColor = primary ? Color.FromArgb(27, 54, 133) : Border;
            button.UseVisualStyleBackColor = false;
            Action applyColors = () =>
            {
                button.FlatAppearance.BorderColor = button.Enabled && primary ? Blue : Border;
                button.BackColor = button.Enabled ? (primary ? Blue : Color.White) : Background;
                button.ForeColor = button.Enabled ? (primary ? Color.White : Ink) : Muted;
            };
            button.EnabledChanged += (sender, args) => applyColors();
            applyColors();
        }

        private void AddMetric(TableLayoutPanel metrics, int column, string title, Label value, Label detail, Control meter = null)
        {
            var card = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, BackColor = Color.White, ColumnCount = 1, RowCount = 4,
                Margin = new Padding(0, 1, column == 2 ? 0 : 9, 9), Padding = new Padding(12, 5, 12, 5)
            };
            card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            card.RowStyles.Add(new RowStyle(SizeType.Absolute, 17F));
            card.RowStyles.Add(new RowStyle(SizeType.Absolute, 29F));
            card.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            card.RowStyles.Add(new RowStyle(SizeType.Absolute, 4F));
            card.Paint += (sender, args) =>
            {
                using (var outline = new Pen(Border))
                    args.Graphics.DrawRectangle(outline, 0, 0, card.Width - 1, card.Height - 1);
            };
            var caption = LocalLabel(title);
            caption.Font = new Font("Segoe UI", 9F);
            card.Controls.Add(caption, 0, 0);
            value.Font = new Font("Segoe UI", 19F, FontStyle.Bold);
            value.Dock = detail.Dock = DockStyle.Fill;
            value.Margin = detail.Margin = Padding.Empty;
            value.TextAlign = detail.TextAlign = ContentAlignment.MiddleLeft;
            detail.ForeColor = Muted;
            detail.Font = new Font("Segoe UI", 9F);
            card.Controls.Add(value, 0, 1);
            card.Controls.Add(detail, 0, 2);
            if (meter != null)
            {
                meter.Dock = DockStyle.Fill;
                meter.Margin = Padding.Empty;
                card.Controls.Add(meter, 0, 3);
            }
            metrics.Controls.Add(card, column, 0);
        }

        private void BindEvents()
        {
            languagePicker.SelectionChangeCommitted += (sender, args) => ChangeLanguage();
            updateButton.Click += async (sender, args) => await CheckUpdates();
            location.SelectionChangeCommitted += async (sender, args) => await ChooseScope();
            location.KeyDown += async (sender, args) =>
            {
                if (args.KeyCode != Keys.Enter)
                    return;
                args.SuppressKeyPress = true;
                await ChooseScope();
            };
            browse.Click += async (sender, args) =>
            {
                using (var dialog = new FolderBrowserDialog { ShowNewFolderButton = false, Description = T("Elegir carpeta") })
                {
                    if (scope != null && Directory.Exists(scope))
                        dialog.SelectedPath = scope;
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                    {
                        location.Text = dialog.SelectedPath;
                        await ChooseScope();
                    }
                }
            };
            scan.Click += async (sender, args) => await Measure();
            baselinePicker.SelectionChangeCommitted += async (sender, args) => await ChangeBaseline();
            filterTimer.Tick += (sender, args) => RefreshFilters();
            search.TextChanged += (sender, args) =>
            {
                filterTimer.Stop();
                filterTimer.Start();
            };
            search.KeyDown += (sender, args) =>
            {
                if (args.KeyCode != Keys.Enter && args.KeyCode != Keys.Escape)
                    return;
                args.SuppressKeyPress = true;
                if (args.KeyCode == Keys.Escape)
                    search.Clear();
                RefreshFilters();
            };
            includeDescendants.CheckedChanged += (sender, args) => RefreshFilters();
            growthOnly.CheckedChanged += (sender, args) => RefreshFilters();
            clearFilters.Click += (sender, args) =>
            {
                ClearSearch();
                refreshingRows = true;
                includeDescendants.Checked = growthOnly.Checked = false;
                refreshingRows = false;
                RefreshFilters();
                search.Focus();
            };
            up.Click += (sender, args) =>
            {
                NavigateTo(Path.GetDirectoryName(relative) ?? "");
            };
            home.Click += (sender, args) => NavigateTo("");
            reveal.Click += (sender, args) => RevealFolder();
            copy.Click += (sender, args) => CopySelectedPath();
            explainFolder.Click += (sender, args) =>
            {
                string folder = SelectedPath(SelectedRow());
                if (folder != null)
                    ShowExplanation(this, folder, false, language);
            };
            compareFiles.Click += (sender, args) =>
            {
                string folder = SelectedPath(SelectedRow());
                if (folder == null)
                    return;
                using (var window = new FileComparisonWindow(folder, language, fileHistory))
                    window.ShowDialog(this);
            };
            largestFiles.Click += (sender, args) =>
            {
                string folder = SelectedPath(SelectedRow());
                if (folder == null || activeScan != null || loading)
                    return;
                using (var details = new LargestFilesWindow(folder, language))
                    details.ShowDialog(this);
            };
            measureFolder.Click += async (sender, args) =>
            {
                string folder = SelectedPath(SelectedRow());
                if (folder == null || activeScan != null || loading)
                    return;
                location.Text = folder;
                await Measure();
            };
            copyTimer.Tick += (sender, args) => ResetCopyButton();
            listing.SelectedIndexChanged += (sender, args) =>
            {
                if (!refreshingRows)
                    UpdateNavigation();
            };
            listing.DoubleClick += (sender, args) => OpenSelected();
            listing.KeyDown += (sender, args) =>
            {
                if (args.KeyCode == Keys.Enter)
                {
                    args.SuppressKeyPress = true;
                    OpenSelected();
                }
            };
            listing.ColumnClick += (sender, args) =>
            {
                int currentSort = sortColumn < 0 ? DefaultSortColumn : sortColumn;
                descending = currentSort == args.Column ? (sortColumn >= 0 && !descending) : args.Column != 0;
                sortColumn = args.Column;
                RefreshFilters();
            };
            listing.ClientSizeChanged += (sender, args) => ResizeColumns();
            FormClosing += (sender, args) =>
            {
                if (updateCancellation != null)
                    updateCancellation.Cancel();
                if (activeScan == null)
                    return;
                closeRequested = true;
                activeScan.Cancel();
                args.Cancel = true;
                SetStatus(loading ? "Cerrando al terminar de guardar…" : "Cancelando la medición antes de cerrar…");
            };
        }

        private void RefreshFilters()
        {
            filterTimer.Stop();
            if (!refreshingRows)
                RenderRows();
        }

        private void ClearSearch()
        {
            search.Clear();
            filterTimer.Stop();
        }

        private void SetLocationChoices(IEnumerable<string> recent)
        {
            string selected = location.Text;
            location.BeginUpdate();
            try
            {
                location.Items.Clear();
                location.Items.AddRange(recent.Concat(drives).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
                location.Text = selected;
            }
            finally
            {
                location.EndUpdate();
            }
        }

        private async Task ChooseScope()
        {
            if (loading || activeScan != null)
                return;
            string selected = GetLocation();
            if (selected == null)
                return;
            if (string.Equals(scope, selected, StringComparison.OrdinalIgnoreCase))
            {
                location.Text = scope;
                return;
            }
            scope = selected;
            location.Text = scope;
            latest = null;
            baseline = null;
            ShowCapacity(null);
            relative = "";
            ClearSearch();
            sortColumn = -1;
            await LoadHistory();
        }

        private string GetLocation()
        {
            try
            {
                string input = location.Text.Trim().Trim('"');
                if (input.Length == 0 || !Path.IsPathRooted(input) ||
                    (input.Length >= 2 && input[1] == ':' && (input.Length < 3 || input[2] != '\\' && input[2] != '/')))
                    throw new ArgumentException("Escribe una ruta completa, por ejemplo C:\\ o C:\\Videos.");
                return History.Normalize(input);
            }
            catch (Exception ex)
            {
                ShowMessage(ex.Message);
                return null;
            }
        }

        private async Task LoadHistory(Snapshot justSaved = null, DateTime? pinnedBaseline = null)
        {
            loading = true;
            UpdateControls();
            SetStatus("Cargando historial…");
            if (latest == null)
                Render();
            try
            {
                string root = scope;
                var disk = await Task.Run(() => ReadDriveCapacity(root));
                if (IsDisposed)
                    return;
                ShowCapacity(disk);
                var entries = await Task.Run(() => history.List(root));
                if (IsDisposed)
                    return;
                baselinePicker.Items.Clear();
                baselinePicker.Items.Add("Sin comparación");
                foreach (var entry in entries.Skip(1))
                    baselinePicker.Items.Add(entry);

                latest = entries.Count == 0 ? null :
                    justSaved != null && justSaved.Date == entries[0].Date ? justSaved :
                    await Task.Run(() => history.Read(entries[0]));
                baseline = null;
                int previous = entries.Count > 1 ? 1 : 0;
                if (pinnedBaseline.HasValue && previous > 0)
                {
                    int pinned = entries.FindIndex(1, entry => entry.Date == pinnedBaseline.Value);
                    if (pinned >= 1)
                        previous = pinned;
                }
                if (previous > 0)
                    baseline = await Task.Run(() => history.Read(entries[previous]));
                if (IsDisposed)
                    return;
                baselinePicker.SelectedIndex = previous;
                if (justSaved != null)
                    SetLocationChoices(await Task.Run(() => history.RecentLocations()));
                SetHistoryStatus();
            }
            catch (Exception ex)
            {
                if (!IsDisposed)
                {
                    baselinePicker.SelectedIndex = baselinePicker.Items.Count > 0 ? 0 : -1;
                    SetStatus("No se pudo cargar el historial: {0}", ex.Message);
                }
            }
            finally
            {
                loading = false;
                if (!IsDisposed)
                {
                    RestoreFolderIfAvailable();
                    Render();
                    UpdateControls();
                }
            }
        }

        private async Task ChangeBaseline()
        {
            var entry = baselinePicker.SelectedItem as HistoryEntry;
            loading = true;
            UpdateControls();
            try
            {
                baseline = entry == null ? null : await Task.Run(() => history.Read(entry));
                if (!IsDisposed)
                    SetHistoryStatus();
            }
            catch (Exception ex)
            {
                baseline = null;
                if (!IsDisposed)
                {
                    baselinePicker.SelectedIndex = 0;
                    SetStatus("No se pudo cargar la medición elegida: {0}", ex.Message);
                }
            }
            finally
            {
                loading = false;
                if (!IsDisposed)
                {
                    RestoreFolderIfAvailable();
                    sortColumn = -1;
                    Render();
                    UpdateControls();
                }
            }
        }

        private void RestoreFolderIfAvailable()
        {
            string original = relative;
            while (relative.Length > 0 && (latest == null || latest.Get(relative) == null) &&
                (baseline == null || baseline.Get(relative) == null))
                relative = Path.GetDirectoryName(relative) ?? "";
            if (relative != original)
                ClearSearch();
        }

        private async Task Measure()
        {
            if (activeScan != null)
            {
                activeScan.Cancel();
                scan.Enabled = false;
                SetStatus("Cancelando la medición…");
                return;
            }
            string root = GetLocation();
            if (root == null)
                return;
            if (!string.Equals(root, scope, StringComparison.OrdinalIgnoreCase))
            {
                await ChooseScope();
                if (!string.Equals(root, scope, StringComparison.OrdinalIgnoreCase))
                    return;
            }
            activeScan = new CancellationTokenSource();
            var token = activeScan.Token;
            var pinned = baselinePicker.SelectedIndex > 1 ? baselinePicker.SelectedItem as HistoryEntry : null;
            UpdateControls();
            SetStatus("Midiendo… puedes seguir usando Windows.");
            if (latest == null)
                RenderRows();
            var reporter = new Progress<ScanProgress>(status =>
            {
                if (activeScan != null && !token.IsCancellationRequested)
                    SetStatus("Midiendo… {0:N0} carpetas · {1:N0} archivos", status.Folders, status.Files);
            });

            try
            {
                var snapshot = await Task.Run(() => new Scanner(token, reporter).Scan(root), token);
                token.ThrowIfCancellationRequested();
                loading = true;
                UpdateControls();
                SetStatus("Guardando la medición…");
                await Task.Run(() => history.Save(root, snapshot));
                await LoadHistory(snapshot, pinned == null ? (DateTime?)null : pinned.Date);
            }
            catch (OperationCanceledException)
            {
                SetStatus("Medición cancelada. Se conservó el historial anterior.");
            }
            catch (Exception ex)
            {
                SetStatus("No se pudo completar la medición.");
                ShowMessage(ex.Message);
            }
            finally
            {
                loading = false;
                activeScan.Dispose();
                activeScan = null;
                UpdateControls();
                if (latest == null)
                    RenderRows();
                if (closeRequested)
                    Close();
            }
        }

        private void UpdateControls()
        {
            bool measuring = activeScan != null;
            location.Enabled = browse.Enabled = !loading && !measuring;
            baselinePicker.Enabled = !loading && !measuring && latest != null && baselinePicker.Items.Count > 1;
            search.Enabled = includeDescendants.Enabled = !loading && latest != null;
            growthOnly.Enabled = !loading && baseline != null;
            scan.Enabled = updateCancellation == null && !loading && (!measuring || !activeScan.IsCancellationRequested);
            updateButton.Enabled = updateCancellation == null && !loading && !measuring;
            scan.Text = T(measuring ? (loading ? "Guardando…" : "Cancelar") : "Medir ahora");
            progressBar.Visible = measuring;
            UpdateFilterActions();
            UpdateNavigation();
        }

        private void UpdateFilterActions()
        {
            clearFilters.Enabled = !loading && latest != null &&
                (search.Text.Length > 0 || includeDescendants.Checked || growthOnly.Checked);
        }

        private void UpdateNavigation()
        {
            home.Enabled = up.Enabled = relative.Length > 0 && !loading;
            var row = SelectedRow();
            string folder = SelectedPath(row);
            copy.Enabled = folder != null;
            explainFolder.Enabled = folder != null;
            compareFiles.Enabled = folder != null && !loading && activeScan == null && updateCancellation == null;
            reveal.Enabled = folder != null && (row != null && !row.DirectFiles
                ? row.ExistsNow
                : relative.Length == 0 || latest != null && latest.Get(relative) != null);
            measureFolder.Enabled = reveal.Enabled && !loading && activeScan == null &&
                !string.Equals(folder, scope, StringComparison.OrdinalIgnoreCase);
            largestFiles.Enabled = reveal.Enabled && !loading && activeScan == null;
            string label = T(row == null || row.DirectFiles ? "Ubicación actual" : "Carpeta seleccionada");
            if (row != null && row.NowPartial)
                label += "  ·  " + T("Lectura parcial");
            else if (row != null && row.BeforePartial)
                label += "  ·  " + T("Medición anterior parcial");
            else if (row != null && !row.ExistsNow)
                label += "  ·  " + T("Solo en la medición anterior");
            detailTitle.Text = label;
            if (detailPath.Text != (folder ?? "—"))
                ResetCopyButton();
            detailPath.Text = folder ?? "—";
            pathTip.SetToolTip(detailPath, folder ?? "");
        }

        private void ResizeColumns()
        {
            if (listing.ClientSize.Width <= 0)
                return;
            int width = Math.Max(180, listing.ClientSize.Width - listing.Columns[1].Width -
                listing.Columns[2].Width - listing.Columns[3].Width - SystemInformation.VerticalScrollBarWidth - 5);
            if (listing.Columns[0].Width != width)
                listing.Columns[0].Width = width;
        }

        private void Render()
        {
            string fullPath = scope == null ? "" : Path.Combine(scope, relative);
            string folderName = scope == null ? "" : Path.GetFileName(scope);
            if (string.IsNullOrEmpty(folderName))
                folderName = scope ?? "";
            breadcrumb.Text = relative.Length == 0
                ? folderName
                : folderName + "  ›  " + string.Join("  ›  ", relative.Split(Path.DirectorySeparatorChar));
            pathTip.SetToolTip(breadcrumb, fullPath);
            var now = latest == null ? null : latest.Get(relative);
            var before = baseline == null ? null : baseline.Get(relative);
            currentValue.Text = latest == null ? "—" : (now != null && now.Partial ? "≈ " : "") +
                FormatBytes(now == null ? 0 : now.Total, language.Culture);
            currentDate.Text = latest == null ? T("Primera medición pendiente") : T("Medido el {0}", language.Date(latest.Date));
            long delta = (now == null ? 0 : now.Total) - (before == null ? 0 : before.Total);
            changeValue.Text = baseline == null ? "—" : FormatChange(delta, IsPartial(now, before));
            changeValue.ForeColor = baseline == null ? Ink : ChangeColor(delta);
            changeDescription.Text = baseline == null ? T("Disponible con dos mediciones") : T("Desde el {0}", language.Date(baseline.Date));
            RenderRows();
        }

        private int DefaultSortColumn { get { return baseline == null ? 2 : 1; } }

        private void RenderRows()
        {
            string query = search.Text.Trim();
            bool recursive = includeDescendants.Checked || query.Length > 0;
            bool onlyGrowth = baseline != null && growthOnly.Checked;
            bool filtered = query.Length > 0 || onlyGrowth;
            var previousSelection = SelectedRow();
            int matches = 0;
            refreshingRows = true;
            listing.BeginUpdate();
            try
            {
                listing.Items.Clear();
                int column = sortColumn < 0 ? DefaultSortColumn : sortColumn;
                bool desc = sortColumn < 0 || descending;
                listing.SetSort(column, desc, language);
                if (latest != null)
                {
                    var rows = ComparisonRow.InFolder(latest, baseline, relative, recursive);
                    if (query.Length > 0)
                        rows = rows.Where(row => !row.DirectFiles && language.Culture.CompareInfo.IndexOf(Path.GetFileName(row.Path),
                            query, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0);
                    if (onlyGrowth)
                        rows = rows.Where(row => row.Change > 0);
                    var visible = rows.ToList();
                    matches = visible.Count;
                    Func<ComparisonRow, string> name = row => RowName(row, recursive);
                    IEnumerable<ComparisonRow> sorted = column == 0 ?
                        (desc ? visible.OrderByDescending(name, StringComparer.CurrentCultureIgnoreCase)
                            : visible.OrderBy(name, StringComparer.CurrentCultureIgnoreCase)) :
                        (desc ? visible.OrderByDescending(row => ColumnValue(row, column))
                            : visible.OrderBy(row => ColumnValue(row, column))).ThenBy(name, StringComparer.CurrentCultureIgnoreCase);
                    var items = sorted.Take(DisplayLimit).Select(row => CreateItem(row, name(row))).ToArray();
                    listing.Items.AddRange(items);
                    if (previousSelection != null)
                    {
                        var item = items.FirstOrDefault(candidate =>
                        {
                            var row = (ComparisonRow)candidate.Tag;
                            return row.DirectFiles == previousSelection.DirectFiles &&
                                string.Equals(row.Path, previousSelection.Path, StringComparison.OrdinalIgnoreCase);
                        });
                        if (item != null)
                        {
                            item.Selected = item.Focused = true;
                            item.EnsureVisible();
                        }
                    }
                }
            }
            finally
            {
                listing.EndUpdate();
                refreshingRows = false;
            }
            notice.Text = BuildNotice(matches, recursive, filtered);
            UpdateEmptyState(matches, filtered);
            UpdateQualityNotice();
            ResizeColumns();
            UpdateFilterActions();
            UpdateNavigation();
        }

        private string BuildNotice(int matches, bool recursive, bool filtered)
        {
            if (latest == null || matches == 0)
                return T(filtered ? "0 resultados" : "Contenido de la carpeta");
            if (matches > DisplayLimit)
                return T("{0:N0} de {1:N0} resultados · Usa Buscar carpeta para acotar", DisplayLimit, matches);
            string count = T(matches == 1 ? "{0:N0} resultado" : "{0:N0} resultados", matches);
            return count + "  ·  " + T(recursive ? "El tamaño de cada carpeta incluye sus subcarpetas" : "Doble clic para explorar");
        }

        private void UpdateQualityNotice()
        {
            if (latest == null)
            {
                quality.Visible = false;
                return;
            }
            int beforeErrors = baseline == null ? 0 : baseline.Errors;
            bool partial = latest.Errors > 0 || beforeErrors > 0;
            string errorText = baseline == null
                ? T(latest.Errors == 1 ? "{0:N0} entrada sin acceso o lectura" : "{0:N0} entradas sin acceso o lectura", latest.Errors)
                : T("Entradas sin acceso o lectura ({0:N0} ahora, {1:N0} antes)", latest.Errors, beforeErrors);
            quality.Text = partial ? T("≈ Medición parcial") + "  ·  " + errorText : "";
            quality.BackColor = WarningFill;
            quality.ForeColor = WarningInk;
            quality.Visible = partial;
            pathTip.SetToolTip(quality, T("≈ señala los valores afectados por entradas que no se pudieron medir."));
            pathTip.SetToolTip(notice, T(
                "La medición suma tamaños de archivo. Puntos de reanálisis omitidos, incluidos enlaces: {0:N0}.", latest.Links));
        }

        private void UpdateEmptyState(int matches, bool filtered)
        {
            emptyState.Visible = matches == 0;
            listing.Visible = matches > 0;
            if (matches != 0)
                return;
            if (latest == null)
            {
                emptyTitle.Text = T(loading ? "Cargando mediciones…" : activeScan != null ? "Medición en curso" : "Guarda la primera medición");
                emptyDescription.Text = activeScan != null
                    ? T("El avance aparece en la parte inferior de la ventana.")
                    : loading ? "" : T("Elige una ubicación y pulsa Medir ahora.");
            }
            else
            {
                emptyTitle.Text = T(filtered ? "Sin resultados" : "Carpeta sin entradas medidas");
                emptyDescription.Text = T(filtered ? "Prueba otro nombre o pulsa Borrar filtros." : "Puedes subir de nivel para explorar otras carpetas.");
            }
        }

        private ComparisonRow SelectedRow()
        {
            return listing.SelectedItems.Count == 0 ? null : (ComparisonRow)listing.SelectedItems[0].Tag;
        }

        private void OpenSelected()
        {
            var row = SelectedRow();
            if (row == null || row.DirectFiles)
                return;
            NavigateTo(row.Path);
        }

        private void NavigateTo(string path)
        {
            relative = path;
            if (listing.SelectedItems.Count != 0)
                listing.SelectedItems[0].Selected = false;
            ClearSearch();
            Render();
        }

        private string SelectedPath(ComparisonRow row)
        {
            return scope == null ? null : Path.Combine(scope, row == null || row.DirectFiles ? relative : row.Path);
        }

        private void RevealFolder()
        {
            var folder = SelectedPath(SelectedRow());
            if (folder == null)
                return;
            try
            {
                if (!Directory.Exists(folder))
                    throw new DirectoryNotFoundException("Esta carpeta ya no está disponible.");
                Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true, Verb = "explore" });
            }
            catch (Exception ex)
            {
                ShowMessage(ex.Message);
            }
        }

        private void CopySelectedPath()
        {
            string folder = SelectedPath(SelectedRow());
            if (folder == null)
                return;
            try
            {
                Clipboard.SetText(folder);
                copy.Text = T("Ruta copiada");
                copyTimer.Stop();
                copyTimer.Start();
            }
            catch (ExternalException)
            {
                ShowMessage("El portapapeles está ocupado. Vuelve a intentar copiar la ruta.");
            }
        }

        private void ResetCopyButton()
        {
            copyTimer.Stop();
            copy.Text = T("Copiar ruta");
        }

        private ListViewItem CreateItem(ComparisonRow row, string label)
        {
            var item = new ListViewItem(label)
            {
                Tag = row, UseItemStyleForSubItems = false,
                ToolTipText = row.DirectFiles ? label : Path.Combine(scope, row.Path)
            };
            var change = item.SubItems.Add(baseline == null ? "—" : FormatChange(row.Change, row.Partial));
            change.ForeColor = baseline == null ? Muted : ChangeColor(row.Change);
            item.SubItems.Add((row.NowPartial ? "≈ " : "") + FormatBytes(row.Now, language.Culture));
            item.SubItems.Add(baseline == null ? "—" : (row.BeforePartial ? "≈ " : "") +
                FormatBytes(row.Before, language.Culture)).ForeColor = Muted;
            return item;
        }

        private string RowName(ComparisonRow row, bool recursive)
        {
            if (row.DirectFiles)
                return T("Archivos de esta carpeta");
            if (!recursive)
                return Path.GetFileName(row.Path);
            return relative.Length == 0 ? row.Path : row.Path.Substring(relative.Length + 1);
        }

        private static long ColumnValue(ComparisonRow row, int column)
        {
            if (column == 1)
                return row.Change;
            return column == 3 ? row.Before : row.Now;
        }

        private static bool IsPartial(FolderSize current, FolderSize before)
        {
            return (current != null && current.Partial) || (before != null && before.Partial);
        }

        private static Color ChangeColor(long value)
        {
            return value > 0 ? Growth : value < 0 ? Reduction : Ink;
        }

        private string FormatChange(long value, bool partial)
        {
            return (partial ? "≈ " : "") + (value > 0 ? "+" : value < 0 ? "−" : "") + FormatBytes(Math.Abs(value), language.Culture);
        }

        private static string FormatBytes(long value, CultureInfo culture)
        {
            double scaled = value;
            string[] units = { "B", "KiB", "MiB", "GiB", "TiB", "PiB", "EiB" };
            int index = 0;
            while (scaled >= 1024 && index < units.Length - 1)
            {
                scaled /= 1024;
                index++;
            }
            return scaled.ToString(index == 0 ? "N0" : "0.##", culture) + " " + units[index];
        }

        private static DriveCapacity ReadDriveCapacity(string path)
        {
            try
            {
                var drive = new DriveInfo(Path.GetPathRoot(path));
                return new DriveCapacity { Free = drive.AvailableFreeSpace, Total = drive.TotalSize };
            }
            catch (Exception ex)
            {
                if (!Scanner.IsFileError(ex) && !(ex is ArgumentException) && !(ex is NotSupportedException))
                    throw;
                return null;
            }
        }

        private void ShowCapacity(DriveCapacity disk)
        {
            displayedCapacity = disk;
            availableValue.Text = disk == null ? "—" : FormatBytes(disk.Free, language.Culture);
            capacityNote.Text = disk == null ? T("Espacio de la unidad no disponible") : T("de {0} totales", FormatBytes(disk.Total, language.Culture));
            capacityMeter.Visible = disk != null && disk.Total > 0;
            if (disk != null && disk.Total > 0)
            {
                capacityMeter.SetUsed(1.0 - (double)disk.Free / disk.Total);
                capacityMeter.AccessibleName = T("Espacio utilizado en la unidad: {0:P0}", capacityMeter.Used);
            }
        }

        private sealed class DriveCapacity
        {
            public long Free;
            public long Total;
        }

        private sealed class PathLabel : Label
        {
            protected override void OnPaint(PaintEventArgs args)
            {
                TextRenderer.DrawText(args.Graphics, Text, Font, ClientRectangle, ForeColor,
                    TextFormatFlags.PathEllipsis | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            }
        }

        private sealed class CapacityBar : Control
        {
            private double used;
            public double Used { get { return used; } }

            public CapacityBar()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
                TabStop = false;
            }

            public void SetUsed(double fraction)
            {
                used = Math.Max(0, Math.Min(1, fraction));
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs args)
            {
                using (var track = new SolidBrush(Tint))
                using (var fill = new SolidBrush(used >= 0.9 ? Growth : Blue))
                {
                    args.Graphics.FillRectangle(track, ClientRectangle);
                    args.Graphics.FillRectangle(fill, 0, 0, (int)Math.Round(Width * used), Height);
                }
            }
        }

        private sealed class ResultsList : ListView
        {
            private static readonly Color HeaderFill = Color.FromArgb(236, 242, 252);
            private static readonly Color Alternate = Color.FromArgb(248, 250, 254);
            private static readonly Color SelectedFill = Color.FromArgb(224, 235, 255);
            private readonly string[] titles;
            private ImageList rowHeight;

            public ResultsList(params string[] titles)
            {
                this.titles = titles;
                DoubleBuffered = true;
                OwnerDraw = !SystemInformation.HighContrast;
            }

            public void SetSort(int column, bool descending, UiLanguage language)
            {
                for (int i = 0; i < titles.Length; i++)
                {
                    string label = language.Text(titles[i]) + (i == column ? (descending ? "  ↓" : "  ↑") : "");
                    if (Columns[i].Text != label)
                        Columns[i].Text = label;
                }
            }

            protected override void OnHandleCreated(EventArgs args)
            {
                base.OnHandleCreated(args);
                if (rowHeight != null)
                    return;
                int height;
                using (var graphics = CreateGraphics())
                    height = (int)Math.Round(graphics.DpiY / 96F * 25);
                rowHeight = new ImageList { ImageSize = new Size(1, height), ColorDepth = ColorDepth.Depth32Bit };
                using (var blank = new Bitmap(1, height))
                {
                    rowHeight.Images.Add(blank);
                    SmallImageList = rowHeight;
                }
            }

            protected override void Dispose(bool disposing)
            {
                base.Dispose(disposing);
                if (disposing && rowHeight != null)
                    rowHeight.Dispose();
            }

            protected override void OnDrawColumnHeader(DrawListViewColumnHeaderEventArgs args)
            {
                using (var fill = new SolidBrush(HeaderFill))
                using (var line = new Pen(Border))
                {
                    args.Graphics.FillRectangle(fill, args.Bounds);
                    args.Graphics.DrawLine(line, args.Bounds.Left, args.Bounds.Bottom - 1, args.Bounds.Right, args.Bounds.Bottom - 1);
                }
                DrawCell(args.Graphics, args.Header.Text, args.Bounds, args.Header.TextAlign, Ink, Font);
            }

            protected override void OnDrawSubItem(DrawListViewSubItemEventArgs args)
            {
                bool selected = args.Item.Selected;
                Color background = selected ? SelectedFill : args.ItemIndex % 2 == 0 ? Color.White : Alternate;
                using (var fill = new SolidBrush(background))
                    args.Graphics.FillRectangle(fill, args.Bounds);

                if (args.ColumnIndex == 0 && (selected || args.Item.Focused && Focused))
                {
                    using (var accent = new SolidBrush(Blue))
                        args.Graphics.FillRectangle(accent, args.Bounds.Left, args.Bounds.Top, 3, args.Bounds.Height);
                }
                Color textColor = args.SubItem.Text == "—" ? Muted : args.SubItem.ForeColor;
                DrawCell(args.Graphics, args.SubItem.Text, args.Bounds, Columns[args.ColumnIndex].TextAlign, textColor, Font);
            }

            private static void DrawCell(Graphics graphics, string text, Rectangle bounds, HorizontalAlignment alignment, Color color, Font font)
            {
                int padding = (int)Math.Round(graphics.DpiX / 96F * 11);
                bounds.Inflate(-padding, 0);
                var align = alignment == HorizontalAlignment.Right ? TextFormatFlags.Right : TextFormatFlags.Left;
                TextRenderer.DrawText(graphics, text, font, bounds, color, align | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            }
        }

        private void ShowMessage(string message)
        {
            MessageBox.Show(this, T(message), Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
