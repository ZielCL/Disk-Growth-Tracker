using System;
using System.Collections.Generic;
using System.IO;

namespace DiskGrowth
{
    internal sealed class Explanation
    {
        public readonly string SpanishTitle;
        public readonly string EnglishTitle;
        public readonly string SpanishDetails;
        public readonly string EnglishDetails;
        public readonly string Source;

        public Explanation(string spanishTitle, string englishTitle, string spanishDetails, string englishDetails, string source = null)
        {
            SpanishTitle = spanishTitle;
            EnglishTitle = englishTitle;
            SpanishDetails = spanishDetails;
            EnglishDetails = englishDetails;
            Source = source;
        }
    }

    internal sealed class PathExplanation
    {
        private static readonly Explanation Clipchamp = new Explanation(
            "Almacenamiento de Clipchamp", "Clipchamp local storage",
            "Clipchamp guarda aquí datos que usa su editor. La carpeta IndexedDB puede incluir copias temporales de los videos, imágenes y audios que usas en tus proyectos. Antes de limpiar ese almacenamiento, comprueba que conservas los originales o su copia de seguridad.",
            "Clipchamp stores editor data here. Its IndexedDB folder can contain temporary copies of videos, images, and audio used in your projects. Before clearing that storage, check that you have the original media or a backup.",
            "https://support.microsoft.com/en-us/clipchamp/why-does-clipchamp-store-temporary-files-on-my-computer");

        private static readonly Explanation Steam = new Explanation(
            "Biblioteca de juegos de Steam", "Steam game library",
            "Steam suele instalar los archivos de sus juegos dentro de steamapps\\common. Puedes ver qué juegos ocupan espacio y gestionar sus instalaciones desde la sección Almacenamiento de Steam.",
            "Steam usually installs game files under steamapps\\common. Use Steam's Storage settings to see how much space each game uses or manage its installation.",
            "https://help.steampowered.com/en/faqs/view/4BD4-4528-6B2E-8327");

        private const string DiskCleanup = "https://learn.microsoft.com/windows-server/administration/windows-commands/cleanmgr";

        private static readonly Dictionary<string, Explanation> WindowsFolders = new Dictionary<string, Explanation>(StringComparer.OrdinalIgnoreCase)
        {
            { "WinSxS", new Explanation("Almacén de componentes de Windows", "Windows component store",
                "Windows guarda aquí componentes que necesita para instalar actualizaciones, activar características y reparar el sistema. Windows administra la limpieza de este almacén.",
                "Windows keeps components here for updates, optional features, and system repair. Windows manages cleanup of this store.",
                "https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/clean-up-the-winsxs-folder?view=windows-11") },
            { "Installer", new Explanation("Caché de Windows Installer", "Windows Installer cache",
                "Contiene archivos que Windows Installer necesita para actualizar, reparar o desinstalar aplicaciones. Conserva sus archivos para que esas operaciones sigan funcionando.",
                "Contains files Windows Installer needs to update, repair, or uninstall apps. Keep these files so those operations continue to work.",
                "https://learn.microsoft.com/en-us/troubleshoot/windows-client/application-management/missing-windows-installer-cache") },
            { "Temp", new Explanation("Archivos temporales de Windows", "Windows temporary files",
                "Windows y algunos programas usan esta carpeta durante tareas como la instalación. Para revisar los temporales que Windows puede limpiar, usa Configuración → Sistema → Almacenamiento.",
                "Windows and some apps use this folder during tasks such as installation. To review temporary files that Windows can clean up, use Settings → System → Storage.", DiskCleanup) }
        };

