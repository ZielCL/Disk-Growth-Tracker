using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace DiskGrowth
{
    internal sealed partial class DiskWindow
    {
        private static void ShowExplanation(IWin32Window owner, string path, bool isFile, UiLanguage language)
        {
            using (var window = new ExplanationWindow(path, isFile, language))
                window.ShowDialog(owner);
        }

        private sealed class ExplanationWindow : Form
        {
            private readonly FlowLayoutPanel description = new FlowLayoutPanel();
            private readonly ToolTip tips = new ToolTip();
            private readonly UiLanguage language;
            private readonly Font captionFont = new Font("Segoe UI", 10F, FontStyle.Bold);
            private readonly Font titleFont = new Font("Segoe UI", 13F, FontStyle.Bold);

            public ExplanationWindow(string path, bool isFile, UiLanguage language)
            {
                this.language = language;
                var explanation = PathExplanation.Identify(path, isFile);
                Text = language.Text("¿Qué es esto?");
                StartPosition = FormStartPosition.CenterParent;
                ShowInTaskbar = false;
                ClientSize = new Size(690, 390);
                MinimumSize = new Size(610, 395);
                Font = new Font("Segoe UI", 10F);
                AutoScaleMode = AutoScaleMode.Dpi;
                BackColor = Background;
                ForeColor = Ink;

                var layout = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3,
                    Padding = new Padding(18, 14, 18, 11)
                };
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 72F));
                layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 45F));
                Controls.Add(layout);

                var heading = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill, BackColor = Hero, ColumnCount = 1, RowCount = 2,
                    Margin = new Padding(0, 0, 0, 9), Padding = new Padding(12, 5, 12, 5)
                };
                heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
                heading.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
                heading.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
                heading.Controls.Add(new Label
                {
                    Text = Text, Dock = DockStyle.Fill, Margin = Padding.Empty,
                    ForeColor = Color.White, Font = new Font("Segoe UI", 17F, FontStyle.Bold)
                }, 0, 0);
                var pathLabel = new PathLabel
                {
                    Text = path, AccessibleName = language.Text("Ruta del elemento"), Dock = DockStyle.Fill,
                    Margin = Padding.Empty, ForeColor = Color.FromArgb(210, 224, 250)
                };
                tips.SetToolTip(pathLabel, path);
                heading.Controls.Add(pathLabel, 0, 1);
                layout.Controls.Add(heading, 0, 0);

                description.Dock = DockStyle.Fill;
                description.Margin = new Padding(0, 0, 0, 8);
                description.Padding = new Padding(13, 10, 12, 10);
                description.BackColor = Color.White;
                description.BorderStyle = BorderStyle.FixedSingle;
                description.FlowDirection = FlowDirection.TopDown;
                description.WrapContents = false;
                description.AutoScroll = true;
                description.AccessibleName = language.Text("Explicación del elemento");
                layout.Controls.Add(description, 0, 1);
                FillDescription(explanation, isFile);
                description.SizeChanged += (sender, args) => FitDescription();
                FitDescription();

                var actions = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty
                };
                actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
                actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170F));
                actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 95F));
                string source = explanation.Location == null ? null : explanation.Location.Source;
                var documentation = new Button();
                StyleButton(documentation, language.Text("Abrir fuente oficial"), false);
                documentation.Visible = source != null;
                documentation.Click += (sender, args) => OpenSource(source);
                actions.Controls.Add(documentation, 1, 0);
                var close = new Button { DialogResult = DialogResult.OK };
                StyleButton(close, language.Text("Cerrar"), true);
                actions.Controls.Add(close, 2, 0);
                layout.Controls.Add(actions, 0, 2);
                AcceptButton = CancelButton = close;
                ActiveControl = close;
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                    tips.Dispose();
                base.Dispose(disposing);
                if (disposing)
                {
                    captionFont.Dispose();
                    titleFont.Dispose();
                }
            }

            private void FillDescription(PathExplanation explanation, bool isFile)
            {
                if (explanation.Location != null)
                {
                    string basis = isFile && !explanation.IdentifiesFile
                        ? "Contexto de la carpeta que lo contiene" : "Identificado por la ruta";
                    AddExplanation(language.Text(basis), explanation.Location);
                }
                if (explanation.Format != null)
                    AddExplanation(language.Text("Formato sugerido por la extensión {0}", explanation.Extension), explanation.Format);
                if (explanation.Location == null && explanation.Format == null)
                {
                    AddText(language.Text("Sin coincidencia conocida"), titleFont, Ink, 4);
                    AddText(language.Text(isFile
                        ? "El nombre, la extensión y la ruta disponibles no permiten identificar para qué se usa este archivo."
                        : "Esta ruta no coincide con una ubicación del catálogo. Para averiguar su función, revisa qué aplicación o proyecto usa la carpeta."), Font, Ink, 0);
                }
                else if (explanation.Format != null && explanation.Location == null)
                {
                    AddText(language.Text("La extensión orienta sobre el formato. El programa que lo creó sigue sin identificar."), Font, Muted, 0);
                }
            }

            private void AddExplanation(string basis, Explanation content)
            {
                bool english = language.Code == "en";
                AddText(basis, captionFont, Blue, 1);
                AddText(english ? content.EnglishTitle : content.SpanishTitle, titleFont, Ink, 3);
                AddText(english ? content.EnglishDetails : content.SpanishDetails, Font, Ink, 13);
            }

            private void AddText(string text, Font font, Color color, int bottomSpacing)
            {
                description.Controls.Add(new Label
                {
                    Text = text, Font = font, ForeColor = color, AutoSize = true,
                    UseMnemonic = false, Margin = new Padding(0, 0, 0, bottomSpacing)
                });
            }

            private void FitDescription()
            {
                int width = Math.Max(120, description.ClientSize.Width - description.Padding.Horizontal -
                    SystemInformation.VerticalScrollBarWidth - 4);
                description.SuspendLayout();
                foreach (Control label in description.Controls)
                    label.MaximumSize = new Size(width, 0);
                description.ResumeLayout();
            }

            private void OpenSource(string source)
            {
                if (source == null)
                    return;
                try
                {
                    Process.Start(new ProcessStartInfo(source) { UseShellExecute = true });
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    MessageBox.Show(this, language.Text("No se pudo abrir la fuente oficial."), Text);
                }
            }
        }
    }
}
