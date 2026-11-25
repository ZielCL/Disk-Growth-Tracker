using System;
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
            string stage = null;
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
                    if (MessageBox.Show(this, T("Está disponible la versión {0}. ¿Descargarla y reiniciar la app para instalarla?", nextVersion),
                        T("Actualización"), MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                        MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                        return;

                    SetStatus("Descargando y verificando la versión {0}…", nextVersion);
                    string executable = Application.ExecutablePath;
                    stage = await updater.Prepare(release, executable, cancellation.Token);
                    cancellation.Token.ThrowIfCancellationRequested();
                    PortableUpdate.LaunchHelper(stage, executable);
                    stage = null;
                    Close();
                }
            }
            catch (Exception ex)
            {
                if (!PortableUpdate.IsUpdateError(ex))
                    throw;
                if (!IsDisposed && !cancellation.IsCancellationRequested)
                    MessageBox.Show(this, T("No se pudo descargar o verificar la actualización. Puedes volver a intentarlo desde el botón."),
                        T("Actualización"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                if (stage != null)
                    PortableUpdate.Cleanup(stage);
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