        private static readonly Explanation RecycleBin = new Explanation(
            "Papelera de reciclaje", "Recycle Bin",
            "Windows guarda aquí archivos eliminados que todavía puedes restaurar desde la Papelera de reciclaje. Los elementos de la papelera siguen ocupando espacio hasta que se eliminan definitivamente.",
            "Windows stores deleted files here that you can still restore from the Recycle Bin. They take up drive space until they are permanently deleted.", DiskCleanup);
        private static readonly Explanation UserTemp = new Explanation(
            "Archivos temporales del usuario", "User temporary files",
            "Las aplicaciones usan esta ubicación para archivos de trabajo temporales. Algunos pueden estar en uso mientras la aplicación sigue abierta.",
            "Apps use this location for temporary working files. Some may still be in use while an app is open.", DiskCleanup);
        private static readonly Explanation AppData = new Explanation(
            "Datos de aplicaciones del perfil", "App data for this profile",
            "Las aplicaciones pueden guardar en AppData configuraciones, cachés y datos que usan para funcionar. La función de cada elemento depende de la aplicación que lo creó.",
            "Apps can store settings, caches, and working data in AppData. Each item's purpose depends on the app that created it.");
        private static readonly Explanation ProgramFiles = new Explanation(
            "Ubicación habitual de programas instalados", "Common location for installed programs",
            "Esta es una ubicación habitual para archivos de programas y juegos instalados. Si quieres quitar una aplicación, usa su opción de desinstalación en Configuración o en su lanzador.",
            "This is a common location for installed program and game files. To remove an app, use its uninstall option in Settings or in its launcher.");
        private static readonly Explanation Downloads = new Explanation(
            "Carpeta habitual de descargas", "Common downloads folder",
            "Esta ubicación suele contener descargas del navegador y de otras aplicaciones. Puede incluir instaladores, documentos y otros archivos que decidiste guardar.",
            "This location commonly holds downloads from your browser and other apps, including installers, documents, and files you chose to save.");

        private static readonly Dictionary<string, Explanation> RootFiles = new Dictionary<string, Explanation>(StringComparer.OrdinalIgnoreCase)
        {
            { "pagefile.sys", new Explanation("Archivo de paginación de Windows", "Windows page file",
                "Windows usa este archivo como respaldo en disco de parte de la memoria virtual. También puede ser necesario para guardar información tras un fallo del sistema.",
                "Windows uses this file as disk backing for some virtual memory. It may also be needed to save information after a system crash.",
                "https://learn.microsoft.com/en-us/troubleshoot/windows-client/performance/introduction-to-the-page-file") },
            { "hiberfil.sys", new Explanation("Archivo de hibernación de Windows", "Windows hibernation file",
                "Windows guarda en este archivo el estado del sistema necesario para reanudarlo después de hibernar.",
                "Windows uses this file to save the system state it needs to resume after hibernation.",
                "https://learn.microsoft.com/en-us/windows/win32/power/system-power-states") }
        };

        private static readonly Dictionary<string, Explanation> Extensions = BuildExtensions();
        public Explanation Location { get; private set; }
        public Explanation Format { get; private set; }
        public string Extension { get; private set; }
        public bool IdentifiesFile { get; private set; }

        public static PathExplanation Identify(string path, bool isFile)
        {
            string root = Path.GetPathRoot(path) ?? "";
            string[] parts = path.Substring(root.Length).Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
            string extension = isFile ? Path.GetExtension(path) : "";
            Explanation format = null;
            if (!string.IsNullOrEmpty(extension))
                Extensions.TryGetValue(extension, out format);
            Explanation knownFile = null;
            if (isFile && IsDrive(root) && parts.Length == 1)
                RootFiles.TryGetValue(parts[0], out knownFile);
            return new PathExplanation
            {
                Location = knownFile ?? IdentifyLocation(parts, root, isFile),
                Format = format,
                Extension = extension,
                IdentifiesFile = knownFile != null
            };
        }

        private static Explanation IdentifyLocation(string[] parts, string root, bool isFile)
        {
            int folders = isFile ? parts.Length - 1 : parts.Length;
            if (folders >= 10 && At(parts, 0, "Users") &&
                Matches(parts, 2, "AppData", "Local", "Packages", "Clipchamp.Clipchamp_yxz26nhyzhsrt", "LocalState", "EBWebView", "Default", "IndexedDB"))
                return Clipchamp;

            for (int i = 0; i + 1 < folders; i++)
                if (Matches(parts, i, "steamapps", "common"))
                    return Steam;

            if (!IsDrive(root) || parts.Length == 0)
                return null;

            Explanation match;
            if (folders >= 2 && At(parts, 0, "Windows") && WindowsFolders.TryGetValue(parts[1], out match))
                return match;
            if (folders >= 1 && At(parts, 0, "$Recycle.Bin"))
                return RecycleBin;
            if (folders >= 1 && (At(parts, 0, "Program Files") || At(parts, 0, "Program Files (x86)")))
                return ProgramFiles;
            if (folders < 3 || !At(parts, 0, "Users"))
                return null;
            if (folders >= 5 && Matches(parts, 2, "AppData", "Local", "Temp"))
                return UserTemp;
            if (At(parts, 2, "AppData"))
                return AppData;
            return At(parts, 2, "Downloads") ? Downloads : null;
        }

