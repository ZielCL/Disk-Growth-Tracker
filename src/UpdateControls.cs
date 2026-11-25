using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DiskGrowth
{
    internal sealed partial class DiskWindow
    {
        private async Task CheckUpdates()
        {
            if (updateCancellation != null || activeScan != null || loading)
                return;
            var cancellation = new CancellationTokenSource();
            updateCancellation = cancellation;
            UpdateControls();
            try
            {
                using (var updater = new PortableUpdate())
                {
                    SetStatus("Buscando actualizaciones…");
                    UpdateRelease release = await updater.Check(cancellation.Token);
                    cancellation.Token.ThrowIfCancellationRequested();
                    var currentVersion = Assembly.GetExecutingAssembly().GetName().Version;
                    if (release.Version <= currentVersion)
                    {
                        MessageBox.Show(this, T("Ya tienes la versión más reciente ({0}).", currentVersion.ToString(3)),
                            T("Actualización"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }
                    string nextVersion = release.Version.ToString(3);
                    if (MessageBox.Show(this, T("Está disponible la versión {0}. ¿Abrir su página de descarga en GitHub?", nextVersion),
                        T("Actualización"), MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                        MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                        return;
                    using (Process.Start(new ProcessStartInfo(release.DownloadPage.AbsoluteUri) { UseShellExecute = true })) { }
                }
            }
            catch (Exception ex)
            {
                if (!(ex is HttpRequestException || ex is OperationCanceledException || ex is IOException || ex is InvalidDataException ||
                    ex is ArgumentException || ex is Win32Exception || ex is ObjectDisposedException))
                    throw;
                if (!IsDisposed && !cancellation.IsCancellationRequested)
                    MessageBox.Show(this, T("No se pudo consultar la actualización o abrir GitHub. Vuelve a intentarlo."),
                        T("Actualización"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                updateCancellation = null;
                cancellation.Dispose();
                if (!IsDisposed)
                {
                    SetHistoryStatus();
                    UpdateControls();
                }
            }
        }
    }
}
