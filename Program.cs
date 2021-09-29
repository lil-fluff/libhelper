using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.IO.Compression;

namespace libhelper {
    class Program {

        private static Program instance;
        private int confidence = 0;
        private int tries = 3;
        private string targetLocation = "", tempDirectory = "";
        private string remoteUrl = "https://cdn.lutonmediagroup.com/", remoteFile = "libraries.zip";
        private string SHA_1 = "4CCC254CA84988184064DDE4EC150F2601D4C98C";

        public Program() {
            instance = this;
        }

        public bool Run() {
            print("Starting up...");
            delayedPrint("Checking environment...", 500);
            targetLocation = @".\";
            testLocationConfidence();
            if (confidence <= 8) {
                var result = MessageBox.Show("It seems this program was not launched alongside\n" +
                                "the target directory for Minecraft.\n\n" +
                                "Would you like to specify the target directory now?", "Error!", 
                                MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
                if (result != DialogResult.Yes) 
                    return ExitWithErr("Please move this program to the same directory as your Minecraft folder and try again.");
                while (confidence <= 8) {
                    if (tries == 0) return ExitWithErr("Too many attempts to find the target directory. Please exit and try again.");
                    delayedPrint($"Please select the target directory. ({tries} {(tries > 1 ? "tries" : "try")} left.)");
                    using (FolderBrowserDialog folder = new FolderBrowserDialog()) {
                        folder.Description = "Location of New Beginnings 3 install folder:";
                        folder.ShowNewFolderButton = false;
                        result = folder.ShowDialog();
                        if (result == DialogResult.OK) {
                            targetLocation = folder.SelectedPath;
                            testLocationConfidence();
                        }
                        else print("Selection canceled, please try again.");
                    }
                    --tries;
                }
            }
            
            delayedPrint("Checking internet connection...", 300);
            if (CheckForInternetConnection()) {
                delayedPrint("Checking remote website availability...", 500);
                if (CheckForInternetConnection(1000, remoteUrl + "news.html")) {
                    delayedPrint("Downloading content...", 300);
                    SetTemporaryDirectory();
                    downloadRemoteFile(remoteUrl, remoteFile);
                    delayedPrint("Download complete. Validating...", 300);
                    var tempZip = Path.Combine(tempDirectory, remoteFile);
                    var sha1calc = validateDownloadedFile(tempZip);
                    if (!sha1calc.Equals(SHA_1)) return ExitWithErr($"Downloaded .zip file failed CRC Check.\nExpected: '{SHA_1}'\nGot: '{sha1calc}'\nThis program will now terminate.");

                    if (Directory.Exists(Path.Combine(targetLocation, "libraries"))){
                        var result = MessageBox.Show("The 'libraries' folder already exists \n" +
                                        "in the target directory. We need to\n" +
                                        "delete this folder to replace it.\n\n" +
                                        "Do you want to continue?", "Warning!", 
                                        MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
                        if (result != DialogResult.Yes) {
                            try {
                                print("Cleaning up temporary downloaded zip file...");
                                File.Delete(tempZip);
                            }
                            catch {
                                print("ERR: Unable to remove temporary downloaded file.");
                            }
                            return ExitWithErr("Replacement operation canceled! Terminating...");
                        }
                        for (int tries = 10; tries > -1; --tries) {
                            try {
                                print("Removing existing library files...");
                                Directory.Delete(Path.Combine(targetLocation, "libraries"), true);
                                break;
                            }
                            catch {
                                if (tries > 1) {
                                    return ExitWithErr("Failed to delete existing 'libraries' directory.\n" +
                                        "Try deleting the folder manually and start this program again.");
                                }
                            }
                        }
                        try {
                            delayedPrint("Extrating new files to 'libraries'...", 300);
                            ZipFile.ExtractToDirectory(tempZip, targetLocation);
                            delayedPrint("Extraction complete! You should now try launching Minecraft again.", 500);
                            return ExitFinal();
                        }
                        catch {
                            return ExitWithErr("Zip file extraction filed! Terminating...");
                        }
                    }
                }
                else return ExitWithErr("Failed to connect to CDN endpoint! Terminating...");
            }
            else return ExitWithErr("Failed to detect internet connection! Terminating...");
            return ExitFinal();
        }

        private void testLocationConfidence() {
            confidence = 0;
            print("Testing selected directory...");
            if (Directory.Exists(Path.Combine(targetLocation, "libraries"))) {
                confidence += 5;
                delayedPrint("Found 'libraries' folder!", 500);
            }
            if (Directory.Exists(Path.Combine(targetLocation, "instances"))) {
                confidence += 5;
                delayedPrint("Found 'instances' folder!", 500);
            }
            if (Directory.Exists(Path.Combine(targetLocation, "assets"))) {
                confidence += 3;
                delayedPrint("Found 'assets' folder!", 500);
            }
            if (Directory.Exists(Path.Combine(targetLocation, "versions"))) {
                confidence += 3;
                delayedPrint("Found 'versions' folder!", 500);
            }
        }

        private bool ExitWithErr(string message) {
            print(message);
            ExitFinal();
            return false;
        }

        private bool ExitFinal() {
            print("Press any key to exit...");
            Console.ReadKey();
            return true;
        }

        private string currentTimeStamp() => DateTime.Now.ToString("[HH:mm:ss] ");
        private void print(string message, bool skipTimeStamp = false) => Console.WriteLine((skipTimeStamp ? "" : currentTimeStamp()) + message);

        private void delayedPrint(string message, int ms = 0) {
            Thread.Sleep(ms);
            print(message);
        }

        private bool downloadRemoteFile(string baseUrl, string fileName) {
            try {
                using (var client = new WebClient()) {
                    var uri = new Uri(baseUrl + fileName);
                    var fileTarget = Path.Combine(tempDirectory, fileName);
                    print("Starting download to " + fileTarget);
                    int percentage = 0;
                    client.DownloadProgressChanged += (s, e) => {
                        if (e.ProgressPercentage > percentage) {
                            print($"{fileName}:  downloaded {e.BytesReceived} of {e.TotalBytesToReceive} bytes. {e.ProgressPercentage} % complete...");
                            percentage = e.ProgressPercentage;
                        }
                    };
                    var downloadTask = client.DownloadFileTaskAsync(uri, fileTarget);
                    while (!downloadTask.IsCompleted) {
                        Thread.Sleep(1);
                        if (downloadTask.IsFaulted || downloadTask.IsCanceled) return false;
                    }
                    return true;
                }
            }
            catch {
                return false;
            }
        }

        private string validateDownloadedFile(string file) {
            using (FileStream fs = new FileStream(file, FileMode.Open)) {
                using (BufferedStream bs = new BufferedStream(fs)) {
                    using (SHA1Managed sha1 = new SHA1Managed()) {
                        byte[] hash = sha1.ComputeHash(bs);
                        StringBuilder formatted = new StringBuilder(2 * hash.Length);
                        foreach (byte b in hash) {
                            formatted.AppendFormat("{0:X2}", b);
                        }
                        return formatted.ToString();
                    }
                }
            }
        }

        private static bool CheckForInternetConnection(int timeoutMs = 1000, string url = null) {
            try {
                if (url == null) {
                    var n = CultureInfo.InstalledUICulture.Name;
                    if (n.StartsWith("fa")) url = "http://www.aparat.com";
                    else if (n.StartsWith("zh")) url = "http://www.baidu.com";
                    else url = "http://www.gstatic.com/generate_204";
                }

                var request = (HttpWebRequest)WebRequest.Create(url);
                request.KeepAlive = false;
                request.Timeout = timeoutMs;
                using (var response = (HttpWebResponse)request.GetResponse())
                    return true;
            }
            catch {
                return false;
            }
        }

        private void SetTemporaryDirectory() {
            tempDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(tempDirectory);
        }

        [STAThread]
        public static void Main(string[] args) => new Program().Run();
    }
}
