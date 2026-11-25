using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace DiskGrowth
{
    internal sealed class LanguagePreferences
    {
        private readonly string file;

        public LanguagePreferences(string directory)
        {
            file = Path.Combine(directory, "language.txt");
        }

        public string Load()
        {
            try
            {
                if (File.Exists(file))
                {
                    string saved = File.ReadAllText(file).Trim();
                    if (saved == "es" || saved == "en")
                        return saved;
                }
            }
            catch (Exception ex)
            {
                if (!Scanner.IsFileError(ex))
                    throw;
            }
            return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "es" ? "es" : "en";
        }

        public bool Save(string code)
        {
            string temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                File.WriteAllText(temporary, code);
                if (File.Exists(file))
                    File.Replace(temporary, file, null);
                else
                    File.Move(temporary, file);
                return true;
            }
            catch (Exception ex)
            {
                if (!Scanner.IsFileError(ex))
                    throw;
                return false;
            }
            finally
            {
                try
                {
                    if (File.Exists(temporary))
                        File.Delete(temporary);
                }
                catch (Exception ex)
                {
                    if (!Scanner.IsFileError(ex))
                        throw;
                }
            }
        }
    }

    internal sealed class UiLanguage
    {
        private static readonly Dictionary<string, string> English = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "¿Qué me está llenando el disco?", "What's filling up my drive?" },
            { "Uso del disco y cambios entre mediciones", "Disk usage and changes between scans" },
            { "Idioma", "Language" },
            { "Unidad o carpeta", "Drive or folder" },
            { "Abre la lista para ver unidades y ubicaciones medidas recientemente.", "Open the list to see drives and recently scanned locations." },
            { "Elegir carpeta", "Choose folder" },
            { "Medir ahora", "Scan now" },
            { "Medir la ubicación seleccionada · F5", "Scan the selected location · F5" },
            { "Tamaño de la carpeta", "Folder size" },
            { "Cambio de tamaño", "Size change" },
            { "Espacio libre en la unidad", "Free space on drive" },
            { "↑ Subir", "↑ Up" },
            { "Raíz", "Root" },
            { "Subir un nivel · Alt+↑", "Go up a level · Alt+↑" },
            { "Volver a la ubicación medida", "Return to the scanned location" },
            { "Comparar con", "Compare with" },
            { "Medición anterior", "Previous scan" },
            { "Buscar carpeta", "Find folder" },
            { "Busca por nombre en las subcarpetas. Ctrl+F para buscar, Esc para limpiar.", "Find a folder by name. Ctrl+F to search, Esc to clear." },
            { "Incluir subcarpetas", "Include subfolders" },
            { "Solo aumentos", "Growth only" },
            { "Borrar filtros", "Clear filters" },
            { "Carpetas y cambio de tamaño", "Folders and size changes" },
            { "Carpeta", "Folder" },
            { "Cambio", "Change" },
            { "Tamaño medido", "Scanned size" },
            { "Antes", "Before" },
            { "Primera medición", "First scan" },
            { "Copiar ruta", "Copy path" },
            { "Explorador", "File Explorer" },
            { "Medir carpeta", "Scan folder" },
            { "Archivos grandes", "Large files" },
            { "Copiar la ruta completa · Ctrl+Mayús+C", "Copy the full path · Ctrl+Shift+C" },
            { "Abrir la carpeta en el Explorador de archivos.", "Open this folder in File Explorer." },
            { "Guardar una medición para esta carpeta como ubicación independiente.", "Scan this folder and keep a separate history for it." },
            { "Buscar los archivos más grandes de esta carpeta y sus subcarpetas.", "Find the largest files in this folder and its subfolders." },
            { "Cerrando al terminar de guardar…", "Closing after saving…" },
            { "Cancelando la medición antes de cerrar…", "Canceling the scan before closing…" },
            { "Escribe una ruta completa, por ejemplo C:\\ o C:\\Videos.", "Enter a full path, for example C:\\ or C:\\Videos." },
            { "Cargando historial…", "Loading scan history…" },
            { "Sin comparación", "No comparison" },
            { "Pulsa Medir ahora para guardar la primera medición.", "Select Scan now to save the first scan." },
            { "Historial local · hasta {0} mediciones por ubicación", "Local history · up to {0} scans per location" },
            { "No se pudo cargar el historial: {0}", "Couldn't load scan history: {0}" },
            { "No se pudo cargar la medición elegida: {0}", "Couldn't load the selected scan: {0}" },
            { "Cancelando la medición…", "Canceling the scan…" },
            { "Midiendo… puedes seguir usando Windows.", "Scanning… you can keep using Windows." },
            { "Midiendo… {0:N0} carpetas · {1:N0} archivos", "Scanning… {0:N0} folders · {1:N0} files" },
            { "Guardando la medición…", "Saving the scan…" },
            { "Medición cancelada. Se conservó el historial anterior.", "Scan canceled. Previous scans are still available." },
            { "No se pudo completar la medición.", "Couldn't complete the scan." },
            { "Guardando…", "Saving…" },
            { "Cancelar", "Cancel" },
            { "Ubicación actual", "Current location" },
            { "Carpeta seleccionada", "Selected folder" },
            { "Lectura parcial", "Partially read" },
            { "Medición anterior parcial", "Previous scan was partial" },
            { "Solo en la medición anterior", "Only in the previous scan" },
            { "Primera medición pendiente", "First scan pending" },
            { "Medido el {0}", "Scanned {0}" },
            { "Disponible con dos mediciones", "Available after two scans" },
            { "Desde el {0}", "Since {0}" },
            { "0 resultados", "0 results" },
            { "Contenido de la carpeta", "Folder contents" },
            { "{0:N0} de {1:N0} resultados · Usa Buscar carpeta para acotar", "{0:N0} of {1:N0} results · Use Find folder to narrow the list" },
            { "{0:N0} resultado", "{0:N0} result" },
            { "{0:N0} resultados", "{0:N0} results" },
            { "El tamaño de cada carpeta incluye sus subcarpetas", "Folder sizes include their subfolders" },
            { "Doble clic para explorar", "Double-click to explore" },
            { "{0:N0} entrada sin acceso o lectura", "{0:N0} unreadable or inaccessible entry" },
            { "{0:N0} entradas sin acceso o lectura", "{0:N0} unreadable or inaccessible entries" },
            { "Entradas sin acceso o lectura ({0:N0} ahora, {1:N0} antes)", "Unreadable or inaccessible entries ({0:N0} now, {1:N0} before)" },
            { "≈ Medición parcial", "≈ Partial scan" },
            { "≈ señala los valores afectados por entradas que no se pudieron medir.", "≈ marks values affected by entries that couldn't be scanned." },
            { "La medición suma tamaños de archivo. Puntos de reanálisis omitidos, incluidos enlaces: {0:N0}.", "The scan adds up file sizes. Reparse points skipped, including links: {0:N0}." },
            { "Cargando mediciones…", "Loading scans…" },
            { "Medición en curso", "Scan in progress" },
            { "Guarda la primera medición", "Save your first scan" },
            { "El avance aparece en la parte inferior de la ventana.", "Progress appears at the bottom of the window." },
            { "Elige una ubicación y pulsa Medir ahora.", "Choose a location and select Scan now." },
            { "Sin resultados", "No results" },
            { "Carpeta sin entradas medidas", "No scanned items in this folder" },
            { "Prueba otro nombre o pulsa Borrar filtros.", "Try a different name or select Clear filters." },
            { "Puedes subir de nivel para explorar otras carpetas.", "Go up a level to explore other folders." },
            { "Esta carpeta ya no está disponible.", "This folder is no longer available." },
            { "Ruta copiada", "Path copied" },
            { "El portapapeles está ocupado. Vuelve a intentar copiar la ruta.", "The clipboard is busy. Try copying the path again." },
            { "Archivos de esta carpeta", "Files in this folder" },
            { "Espacio de la unidad no disponible", "Drive space unavailable" },
            { "de {0} totales", "of {0} total" },
            { "Espacio utilizado en la unidad: {0:P0}", "Used drive space: {0:P0}" },
            { "No se pudo guardar el idioma. El cambio se aplicó a esta sesión.", "Couldn't save the language. The change applies to this session." },
            { "Formato de historial desconocido.", "Unrecognized scan history format." },
            { "La medición está incompleta.", "The scan is incomplete." },
            { "Archivos más grandes", "Largest files" },
            { "Archivo", "File" },
            { "Tamaño", "Size" },
            { "Preparando la búsqueda…", "Preparing to scan…" },
            { "Actualizar", "Refresh" },
            { "Archivos ordenados por tamaño", "Files sorted by size" },
            { "Mostrar en Explorador", "Show in File Explorer" },
            { "Cancelando la búsqueda…", "Canceling the search…" },
            { "Buscando en la carpeta y sus subcarpetas…", "Scanning the folder and its subfolders…" },
            { "Buscando… {0:N0} archivos revisados", "Scanning… {0:N0} files checked" },
            { "Búsqueda cancelada.", "Search canceled." },
            { "Búsqueda cancelada. Se muestran los resultados anteriores.", "Search canceled. Showing the previous results." },
            { "No se pudo revisar la carpeta. {0}", "Couldn't scan the folder. {0}" },
            { "En esta carpeta", "In this folder" },
            { "No se encontraron archivos que se pudieran medir.", "No files could be scanned." },
            { "1 archivo medido", "1 file scanned" },
            { "{0:N0} archivos medidos", "{0:N0} files scanned" },
            { "Los {0:N0} más grandes de {1:N0} archivos medidos", "Largest {0:N0} of {1:N0} scanned files" },
            { "Resultados parciales", "Partial results" },
            { "Puntos de reanálisis omitidos, incluidos enlaces: {0:N0}.", "Reparse points skipped, including links: {0:N0}." },
            { "Selecciona un archivo.", "Select a file." },
            { "El archivo ya no está disponible en esa ruta.", "The file is no longer available at that path." },
            { "¿Qué es esto?", "What is this?" },
            { "Explicar para qué sirve el elemento seleccionado.", "Explain what the selected item is used for." },
            { "Ruta del elemento", "Item path" },
            { "Explicación del elemento", "Item explanation" },
            { "Abrir fuente oficial", "Open official source" },
            { "Cerrar", "Close" },
            { "Identificado por la ruta", "Identified by path" },
            { "Contexto de la carpeta que lo contiene", "Context from the containing folder" },
            { "Formato sugerido por la extensión {0}", "Format suggested by the {0} extension" },
            { "Sin coincidencia conocida", "No known match" },
            { "El nombre, la extensión y la ruta disponibles no permiten identificar para qué se usa este archivo.", "The available name, extension, and path give too little information to identify this file's purpose." },
            { "Esta ruta no coincide con una ubicación del catálogo. Para averiguar su función, revisa qué aplicación o proyecto usa la carpeta.", "This path has no match in the catalog. Check which app or project uses this folder to learn its purpose." },
            { "La extensión orienta sobre el formato. El programa que lo creó sigue sin identificar.", "The extension suggests a format. The app that created it is still unknown." },
            { "No se pudo abrir la fuente oficial.", "Couldn't open the official source." },
            { "Buscar actualizaciones", "Check for updates" },
            { "Buscando actualizaciones…", "Checking for updates…" },
            { "Actualización", "Update" },
            { "Ya tienes la versión más reciente ({0}).", "You have the latest version ({0})." },
            { "Está disponible la versión {0}. ¿Descargarla y abrirla ahora?", "Version {0} is available. Download and open it now?" },
            { "Descargando y verificando la versión {0}…", "Downloading and verifying version {0}…" },
            { "No se pudo completar la actualización. Si Windows bloqueó el archivo, revisa el aviso antes de volver a intentarlo.", "The update could not be completed. If Windows blocked the file, review the alert before trying again." },
            { "Comparar archivos", "Compare files" },
            { "Compara los archivos de la carpeta seleccionada entre dos mediciones.", "Compare files in the selected folder between two scans." },
            { "Guardar medición", "Save scan" },
            { "Buscar archivo", "Find file" },
            { "Estado", "Status" },
            { "Ahora", "Now" },
            { "Todos los cambios", "All changes" },
            { "Nuevo", "Added" },
            { "Ya no aparece", "Removed" },
            { "Aumentó", "Grew" },
            { "Se redujo", "Shrank" },
            { "Sin confirmar", "Unconfirmed" },
            { "Midiendo…", "Scanning…" },
            { "Guarda la primera medición de esta carpeta.", "Save the first scan of this folder." },
            { "Guarda otra medición para comparar los tamaños de archivo.", "Save another scan to compare file sizes." },
            { "Última medición {0} · {1:N0} archivos", "Latest scan {0} · {1:N0} files" },
            { "Medición parcial · {0:N0} entradas omitidas ahora · {1:N0} antes", "Partial scan · {0:N0} entries skipped now · {1:N0} before" },
            { "Si una medición es parcial, un archivo ausente en esa medición queda sin confirmar.", "A file missing from a partial scan is shown as unconfirmed." },
            { "Medición cancelada.", "Scan canceled." },
            { "Esta carpeta supera el límite de una medición por archivo. Elige una subcarpeta.", "This folder exceeds the file scan limit. Choose a smaller subfolder." },
            { "No se pudo leer o guardar la medición de archivos.", "Couldn't read or save the file scan." },
            { "No hay cambios de tamaño para mostrar.", "No size changes to show." },
            { "{0:N0} de {1:N0} cambios · Usa Buscar archivo para acotar", "{0:N0} of {1:N0} changes · Use Find file to narrow the list" },
            { "{0:N0} cambio", "{0:N0} change" },
            { "{0:N0} cambios", "{0:N0} changes" }
        };

        public CultureInfo Culture { get; private set; }
        public string Code { get; private set; }

        public void Select(string code)
        {
            Code = code == "es" ? "es" : "en";
            Culture = CultureInfo.GetCultureInfo(Code == "es" ? "es-CL" : "en-US");
        }

        public string Text(string key, params object[] args)
        {
            string translated;
            string text = Code == "en" && English.TryGetValue(key, out translated) ? translated : key;
            return args.Length == 0 ? text : string.Format(Culture, text, args);
        }

        public string Date(DateTime date, bool seconds = false)
        {
            string time = Code == "es" ? (seconds ? "HH:mm:ss" : "HH:mm") : (seconds ? "h:mm:ss tt" : "h:mm tt");
            string format = Code == "es" ? "dd'/'MM'/'yyyy" : "MMM d, yyyy";
            return date.ToLocalTime().ToString(format + (seconds ? " · " : " ") + time, Culture);
        }
    }
}
