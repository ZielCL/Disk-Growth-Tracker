using System;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
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
            string downloaded = null;
            UpdateControls();
            try
            {
                using (var updater = new PortableUpdate())
                {
                    SetStatus("Buscando actualizaciones…");
                    UpdateRelease release = await updater.Check(cancellation.Token);
                    cancellation.Token.ThrowIfCancellationRequested();
                    AssemblyName current = Assembly.GetExecutingAssembly().GetName();
                    var currentVersion = current.Version;
                    if (release.Version <= currentVersion)
                    {
                        MessageBox.Show(this, T("Ya tienes la versión más reciente ({0}).", currentVersion.ToString(3)),
                            T("Actualización"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }
                    string nextVersion = release.Version.ToString(3);
                    if (MessageBox.Show(this, T("Está disponible la versión {0}. ¿Descargarla y abrirla ahora?", nextVersion),
                        T("Actualización"), MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                        MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                        return;
                    SetStatus("Descargando y verificando la versión {0}…", nextVersion);
                    downloaded = await updater.Download(release, Path.Combine(Program.DataDirectory, "Versions"), current, cancellation.Token);
                    cancellation.Token.ThrowIfCancellationRequested();
                    using (var next = PortableUpdate.Open(release, downloaded, current.Name, Handle))
                        if (next == null || await Task.Run(() => next.WaitForExit(800)))
                            throw new IOException();
                    downloaded = null;
                    if (!IsDisposed)
                        Close();
                }
            }
            catch (Exception ex)
            {
                if (!(ex is HttpRequestException || ex is OperationCanceledException || ex is IOException || ex is InvalidDataException ||
                    ex is ArgumentException || ex is Win32Exception || ex is ObjectDisposedException ||
                    ex is BadImageFormatException || ex is COMException || ex is UnauthorizedAccessException))
                    throw;
                if (!IsDisposed && !cancellation.IsCancellationRequested)
                    MessageBox.Show(this, T("No se pudo completar la actualización. Si Windows bloqueó el archivo, revisa el aviso antes de volver a intentarlo."),
                        T("Actualización"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                PortableUpdate.Discard(downloaded);
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