        private static bool IsDrive(string root)
        {
            return (root.Length >= 2 && root[1] == ':') ||
                (root.Length >= 6 && root.StartsWith(@"\\?\", StringComparison.Ordinal) && root[5] == ':');
        }

        private static bool Matches(string[] path, int start, params string[] segments)
        {
            for (int i = 0; i < segments.Length; i++)
                if (!At(path, start + i, segments[i]))
                    return false;
            return true;
        }

        private static bool At(string[] path, int index, string value)
        {
            return index < path.Length && string.Equals(path[index], value, StringComparison.OrdinalIgnoreCase);
        }

        private static Dictionary<string, Explanation> BuildExtensions()
        {
            var results = new Dictionary<string, Explanation>(StringComparer.OrdinalIgnoreCase);
            Add(results, new Explanation("Archivo de video", "Video file",
                "Este formato suele contener video y puede incluir audio, subtítulos u otras pistas.",
                "This format usually contains video and can include audio, subtitles, or other tracks."), ".mp4", ".mkv", ".mov", ".avi", ".webm", ".wmv");
            Add(results, new Explanation("Archivo de audio", "Audio file",
                "Este formato se usa para música, grabaciones o pistas de audio.",
                "This format is used for music, recordings, or audio tracks."), ".mp3", ".wav", ".flac", ".m4a", ".aac", ".ogg", ".opus");
            Add(results, new Explanation("Archivo de imagen", "Image file",
                "Este formato se usa para capturas, fotografías u otros gráficos.",
                "This format is used for screenshots, photos, or other graphics."), ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".tiff", ".heic", ".gif", ".svg");
            Add(results, new Explanation("Archivo comprimido", "Archive file",
                "Un archivo comprimido puede reunir varios archivos o carpetas en un solo paquete.",
                "An archive can bundle multiple files or folders into one package."), ".zip", ".7z", ".rar", ".gz", ".xz");
            Add(results, new Explanation("Imagen de disco", "Disk image",
                "Suele representar el contenido de un disco óptico o de un disco virtual.",
                "Usually represents the contents of an optical disc or virtual disk."), ".iso", ".img", ".vhd", ".vhdx", ".vmdk", ".vdi");
            Add(results, new Explanation("Paquete de instalación", "Installer package",
                "Este formato se usa para instalar o actualizar software en Windows.",
                "This format is used to install or update software on Windows."), ".msi", ".msp", ".msix", ".appx", ".msixbundle", ".appxbundle");
            Add(results, new Explanation("Programa ejecutable", "Executable program",
                "Windows usa este formato para programas que se pueden iniciar, incluidos algunos instaladores.",
                "Windows uses this format for runnable programs, including some installers."), ".exe");
            Add(results, new Explanation("Biblioteca de Windows", "Windows library",
                "Puede contener código o recursos compartidos que Windows o una aplicación carga al funcionar.",
                "May contain shared code or resources that Windows or an app loads while running."), ".dll");
            Add(results, new Explanation("Base de datos SQLite", "SQLite database",
                "Este formato puede contener datos de una aplicación, como configuraciones, índices o historial.",
                "This format can store app data such as settings, indexes, or history."), ".sqlite", ".sqlite3");
            Add(results, new Explanation("Archivo temporal", "Temporary file",
                "La extensión se usa habitualmente para datos de trabajo de una aplicación.",
                "This extension is commonly used for an app's working data."), ".tmp");
            Add(results, new Explanation("Archivo de registro", "Log file",
                "Puede contener eventos o mensajes de diagnóstico de una aplicación o del sistema.",
                "May contain events or diagnostic messages from an app or the system."), ".log");
            Add(results, new Explanation("Documento", "Document",
                "Este formato suele contener un documento de texto o una copia lista para compartir.",
                "This format usually contains a text document or a copy ready to share."), ".pdf", ".doc", ".docx", ".odt", ".rtf", ".txt");
            Add(results, new Explanation("Hoja de cálculo o datos tabulares", "Spreadsheet or tabular data",
                "Este formato se usa para organizar datos en filas y columnas.",
                "This format is used to organize data in rows and columns."), ".xls", ".xlsx", ".ods", ".csv", ".tsv");
            return results;
        }

        private static void Add(Dictionary<string, Explanation> table, Explanation explanation, params string[] extensions)
        {
            foreach (string extension in extensions)
                table.Add(extension, explanation);
        }
    }
}
