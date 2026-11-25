using System;
using System.Diagnostics;
using System.Drawing;
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
        private sealed class LargestFilesWindow : Form
        {
            private const int ResultLimit = 100;
            private readonly string folder;
            private readonly string folderPrefix;
            private readonly UiLanguage language;
            private readonly ResultsList files = new ResultsList("Archivo", "Tamaño", "Carpeta");
            private readonly PathLabel selectedPath = new PathLabel();
            private readonly Label summary = new Label();
            private readonly Label quality = new Label();
            private readonly Button refresh = new Button();
            private readonly Button copyFile = new Button();
            private readonly Button revealFile = new Button();
            private readonly ProgressBar progress = new ProgressBar();
            private readonly ToolTip tips = new ToolTip();
            private CancellationTokenSource active;
            private bool closing;

            public LargestFilesWindow(string folder, UiLanguage language)
            {
                this.folder = folder;
                this.language = language;
                folderPrefix = folder.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                Text = T("Archivos más grandes");
                StartPosition = FormStartPosition.CenterParent;
                ShowInTaskbar = false;
                ClientSize = new Size(880, 525);
                MinimumSize = new Size(720, 420);
                Font = new Font("Segoe UI", 10F);
                AutoScaleMode = AutoScaleMode.Dpi;
                BackColor = Background;
                ForeColor = Ink;
                BuildFilesLayout();
                UpdateActions();

                Shown += async (sender, args) => await FindFiles();
                refresh.Click += async (sender, args) => await FindFiles();
                copyFile.Click += (sender, args) => CopyFilePath();
                revealFile.Click += (sender, args) => RevealFilePath();
                files.SelectedIndexChanged += (sender, args) => UpdateActions();
                files.DoubleClick += (sender, args) => RevealFilePath();
                files.KeyDown += (sender, args) =>
                {
                    if (args.KeyCode != Keys.Enter)
                        return;
                    args.SuppressKeyPress = true;
                    RevealFilePath();
                };
                files.ClientSizeChanged += (sender, args) => ResizeFileColumns();
                FormClosing += (sender, args) =>
                {
                    if (active == null)
                        return;
                    closing = true;
                    args.Cancel = true;
                    active.Cancel();
                };
            }

            private string T(string key, params object[] args)
            {
                return language.Text(key, args);
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                    tips.Dispose();
                base.Dispose(disposing);
            }

            private void BuildFilesLayout()
            {
                var layout = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(18, 14, 18, 8)
                };
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 77F));
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 39F));
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 53F));
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 8F));
                Controls.Add(layout);

                var heading = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill, BackColor = Hero, ColumnCount = 1, RowCount = 2,
                    Padding = new Padding(13, 6, 13, 5), Margin = new Padding(0, 0, 0, 8)
                };
                heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
                heading.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
                heading.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
                heading.Controls.Add(new Label
                {
                    Text = Text, Dock = DockStyle.Fill, Margin = Padding.Empty, ForeColor = Color.White,
                    Font = new Font("Segoe UI", 17F, FontStyle.Bold)
                }, 0, 0);
                var source = new PathLabel
                {
                    Text = folder, Dock = DockStyle.Fill, Margin = Padding.Empty, ForeColor = Color.FromArgb(210, 224, 250)
                };
                tips.SetToolTip(source, folder);
                heading.Controls.Add(source, 0, 1);
                layout.Controls.Add(heading, 0, 0);

                var toolbar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
                toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
                toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 144F));
                summary.Dock = DockStyle.Fill;
                summary.ForeColor = Muted;
                summary.TextAlign = ContentAlignment.MiddleLeft;
                summary.AutoEllipsis = true;
                summary.Margin = new Padding(0, 0, 12, 5);
                summary.Text = T("Preparando la búsqueda…");
                toolbar.Controls.Add(summary, 0, 0);
                StyleButton(refresh, T("Actualizar"), true);
                toolbar.Controls.Add(refresh, 1, 0);
                layout.Controls.Add(toolbar, 0, 1);

                quality.Dock = DockStyle.Fill;
                quality.AutoSize = true;
                quality.Margin = new Padding(0, 0, 0, 7);
                quality.Padding = new Padding(8, 5, 8, 5);
                quality.BackColor = WarningFill;
                quality.ForeColor = WarningInk;
                quality.Visible = false;
                layout.Controls.Add(quality, 0, 2);

                files.Dock = DockStyle.Fill;
                files.View = View.Details;
                files.HeaderStyle = ColumnHeaderStyle.Nonclickable;
                files.FullRowSelect = true;
                files.MultiSelect = false;
                files.HideSelection = false;
                files.ShowItemToolTips = true;
                files.BorderStyle = BorderStyle.None;
                files.ForeColor = Ink;
                files.AccessibleName = T("Archivos ordenados por tamaño");
                files.Columns.Add(T("Archivo"), 340);
                files.Columns.Add(T("Tamaño"), 125, HorizontalAlignment.Right);
                files.Columns.Add(T("Carpeta"), 260);
                files.SetSort(1, true, language);
                var listPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(1), BackColor = Border, Margin = Padding.Empty };
                listPanel.Controls.Add(files);
                layout.Controls.Add(listPanel, 0, 3);

                var actions = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = new Padding(0, 7, 0, 0),
                    Padding = new Padding(10, 0, 7, 0), BackColor = Tint
                };
                actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
                actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 121F));
                actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 176F));
                selectedPath.Dock = DockStyle.Fill;
                selectedPath.Margin = new Padding(0, 0, 10, 0);
                selectedPath.ForeColor = Muted;
                actions.Controls.Add(selectedPath, 0, 0);
                StyleButton(copyFile, T("Copiar ruta"), false);
                StyleButton(revealFile, T("Mostrar en Explorador"), false);
                copyFile.Margin = new Padding(0, 6, 7, 6);
                revealFile.Margin = new Padding(0, 6, 0, 6);
                actions.Controls.Add(copyFile, 1, 0);
                actions.Controls.Add(revealFile, 2, 0);
                layout.Controls.Add(actions, 0, 4);

                progress.Dock = DockStyle.Fill;
                progress.Style = ProgressBarStyle.Marquee;
                progress.MarqueeAnimationSpeed = 24;
                progress.Margin = new Padding(0, 3, 0, 0);
                progress.Visible = false;
                layout.Controls.Add(progress, 0, 5);
            }

            private async Task FindFiles()
            {
                if (active != null)
                {
                    active.Cancel();
                    refresh.Enabled = false;
                    summary.Text = T("Cancelando la búsqueda…");
                    return;
                }
                active = new CancellationTokenSource();
                var token = active.Token;
                refresh.Text = T("Cancelar");
                progress.Visible = true;
                summary.Text = T("Buscando en la carpeta y sus subcarpetas…");
                var reporter = new Progress<ScanProgress>(state =>
                {
                    if (active != null && !token.IsCancellationRequested)
                        summary.Text = T("Buscando… {0:N0} archivos revisados", state.Files);
                });
                try
                {
                    var result = await Task.Run(() => new Scanner(token, reporter).ScanLargest(folder, ResultLimit), token);
                    token.ThrowIfCancellationRequested();
                    ShowFiles(result);
                }
                catch (OperationCanceledException)
                {
                    summary.Text = T(files.Items.Count == 0 ? "Búsqueda cancelada." : "Búsqueda cancelada. Se muestran los resultados anteriores.");
                }
                catch (Exception ex)
                {
                    if (!Scanner.IsFileError(ex) && !(ex is ArgumentException) && !(ex is NotSupportedException))
                        throw;
                    summary.Text = T("No se pudo revisar la carpeta. {0}", T(ex.Message));
                    tips.SetToolTip(summary, summary.Text);
                }
                finally
                {
                    active.Dispose();
                    active = null;
                    refresh.Text = T("Actualizar");
                    refresh.Enabled = true;
                    progress.Visible = false;
                    if (closing)
                        Close();
                }
            }

            private void ShowFiles(LargestFiles result)
            {
                files.BeginUpdate();
                try
                {
                    files.Items.Clear();
                    files.Items.AddRange(result.Entries.Select(entry =>
                    {
                        string displayPath = entry.Path.StartsWith(folderPrefix, StringComparison.OrdinalIgnoreCase)
                            ? entry.Path.Substring(folderPrefix.Length) : entry.Path;
                        string subfolder = Path.GetDirectoryName(displayPath);
                        var item = new ListViewItem(Path.GetFileName(entry.Path))
                        {
                            Tag = entry, ToolTipText = entry.Path, UseItemStyleForSubItems = false
                        };
                        item.SubItems.Add(FormatBytes(entry.Bytes, language.Culture));
                        item.SubItems.Add(string.IsNullOrEmpty(subfolder) ? T("En esta carpeta") : subfolder).ForeColor = Muted;
                        return item;
                    }).ToArray());
                }
                finally
                {
                    files.EndUpdate();
                }
                if (result.Files == 0)
                    summary.Text = T("No se encontraron archivos que se pudieran medir.");
                else if (result.Files == 1)
                    summary.Text = T("1 archivo medido");
                else if (result.Entries.Length == result.Files)
                    summary.Text = T("{0:N0} archivos medidos", result.Files);
                else
                    summary.Text = T("Los {0:N0} más grandes de {1:N0} archivos medidos", result.Entries.Length, result.Files);
                tips.SetToolTip(summary, summary.Text);
                quality.Text = T("Resultados parciales") + " · " +
                    T(result.Errors == 1 ? "{0:N0} entrada sin acceso o lectura" : "{0:N0} entradas sin acceso o lectura", result.Errors);
                quality.Visible = result.Errors > 0;
                tips.SetToolTip(quality, T("Puntos de reanálisis omitidos, incluidos enlaces: {0:N0}.", result.Links));
                ResizeFileColumns();
                UpdateActions();
            }

            private void ResizeFileColumns()
            {
                int rest = files.ClientSize.Width - files.Columns[1].Width - SystemInformation.VerticalScrollBarWidth - 6;
                if (rest <= 0)
                    return;
                int names = Math.Max(190, rest * 56 / 100);
                int folders = Math.Max(140, rest - names);
                if (files.Columns[0].Width != names)
                    files.Columns[0].Width = names;
                if (files.Columns[2].Width != folders)
                    files.Columns[2].Width = folders;
            }

            private FileSize SelectedFile()
            {
                return files.SelectedItems.Count == 0 ? null : (FileSize)files.SelectedItems[0].Tag;
            }

            private void UpdateActions()
            {
                var entry = SelectedFile();
                copyFile.Enabled = revealFile.Enabled = entry != null;
                copyFile.Text = T("Copiar ruta");
                selectedPath.Text = entry == null ? T("Selecciona un archivo.") : entry.Path;
                tips.SetToolTip(selectedPath, entry == null ? "" : entry.Path);
            }

            private void CopyFilePath()
            {
                var entry = SelectedFile();
                if (entry == null)
                    return;
                try
                {
                    Clipboard.SetText(entry.Path);
                    copyFile.Text = T("Ruta copiada");
                }
                catch (ExternalException)
                {
                    MessageBox.Show(this, T("El portapapeles está ocupado. Vuelve a intentar copiar la ruta."), Text);
                }
            }

            private void RevealFilePath()
            {
                var entry = SelectedFile();
                if (entry == null)
                    return;
                try
                {
                    if (!File.Exists(entry.Path))
                        throw new FileNotFoundException("El archivo ya no está disponible en esa ruta.");
                    string explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
                    Process.Start(new ProcessStartInfo(explorer, "/select,\"" + entry.Path + "\"") { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    if (!Scanner.IsFileError(ex) && !(ex is System.ComponentModel.Win32Exception))
                        throw;
                    MessageBox.Show(this, T(ex.Message), Text);
                }
            }
        }
    }
}
