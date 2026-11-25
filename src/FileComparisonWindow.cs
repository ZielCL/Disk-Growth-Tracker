using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DiskGrowth
{
    internal sealed partial class DiskWindow
    {
        private sealed class FileComparisonWindow : Form
        {
            private const int DisplayLimit = 3000;
            private readonly string folder;
            private readonly UiLanguage language;
            private readonly FileHistory history;
            private readonly Button measure = new Button();
            private readonly Button copy = new Button();
            private readonly ComboBox baseline = new ComboBox();
            private readonly ComboBox filter = new ComboBox();
            private readonly TextBox search = new TextBox();
            private readonly PathLabel detail = new PathLabel();
            private readonly Label summary = new Label();
            private readonly Label resultCount = new Label();
            private readonly Label warning = new Label();
            private readonly ResultsList files = new ResultsList("Archivo", "Estado", "Cambio", "Antes", "Ahora");
            private readonly System.Windows.Forms.Timer searchDelay = new System.Windows.Forms.Timer { Interval = 170 };
            private readonly ToolTip tips = new ToolTip();
            private ViewData view;
            private CancellationTokenSource work;
            private bool closing;
            private bool capturing;
            private bool descending = true;
            private readonly string[] kinds = { "Todos los cambios", "Nuevo", "Ya no aparece", "Aumentó", "Se redujo", "Sin confirmar" };

            public FileComparisonWindow(string folder, UiLanguage language, FileHistory history)
            {
                this.folder = folder;
                this.language = language;
                this.history = history;
                Text = T("Comparar archivos");
                StartPosition = FormStartPosition.CenterParent;
                ShowInTaskbar = false;
                ClientSize = new Size(960, 625);
                MinimumSize = new Size(780, 500);
                AutoScaleMode = AutoScaleMode.Dpi;
                Font = new Font("Segoe UI", 10F);
                BackColor = Background;
                ForeColor = Ink;
                BuildComparisonLayout();
                SetControls();
                Shown += async (sender, args) => await Refresh(false);
                measure.Click += async (sender, args) =>
                {
                    if (work != null)
                    {
                        work.Cancel();
                        measure.Enabled = false;
                        return;
                    }
                    await Refresh(true);
                };
                baseline.SelectionChangeCommitted += async (sender, args) => await Refresh(false, baseline.SelectedItem as HistoryEntry);
                filter.SelectionChangeCommitted += (sender, args) => RenderResults();
                search.TextChanged += (sender, args) => { searchDelay.Stop(); searchDelay.Start(); };
                searchDelay.Tick += (sender, args) => { searchDelay.Stop(); RenderResults(); };
                files.SelectedIndexChanged += (sender, args) => UpdateSelection();
                files.ColumnClick += (sender, args) =>
                {
                    if (args.Column == 2)
                    {
                        descending = !descending;
                        RenderResults();
                    }
                };
                files.ClientSizeChanged += (sender, args) => ResizeFileColumn();
                copy.Click += (sender, args) => CopyPath();
                FormClosing += (sender, args) =>
                {
                    if (work == null)
                        return;
                    closing = true;
                    args.Cancel = true;
                    work.Cancel();
                };
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    searchDelay.Dispose();
                    tips.Dispose();
                }
                base.Dispose(disposing);
            }

            private string T(string key, params object[] args) { return language.Text(key, args); }

            private void BuildComparisonLayout()
            {
                var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 8, Padding = new Padding(18, 14, 18, 9) };
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
                foreach (int height in new[] { 76, 40, 38, 28, 27 })
                    layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
                Controls.Add(layout);

                var heading = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill, BackColor = Hero, ColumnCount = 1, RowCount = 2,
                    Padding = new Padding(13, 6, 13, 6), Margin = new Padding(0, 0, 0, 8)
                };
                heading.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
                heading.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
                heading.Controls.Add(new Label
                {
                    Text = Text, Dock = DockStyle.Fill, Margin = Padding.Empty, ForeColor = Color.White,
                    Font = new Font("Segoe UI", 17F, FontStyle.Bold)
                }, 0, 0);
                var path = new PathLabel { Text = folder, Dock = DockStyle.Fill, Margin = Padding.Empty, ForeColor = Color.FromArgb(210, 224, 250) };
                tips.SetToolTip(path, folder);
                heading.Controls.Add(path, 0, 1);
                layout.Controls.Add(heading, 0, 0);

                var tools = Bar(102, 214, 0, 165);
                tools.Controls.Add(BarLabel("Comparar con"), 0, 0);
                baseline.Dock = DockStyle.Top;
                baseline.Margin = new Padding(0, 3, 12, 0);
                baseline.DropDownStyle = ComboBoxStyle.DropDownList;
                baseline.AccessibleName = T("Comparar con");
                baseline.FormattingEnabled = true;
                baseline.Format += (sender, args) =>
                {
                    var entry = args.ListItem as HistoryEntry;
                    args.Value = entry == null ? T("Sin comparación") : language.Date(entry.Date, true);
                };
                tools.Controls.Add(baseline, 1, 0);
                StyleButton(measure, T("Guardar medición"), true);
                tools.Controls.Add(measure, 3, 0);
                layout.Controls.Add(tools, 0, 1);

                var filters = Bar(111, 0, 70, 183);
                filters.Controls.Add(BarLabel("Buscar archivo"), 0, 0);
                search.Dock = DockStyle.Top;
                search.Margin = new Padding(0, 3, 12, 0);
                search.AccessibleName = T("Buscar archivo");
                filters.Controls.Add(search, 1, 0);
                filters.Controls.Add(BarLabel("Estado"), 2, 0);
                filter.Dock = DockStyle.Top;
                filter.Margin = new Padding(0, 3, 0, 0);
                filter.DropDownStyle = ComboBoxStyle.DropDownList;
                filter.AccessibleName = T("Estado");
                filter.Items.AddRange(kinds.Select(key => T(key)).ToArray());
                filter.SelectedIndex = 0;
                filters.Controls.Add(filter, 3, 0);
                layout.Controls.Add(filters, 0, 2);

                ConfigureLine(summary);
                ConfigureLine(resultCount);
                layout.Controls.Add(summary, 0, 3);
                layout.Controls.Add(resultCount, 0, 4);
                warning.Dock = DockStyle.Fill;
                warning.AutoSize = true;
                warning.Margin = new Padding(0, 0, 0, 6);
                warning.Padding = new Padding(8, 5, 8, 5);
                warning.ForeColor = WarningInk;
                warning.BackColor = WarningFill;
                warning.Visible = false;
                layout.Controls.Add(warning, 0, 5);

                files.Dock = DockStyle.Fill;
                files.View = View.Details;
                files.FullRowSelect = true;
                files.MultiSelect = false;
                files.HideSelection = false;
                files.ShowItemToolTips = true;
                files.BorderStyle = BorderStyle.None;
                files.ForeColor = Ink;
                files.AccessibleName = Text;
                files.Columns.Add(T("Archivo"), 350);
                files.Columns.Add(T("Estado"), 114);
                files.Columns.Add(T("Cambio"), 104, HorizontalAlignment.Right);
                files.Columns.Add(T("Antes"), 104, HorizontalAlignment.Right);
                files.Columns.Add(T("Ahora"), 104, HorizontalAlignment.Right);
                var table = new Panel { Dock = DockStyle.Fill, Padding = new Padding(1), BackColor = Border, Margin = Padding.Empty };
                table.Controls.Add(files);
                layout.Controls.Add(table, 0, 6);

                var selection = Bar(0, 110);
                selection.BackColor = Tint;
                selection.Margin = new Padding(0, 7, 0, 0);
                selection.Padding = new Padding(8, 0, 7, 0);
                detail.Dock = DockStyle.Fill;
                detail.ForeColor = Muted;
                selection.Controls.Add(detail, 0, 0);
                StyleButton(copy, T("Copiar ruta"), false);
                copy.Margin = new Padding(0, 5, 0, 5);
                selection.Controls.Add(copy, 1, 0);
                layout.Controls.Add(selection, 0, 7);
            }

            private static TableLayoutPanel Bar(params int[] widths)
            {
                var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = widths.Length, RowCount = 1, Margin = Padding.Empty };
                foreach (int width in widths)
                    panel.ColumnStyles.Add(width == 0 ? new ColumnStyle(SizeType.Percent, 100F) : new ColumnStyle(SizeType.Absolute, width));
                return panel;
            }

            private Label BarLabel(string key)
            {
                var label = new Label { Text = T(key) };
                ConfigureLine(label);
                label.Margin = new Padding(0, 0, 3, 7);
                return label;
            }

            private static void ConfigureLine(Label label)
            {
                label.Dock = DockStyle.Fill;
                label.AutoEllipsis = true;
                label.TextAlign = ContentAlignment.MiddleLeft;
                label.ForeColor = Muted;
                label.Margin = Padding.Empty;
            }

            private async Task Refresh(bool capture, HistoryEntry selected = null)
            {
                var source = new CancellationTokenSource();
                var token = source.Token;
                work = source;
                capturing = capture;
                SetControls();
                summary.Text = T(capture ? "Midiendo…" : "Cargando historial…");
                var progress = new Progress<ScanProgress>(state =>
                {
                    if (work == source && !token.IsCancellationRequested)
                        summary.Text = T("Midiendo… {0:N0} carpetas · {1:N0} archivos", state.Folders, state.Files);
                });
                try
                {
                    view = await Task.Run(() => LoadView(capture, selected, progress, token), token);
                    baseline.Items.Clear();
                    if (view.Entries.Count > 1)
                    {
                        baseline.Items.AddRange(view.Entries.Skip(1).ToArray());
                        baseline.SelectedIndex = view.Selected - 1;
                    }
                    else
                    {
                        baseline.Items.Add(T("Sin comparación"));
                        baseline.SelectedIndex = 0;
                    }
                    summary.Text = view.Entries.Count == 0 ? T("Guarda la primera medición de esta carpeta.")
                        : T("Última medición {0} · {1:N0} archivos", language.Date(view.Entries[0].Date, true), view.Files);
                    warning.Visible = view.CurrentOmitted > 0 || view.PreviousOmitted > 0;
                    warning.Text = T("Medición parcial · {0:N0} entradas omitidas ahora · {1:N0} antes", view.CurrentOmitted, view.PreviousOmitted);
                    tips.SetToolTip(warning, T("Si una medición es parcial, un archivo ausente en esa medición queda sin confirmar."));
                    RenderResults();
                }
                catch (OperationCanceledException)
                {
                    summary.Text = T("Medición cancelada.");
                }
                catch (FileScanLimitException)
                {
                    summary.Text = T("Esta carpeta supera el límite de una medición por archivo. Elige una subcarpeta.");
                }
                catch (Exception ex)
                {
                    if (!Scanner.IsFileError(ex) && !(ex is InvalidDataException) && !(ex is ArgumentException) && !(ex is NotSupportedException))
                        throw;
                    summary.Text = T("No se pudo leer o guardar la medición de archivos.");
                }
                finally
                {
                    if (view != null && view.Entries.Count > 1 && baseline.Items.Count == view.Entries.Count - 1)
                        baseline.SelectedIndex = view.Selected - 1;
                    work = null;
                    source.Dispose();
                    SetControls();
                    if (closing)
                        Close();
                }
            }

            private ViewData LoadView(bool capture, HistoryEntry selected, IProgress<ScanProgress> progress, CancellationToken token)
            {
                FileSnapshot newScan = null;
                if (capture)
                {
                    newScan = new Scanner(token, progress).ScanFiles(folder);
                    history.Save(folder, newScan, token);
                }
                var data = new ViewData { Entries = history.List(folder) };
                if (data.Entries.Count == 0)
                    return data;
                token.ThrowIfCancellationRequested();
                var current = newScan != null && data.Entries[0].Date == newScan.Date ? newScan : history.Read(data.Entries[0], token);
                data.Files = current.Files.Count;
                data.CurrentOmitted = current.Omitted;
                if (data.Entries.Count == 1)
                    return data;
                data.Selected = selected == null ? 1 : Math.Max(1, data.Entries.FindIndex(1, entry => entry.FilePath == selected.FilePath));
                var previous = history.Read(data.Entries[data.Selected], token);
                token.ThrowIfCancellationRequested();
                data.PreviousOmitted = previous.Omitted;
                data.Changes = FileChange.Between(previous, current)
                    .OrderByDescending(change => change.Delta).ThenBy(change => change.Path, StringComparer.OrdinalIgnoreCase).ToArray();
                return data;
            }

            private void RenderResults()
            {
                string query = search.Text.Trim();
                int kind = filter.SelectedIndex;
                int matches = 0;
                var entries = new List<ListViewItem>();
                if (view != null)
                {
                    IEnumerable<FileChange> ordered = descending ? view.Changes :
                        view.Changes.Where(change => change.Delta.HasValue).Reverse().Concat(view.Changes.Where(change => !change.Delta.HasValue));
                    foreach (var change in ordered)
                    {
                        if ((kind > 0 && (int)change.Kind != kind) ||
                            (query.Length > 0 && language.Culture.CompareInfo.IndexOf(change.Path, query, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) < 0))
                            continue;
                        if (++matches <= DisplayLimit)
                            entries.Add(CreateRow(change));
                    }
                }
                files.BeginUpdate();
                try
                {
                    files.Items.Clear();
                    files.SetSort(2, descending, language);
                    files.Items.AddRange(entries.ToArray());
                }
                finally { files.EndUpdate(); }
                if (view == null || view.Entries.Count == 0)
                    resultCount.Text = "";
                else if (view.Entries.Count == 1)
                    resultCount.Text = T("Guarda otra medición para comparar los tamaños de archivo.");
                else if (matches == 0)
                    resultCount.Text = T("No hay cambios de tamaño para mostrar.");
                else
                    resultCount.Text = matches > DisplayLimit ? T("{0:N0} de {1:N0} cambios · Usa Buscar archivo para acotar", DisplayLimit, matches)
                        : T(matches == 1 ? "{0:N0} cambio" : "{0:N0} cambios", matches);
                ResizeFileColumn();
                UpdateSelection();
            }

            private ListViewItem CreateRow(FileChange change)
            {
                var row = new ListViewItem(change.Path) { Tag = change, ToolTipText = Path.Combine(folder, change.Path), UseItemStyleForSubItems = false };
                row.SubItems.Add(T(kinds[(int)change.Kind]));
                long? delta = change.Delta;
                row.SubItems.Add(delta.HasValue ? (delta.Value > 0 ? "+" : delta.Value < 0 ? "−" : "") +
                    FormatBytes(Math.Abs(delta.Value), language.Culture) : "—").ForeColor = delta.HasValue ? ChangeColor(delta.Value) : Muted;
                row.SubItems.Add(change.Before.HasValue ? FormatBytes(change.Before.Value, language.Culture) : "—").ForeColor = Muted;
                row.SubItems.Add(change.Now.HasValue ? FormatBytes(change.Now.Value, language.Culture) : "—");
                return row;
            }

            private void SetControls()
            {
                measure.Enabled = work == null || (capturing && !work.IsCancellationRequested);
                measure.Text = T(work != null && capturing ? "Cancelar" : "Guardar medición");
                baseline.Enabled = work == null && view != null && view.Entries.Count > 1;
                search.Enabled = filter.Enabled = work == null && view != null && view.Entries.Count > 1;
                UpdateSelection();
            }

            private void ResizeFileColumn()
            {
                if (files.ClientSize.Width <= 0)
                    return;
                int width = Math.Max(185, files.ClientSize.Width - files.Columns.Cast<ColumnHeader>().Skip(1).Sum(column => column.Width) -
                    SystemInformation.VerticalScrollBarWidth - 5);
                if (files.Columns[0].Width != width)
                    files.Columns[0].Width = width;
            }

            private void UpdateSelection()
            {
                copy.Enabled = files.SelectedItems.Count == 1;
                copy.Text = T("Copiar ruta");
                detail.Text = copy.Enabled ? files.SelectedItems[0].ToolTipText : T("Selecciona un archivo.");
                tips.SetToolTip(detail, copy.Enabled ? detail.Text : "");
            }

            private void CopyPath()
            {
                if (files.SelectedItems.Count != 1)
                    return;
                try { Clipboard.SetText(files.SelectedItems[0].ToolTipText); copy.Text = T("Ruta copiada"); }
                catch (ExternalException) { MessageBox.Show(this, T("El portapapeles está ocupado. Vuelve a intentar copiar la ruta."), Text); }
            }

            private sealed class ViewData
            {
                public List<HistoryEntry> Entries;
                public int Selected;
                public int Files;
                public long CurrentOmitted;
                public long PreviousOmitted;
                public FileChange[] Changes = new FileChange[0];
            }
        }
    }
}
