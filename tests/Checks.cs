using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DiskGrowth
{
    internal static class Checks
    {
        [STAThread]
        private static int Main(string[] args)
        {
            AppContext.SetSwitch("Switch.System.IO.UseLegacyPathHandling", false);
            AppContext.SetSwitch("Switch.System.IO.BlockLongPaths", false);
            try
            {
                string folder = Path.GetFullPath(args[0]);
                string scope = Path.Combine(folder, "Datos de prueba");
                Directory.CreateDirectory(scope);
                var history = new History(Path.Combine(folder, "historial"));
                RunDataChecks(scope, history);
                CheckLargestFiles(folder, scope);
                CheckPathExplanations();
                CheckUpdater(folder);
                if (args.Length > 1)
                    CheckVersionOne(args[1]);
                RunWindowChecks(scope, history, folder);
                Console.WriteLine("OK: history, navigation, bilingual UI, explanations and update verification.");
                return 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error);
                return 1;
            }
        }

        private static void CheckUpdater(string fixture)
        {
            string app = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "Disk Growth Tracker.exe");
            byte[] bytes = File.ReadAllBytes(app);
            AssemblyName releaseName = AssemblyName.GetAssemblyName(app);
            var old = new AssemblyName { Name = releaseName.Name, Version = new Version(0, 0, 0, 0) };
            string hash;
            using (var sha = SHA256.Create())
                hash = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "");
            string payloadInfo = "\nsha256=" + hash + "\nsize=" + bytes.Length;
            string manifest = "version=" + releaseName.Version.ToString(3) + payloadInfo;
            using (var updater = new PortableUpdate(new TestUpdateFeed(manifest, bytes)))
            {
                var release = updater.Check(CancellationToken.None).GetAwaiter().GetResult();
                var future = UpdateRelease.Parse("version=2.0.10" + payloadInfo);
                Require(release.Version == releaseName.Version && future.Version > new Version(2, 0, 9, 0),
                    "versions compare numerically");
                Require(future.Download.AbsoluteUri ==
                    "https://raw.githubusercontent.com/ZielCL/Disk-Growth-Tracker/main/downloads/Disk%20Growth%20Tracker%202.0.10.exe",
                    "update derives a URL in the trusted repository");
                string copy = updater.Download(release, fixture, old, CancellationToken.None).GetAwaiter().GetResult();
                try
                {
                    Require(File.Exists(copy) && !string.Equals(copy, app, StringComparison.OrdinalIgnoreCase),
                        "update downloaded to a separate version folder");
                    PortableUpdate.CheckWithWindows(release, copy, old.Name);
                }
                finally { PortableUpdate.Discard(copy); }

                try
                {
                    updater.Download(release, fixture, releaseName, CancellationToken.None).GetAwaiter().GetResult();
                    throw new Exception("The same version was accepted for installation.");
                }
                catch (InvalidDataException) { }
            }

            bytes[bytes.Length - 1] ^= 1;
            using (var updater = new PortableUpdate(new TestUpdateFeed(manifest, bytes)))
            {
                try
                {
                    var release = updater.Check(CancellationToken.None).GetAwaiter().GetResult();
                    updater.Download(release, fixture, old, CancellationToken.None).GetAwaiter().GetResult();
                    throw new Exception("A payload with the wrong hash was accepted.");
                }
                catch (InvalidDataException) { }
            }
            using (var updater = new PortableUpdate(new TestUpdateFeed(new string('x', 257), bytes)))
            {
                try
                {
                    updater.Check(CancellationToken.None).GetAwaiter().GetResult();
                    throw new Exception("An oversized release record was accepted.");
                }
                catch (InvalidDataException) { }
            }
            try
            {
                UpdateRelease.Parse(manifest + "\nurl=https://example.com/file.exe");
                throw new Exception("An update manifest with a URL override was accepted.");
            }
            catch (InvalidDataException) { }
        }

        private sealed class TestUpdateFeed : HttpMessageHandler
        {
            private readonly string manifest;
            private readonly byte[] bytes;

            public TestUpdateFeed(string manifest, byte[] bytes)
            {
                this.manifest = manifest;
                this.bytes = bytes;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            {
                Require(request.RequestUri.Scheme == "https" && request.RequestUri.Host == "raw.githubusercontent.com" &&
                    request.Method == HttpMethod.Get, "the updater requests data only from the trusted host");
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = request.RequestUri == UpdateRelease.Manifest
                        ? (HttpContent)new StringContent(manifest) : new ByteArrayContent(bytes)
                });
            }
        }

        private static void CheckPathExplanations()
        {
            const string clipchamp = @"C:\Users\Demo\AppData\Local\Packages\Clipchamp.Clipchamp_yxz26nhyzhsrt\LocalState\EBWebView\Default\IndexedDB";
            var info = PathExplanation.Identify(clipchamp + @"\blob\sample.MP4", true);
            Require(info.Location.EnglishTitle == "Clipchamp local storage" && info.Location.Source != null &&
                info.Format.EnglishTitle == "Video file", "Clipchamp location and uppercase file extension");
            Require(PathExplanation.Identify(clipchamp, false).Location.SpanishTitle == "Almacenamiento de Clipchamp",
                "Clipchamp root and Spanish catalog");
            Require(PathExplanation.Identify(@"D:\SteamLibrary\steamapps\common\Game", false).Location.EnglishTitle == "Steam game library",
                "Steam game on another drive");
            Require(PathExplanation.Identify(@"C:\Windows\Installer\package.msi", true).Location.EnglishTitle == "Windows Installer cache",
                "Windows Installer context");
            Require(PathExplanation.Identify(@"C:\pagefile.sys", true).Location.EnglishTitle == "Windows page file", "Windows root file");
            Require(PathExplanation.Identify(@"C:\Examples\Windows\Installer", false).Location == null &&
                PathExplanation.Identify(@"C:\Examples\pagefile.sys", true).Location == null, "no system match for incidental names");
            Require(PathExplanation.Identify(@"C:\Demo\archive.zip", false).Format == null, "folder suffix is not a file type");
            info = PathExplanation.Identify(@"D:\Unidentified\payload.unknown", true);
            Require(info.Location == null && info.Format == null, "unknown file type is left unidentified");
        }

        private static void CheckLargestFiles(string folder, string scope)
        {
            var largest = new Scanner(CancellationToken.None, null).ScanLargest(scope, 2);
            Require(largest.Files == 4 && largest.Entries.Length == 2, "bounded largest files result");
            Require(largest.Entries[0].Path == Path.Combine(scope, "Vídeos", "Edición", "proyecto.bin") &&
                largest.Entries[0].Bytes == 5120 && largest.Entries[1].Bytes == 2048, "largest files measured recursively");
            string ties = Path.Combine(folder, "empates");
            Directory.CreateDirectory(ties);
            foreach (string name in new[] { "c.bin", "a.bin", "b.bin", "d.bin" })
                Write(ties, name, 10);
            var ranked = new Scanner(CancellationToken.None, null).ScanLargest(ties, 2);
            Require(ranked.Entries.Select(item => Path.GetFileName(item.Path)).SequenceEqual(new[] { "a.bin", "b.bin" }),
                "equal sizes have stable ordering");
            using (var token = new CancellationTokenSource())
            {
                token.Cancel();
                try
                {
                    new Scanner(token.Token, null).ScanLargest(scope, 2);
                    throw new Exception("Canceled search completed.");
                }
                catch (OperationCanceledException) { }
            }
        }

        private static void RunDataChecks(string scope, History history)
        {
            Directory.CreateDirectory(Path.Combine(scope, "Vídeos", "Edición"));
            Directory.CreateDirectory(Path.Combine(scope, "Descargas"));
            Directory.CreateDirectory(Path.Combine(scope, "Fotos", "Viajes"));
            Write(scope, "Vídeos\\Edición\\proyecto.bin", 1024);
            Write(scope, "Descargas\\antiguo.bin", 512);
            Write(scope, "Fotos\\Viajes\\a.jpg", 256);
            Write(scope, "notas.txt", 128);
            var first = new Scanner(CancellationToken.None, null).Scan(scope);
            first.Errors = 3;
            first.Get("Fotos").Partial = true;
            first.Get("").Partial = true;
            history.Save(scope, first);
            Require(first.Get("").Total == 1920, "first total");
            Require(first.Get("Vídeos").Total == 1024, "nested size");

            Write(scope, "Vídeos\\Edición\\proyecto.bin", 5120);
            File.Delete(Path.Combine(scope, "Descargas", "antiguo.bin"));
            Directory.Delete(Path.Combine(scope, "Descargas"));
            Directory.CreateDirectory(Path.Combine(scope, "Música"));
            Write(scope, "Música\\audio.bin", 2048);
            Write(scope, "notas.txt", 100);
            var second = new Scanner(CancellationToken.None, null).Scan(scope);
            var rows = ComparisonRow.InFolder(second, first, "").ToList();
            Require(rows.Single(r => r.Path == "Vídeos").Change == 4096, "growing folder");
            Require(rows.Single(r => r.Path == "Música").Change == 2048, "new folder");
            Require(rows.Single(r => r.Path == "Descargas").Change == -512, "deleted folder");
            Require(rows.Single(r => r.DirectFiles).Change == -28, "direct files");
            Require(rows.Single(r => r.Path == "Fotos").Change == 0, "unchanged folder");
            Require(ComparisonRow.InFolder(second, first, "Vídeos").Single().Path == "Vídeos\\Edición", "child row");
            Require(ComparisonRow.InFolder(second, first, "", true).Single(r => r.Path == "Vídeos\\Edición").Change == 4096,
                "nested change in ranking");
            Require(ComparisonRow.InFolder(second, first, "Vídeos", true).Single().Path == "Vídeos\\Edición",
                "recursive scope limited to selected branch");
            Require(second.Get("").Total == 7524, "second total");
            Require(history.Read(history.List(scope).Single()).Get("").Total == first.Get("").Total, "round trip");
            Require(History.Normalize(" \"" + scope + "\\\" ") == scope, "normalization");
            Require(history.RecentLocations().Single() == scope, "recent location written");

            using (var token = new CancellationTokenSource())
            {
                token.Cancel();
                try
                {
                    new Scanner(token.Token, null).Scan(scope);
                    throw new Exception("Canceled scan completed.");
                }
                catch (OperationCanceledException) { }
            }
            var testHistory = new History(Path.Combine(Path.GetDirectoryName(scope), "retention"));
            for (int i = 0; i < History.Limit + 2; i++)
            {
                second.Date = DateTime.UtcNow.AddMinutes(i);
                testHistory.Save(scope, second);
            }
            Require(testHistory.List(scope).Count == History.Limit, "listing cap");
            Require(Directory.GetFiles(Path.Combine(Path.GetDirectoryName(scope), "retention"), "*.dgs",
                SearchOption.AllDirectories).Length == History.Limit, "storage cap");

            var uncertain = new Snapshot();
            uncertain.Add(new FolderSize { Path = "", Partial = true });
            Require(ComparisonRow.InFolder(uncertain, first, "").Single(row => row.Path == "Vídeos").Partial,
                "missing folder marked approximate when parent is incomplete");
            Require(ComparisonRow.InFolder(uncertain, first, "", true).Single(row => row.Path == "Vídeos\\Edición").Partial,
                "deep omission marked approximate");
        }

        private static void CheckVersionOne(string folder)
        {
            string root = Path.Combine(folder, "Datos de prueba");
            var history = new History(Path.Combine(folder, "historial"));
            var entries = history.List(root);
            Require(entries.Count == 2, "v1 measurement files discovered");
            var newest = history.Read(entries[0]);
            var first = history.Read(entries[1]);
            Require(ComparisonRow.InFolder(newest, first, "", true).Single(row => row.Path == "Vídeos\\Edición").Change == 4096,
                "rank a change using v1 data");
            Require(history.RecentLocations().Count == 0, "v1 history can lack recent metadata");
        }

        private static void RunWindowChecks(string scope, History history, string folder)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            var preferences = new LanguagePreferences(Path.Combine(folder, "settings"));
            Require(preferences.Save("es"), "set initial test language");
            Exception failure = null;
            using (var form = new DiskWindow(history, preferences))
            {
                form.Shown += async (sender, args) =>
                {
                    try
                    {
                        var scan = Get<Button>(form, "scan");
                        var location = Get<ComboBox>(form, "location");
                        var listing = Get<ListView>(form, "listing");
                        var picker = Get<ComboBox>(form, "baselinePicker");
                        await WaitUntil(() => scan.Enabled);
                        Require(location.Text == scope && location.Items.Contains(scope), "recent location reopened and selectable");
                        location.Text = scope;
                        scan.PerformClick();
                        await WaitUntil(() => scan.Enabled && scan.Text == "Medir ahora" && listing.Items.Count == 5);
                        Require(picker.Enabled && picker.SelectedIndex == 1, "baseline loaded");
                        Require(listing.Items[0].Text == "Vídeos", "largest increase first");
                        Require(listing.Items[0].SubItems[1].Text == "+4 KiB", "formatted change");
                        Require(listing.Items[4].Text == "Descargas", "deletion listed last");

                        var search = Get<TextBox>(form, "search");
                        var includeDescendants = Get<CheckBox>(form, "includeDescendants");
                        var growthOnly = Get<CheckBox>(form, "growthOnly");
                        search.Text = "edicion";
                        await WaitUntil(() => listing.Items.Count == 1);
                        Require(listing.Items[0].Text == "Vídeos\\Edición", "search without accent finds nested folder");
                        search.Text = "no existe";
                        await WaitUntil(() => listing.Items.Count == 0);
                        Require(Get<Label>(form, "emptyTitle").Text == "Sin resultados" && !listing.Visible, "empty search feedback");
                        search.Clear();
                        includeDescendants.Checked = true;
                        growthOnly.Checked = true;
                        Require(listing.Items.Count == 3 && listing.Items.Cast<ListViewItem>()
                            .Any(item => item.Text == "Vídeos\\Edición"), "rank only folders with positive growth");
                        SaveView(form, Path.Combine(folder, "preview.png"));
                        includeDescendants.Checked = false;
                        Require(listing.Items.Count == 2, "positive growth in the current level");
                        growthOnly.Checked = false;

                        listing.Items[0].Selected = true;
                        Require(Get<Label>(form, "detailPath").Text == Path.Combine(scope, "Vídeos"), "absolute path for selection");
                        Require(Get<Button>(form, "copy").Enabled && Get<Button>(form, "reveal").Enabled, "selected path actions");
                        Require(Get<Button>(form, "measureFolder").Enabled, "selected folder can be measured independently");
                        CheckExplanationDialog(form, Get<Button>(form, "explainFolder"), false);
                        SaveKnownExplanation(form, folder);
                        CheckFileWindow(form, scope, folder, false);
                        var languagePicker = Get<ComboBox>(form, "languagePicker");
                        var changeLanguage = typeof(DiskWindow).GetMethod("ChangeLanguage", BindingFlags.Instance | BindingFlags.NonPublic);
                        string spanishDate = picker.GetItemText(picker.SelectedItem);
                        languagePicker.SelectedIndex = 1;
                        changeLanguage.Invoke(form, null);
                        Require(form.Text == "What's filling up my drive?" && scan.Text == "Scan now" &&
                            listing.Columns[0].Text == "Folder" && Get<Button>(form, "copy").Text == "Copy path" &&
                            Get<Button>(form, "explainFolder").Text == "What is this?" &&
                            Get<Button>(form, "updateButton").Text == "Check for updates",
                            "English reached main controls and columns");
                        Require(picker.SelectedIndex == 1 && picker.GetItemText(picker.Items[0]) == "No comparison" &&
                            picker.GetItemText(picker.SelectedItem) != spanishDate, "comparison selection and localized date");
                        Require(Get<Label>(form, "quality").Text.Contains("Partial scan") &&
                            Get<Label>(form, "footer").Text.StartsWith("Local history"), "status and partial warning translated");
                        Require(Get<Label>(form, "detailPath").Text == Path.Combine(scope, "Vídeos") &&
                            listing.SelectedItems[0].Text == "Vídeos" && history.List(scope).Count == 2, "selection and history preserved");
                        Require(preferences.Load() == "en", "English preference saved");
                        SaveView(form, Path.Combine(folder, "main-english.png"));
                        CheckFileWindow(form, scope, folder, true);
                        languagePicker.SelectedIndex = 0;
                        changeLanguage.Invoke(form, null);
                        Require(scan.Text == "Medir ahora" && picker.GetItemText(picker.Items[0]) == "Sin comparación" &&
                            picker.GetItemText(picker.SelectedItem) == spanishDate && preferences.Load() == "es",
                            "language and format restored");
                        var photos = listing.Items.Cast<ListViewItem>().Single(item => item.Text == "Fotos");
                        Require(!photos.SubItems[2].Text.StartsWith("≈") && photos.SubItems[3].Text.StartsWith("≈"),
                            "only baseline size is partial");
                        Require(Get<Label>(form, "quality").Visible && Get<Label>(form, "quality").Text.Contains("3 antes"),
                            "partial baseline warning");
                        typeof(DiskWindow).GetMethod("OpenSelected", BindingFlags.Instance | BindingFlags.NonPublic)
                            .Invoke(form, null);
                        Require(listing.Items.Count == 1 && listing.Items[0].Text == "Edición", "drill down");
                        Require(Get<Label>(form, "breadcrumb").Text.EndsWith("›  Vídeos"), "short breadcrumb");
                        scan.PerformClick();
                        await WaitUntil(() => scan.Enabled && scan.Text == "Medir ahora" && history.List(scope).Count == 3);
                        Require(listing.Items.Count == 1 && listing.Items[0].Text == "Edición", "remeasure without leaving active folder");
                        Get<Button>(form, "home").PerformClick();
                        picker.SelectedIndex = 2;
                        await (Task)typeof(DiskWindow).GetMethod("ChangeBaseline", BindingFlags.Instance | BindingFlags.NonPublic)
                            .Invoke(form, null);
                        Require(listing.Items[0].Text == "Vídeos", "navigate up");
                        listing.Items.Cast<ListViewItem>().Single(item => item.Text == "Descargas").Selected = true;
                        Require(Get<Label>(form, "detailTitle").Text.Contains("Solo en la medición anterior"),
                            "removed folder explained");
                        Require(Get<Button>(form, "copy").Enabled && !Get<Button>(form, "reveal").Enabled,
                            "removed folder is copyable without explorer action");
                        Require(!Get<Button>(form, "largestFiles").Enabled, "no live lookup for removed folder");
                        form.Size = form.MinimumSize;
                        form.PerformLayout();
                        await Task.Delay(120);
                        SaveView(form, Path.Combine(folder, "preview-small.png"));
                        listing.Items[0].Selected = true;
                        var videoFolder = Path.Combine(scope, "Vídeos");
                        Get<Button>(form, "measureFolder").PerformClick();
                        await WaitUntil(() => scan.Enabled && scan.Text == "Medir ahora" &&
                            location.Text == videoFolder && listing.Items.Count == 1);
                        Require(history.List(videoFolder).Count == 1 && history.List(scope).Count == 3,
                            "selected folder gets separate history");
                        Require(!Get<Button>(form, "measureFolder").Enabled, "no duplicate measure action for root");
                    }
                    catch (Exception error)
                    {
                        failure = error;
                    }
                    finally
                    {
                        form.Close();
                    }
                };
                Application.Run(form);
            }
            if (failure != null)
                throw failure;
            Require(preferences.Save("en"), "save English for relaunch");
            using (var restarted = new DiskWindow(history, preferences))
                Require(restarted.Text == "What's filling up my drive?", "new window starts in saved language");
        }

        private static void CheckFileWindow(DiskWindow parent, string scope, string output, bool english)
        {
            Exception failure = null;
            bool checkedDialog = false;
            int ticks = 0;
            using (var timer = new System.Windows.Forms.Timer { Interval = 50 })
            {
                timer.Tick += (sender, args) =>
                {
                    var dialog = Application.OpenForms.Cast<Form>().FirstOrDefault(form => form != parent);
                    if (dialog == null)
                        return;
                    try
                    {
                        var grid = Get<ListView>(dialog, "files");
                        if (grid.Items.Count == 0)
                        {
                            Require(++ticks < 100, "file search completed");
                            return;
                        }
                        Require(grid.Items.Count == 1 && grid.Items[0].Text == "proyecto.bin", "selected folder is analyzed");
                        Require(grid.Items[0].SubItems[1].Text == "5 KiB" && grid.Items[0].SubItems[2].Text == "Edición",
                            "file row shows size and relative location");
                        Require(dialog.Text == (english ? "Largest files" : "Archivos más grandes") &&
                            grid.Columns[0].Text == (english ? "File" : "Archivo") &&
                            Get<Label>(dialog, "summary").Text == (english ? "1 file scanned" : "1 archivo medido"),
                            "file window follows selected language");
                        grid.Items[0].Selected = true;
                        string expectedPath = Path.Combine(scope, "Vídeos", "Edición", "proyecto.bin");
                        Require(Get<Label>(dialog, "selectedPath").Text == expectedPath, "file action uses full path");
                        Require(Get<Button>(dialog, "copyFile").Enabled && Get<Button>(dialog, "revealFile").Enabled,
                            "file actions ready for selection");
                        timer.Stop();
                        CheckExplanationDialog(dialog, Get<Button>(dialog, "explainFile"), english);
                        SaveView(dialog, Path.Combine(output, english ? "largest-files-english.png" : "largest-files.png"));
                        checkedDialog = true;
                    }
                    catch (Exception error)
                    {
                        failure = error;
                    }
                    if (failure != null || checkedDialog)
                    {
                        timer.Stop();
                        dialog.Close();
                    }
                };
                timer.Start();
                Get<Button>(parent, "largestFiles").PerformClick();
            }
            if (failure != null)
                throw failure;
            Require(checkedDialog, "file dialog opened");
        }

        private static void CheckExplanationDialog(Form owner, Button action, bool english)
        {
            Exception failure = null;
            bool checkedDialog = false;
            Require(action.Enabled, "explanation action is available");
            using (var timer = new System.Windows.Forms.Timer { Interval = 50 })
            {
                timer.Tick += (sender, args) =>
                {
                    var dialog = Application.OpenForms.Cast<Form>().FirstOrDefault(form => form.GetType().Name == "ExplanationWindow");
                    if (dialog == null)
                        return;
                    timer.Stop();
                    try
                    {
                        Require(dialog.Owner == owner && dialog.Text == (english ? "What is this?" : "¿Qué es esto?"),
                            "explanation dialog uses the parent and selected language");
                        string description = ReadExplanation(dialog);
                        Require(description.Contains(english ? "No known match" : "Sin coincidencia conocida"),
                            "unidentified sample reports uncertainty honestly");
                        checkedDialog = true;
                    }
                    catch (Exception error) { failure = error; }
                    finally { dialog.Close(); }
                };
                timer.Start();
                action.PerformClick();
            }
            if (failure != null)
                throw failure;
            Require(checkedDialog, "explanation opened from the UI");
        }

        private static void SaveKnownExplanation(Form owner, string output)
        {
            const string example = @"C:\Users\Demo\AppData\Local\Packages\Clipchamp.Clipchamp_yxz26nhyzhsrt\LocalState\EBWebView\Default\IndexedDB\clip.mp4";
            var language = new UiLanguage();
            language.Select("es");
            var type = typeof(DiskWindow).GetNestedType("ExplanationWindow", BindingFlags.NonPublic);
            using (var window = (Form)Activator.CreateInstance(type, example, true, language))
            {
                window.Show(owner);
                string text = ReadExplanation(window);
                Require(text.Contains("Contexto de la carpeta que lo contiene") && text.Contains("Almacenamiento de Clipchamp") &&
                    text.Contains("Formato sugerido por la extensión .mp4"), "known context and file format have separate explanations");
                SaveView(window, Path.Combine(output, "explanation.png"));
                window.Close();
            }
        }

        private static string ReadExplanation(Form window)
        {
            return string.Join("\n", Get<Panel>(window, "description").Controls.Cast<Control>().Select(label => label.Text));
        }

        private static T Get<T>(Form form, string field)
        {
            return (T)form.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
        }

        private static async Task WaitUntil(Func<bool> check)
        {
            for (int i = 0; i < 200; i++)
            {
                if (check())
                    return;
                await Task.Delay(25);
            }
            throw new TimeoutException("The app did not reach the expected state.");
        }

        private static void SaveView(Form form, string destination)
        {
            using (var capture = new Bitmap(form.Width, form.Height))
            {
                form.DrawToBitmap(capture, new Rectangle(Point.Empty, capture.Size));
                capture.Save(destination, ImageFormat.Png);
            }
        }

        private static void Write(string scope, string relative, int length)
        {
            File.WriteAllBytes(Path.Combine(scope, relative), new byte[length]);
        }

        private static void Require(bool condition, string description)
        {
            if (!condition)
                throw new InvalidOperationException("Check failed: " + description);
        }
    }
}
