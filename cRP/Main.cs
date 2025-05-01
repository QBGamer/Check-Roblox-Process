using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Newtonsoft.Json;
using System.IO;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading;
using System.Net;
using System.Runtime.InteropServices;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.StartPanel;
using System.Security.Policy;

namespace cRP
{
    public partial class Main : Form
    {
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool SetWindowText(IntPtr hWnd, string lpString);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        private Dictionary<int, RobloxProcessInfo> trackedProcesses = new Dictionary<int, RobloxProcessInfo>();
        private bool isMonitoring = true;
        private string handlePath;
        private System.Windows.Forms.Timer backupTimer;
        private int maxBackups;
        public Main()
        {
            InitializeComponent();
            handlePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "handle.exe");
            if (!File.Exists(handlePath))
            {
                MessageBox.Show("handle.exe not found in application directory!");
                Application.Exit();
                return;
            }
            if (!Directory.Exists("backup"))
            {
                Directory.CreateDirectory("backup");
            }

            if (!File.Exists("config.json"))
            {
                // Create a default config.json file
                var config = new
                {
                    RAMPath = "",
                    NumOfBackup = 5
                };

                File.WriteAllText("config.json", JsonConvert.SerializeObject(config, Formatting.Indented));
            }

            try
            {
                string configJson = File.ReadAllText("config.json");
                dynamic config = JsonConvert.DeserializeObject(configJson);
                maxBackups = config.NumOfBackup ?? 5; // Mặc định là 5 nếu không có
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi khi đọc config.json: {ex.Message}. Sử dụng NumOfBackup mặc định là 5.", "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
                maxBackups = 5;
            }

            if (!File.Exists("data.json"))
            {
                // Create a default data.json file
                var data = new
                {
                    Accounts = new List<object> {},
                    Places = new List<object> {},
                };

                File.WriteAllText("data.json", JsonConvert.SerializeObject(data, Formatting.Indented));
            }

            cb_game.Items.Add("All");
            cb_game.SelectedIndex = 0;

            backupTimer = new System.Windows.Forms.Timer();
            backupTimer.Interval = 60 * 10 * 1000;
            backupTimer.Tick += BackupTimer_Tick;
            backupTimer.Start();
            StartMonitoring();
        }

        private void Main_Load(object sender, EventArgs e)
        {
            
        }

        private void BackupTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                string dataJsonPath = "data.json";
                string backupDir = "backup";
                if (!File.Exists(dataJsonPath))
                {
                    Console.WriteLine("data.json không tồn tại, bỏ qua backup.");
                    return;
                }

                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string backupFileName = $"data_{timestamp}.json";
                string backupFilePath = Path.Combine(backupDir, backupFileName);

                File.Copy(dataJsonPath, backupFilePath, true);

                string[] backupFiles = Directory.GetFiles(backupDir, "data_*.json")
                    .OrderBy(f => File.GetCreationTime(f))
                    .ToArray();

                while (backupFiles.Length > maxBackups)
                {
                    // Delete oldest backup
                    File.Delete(backupFiles[0]);
                    backupFiles = Directory.GetFiles(backupDir, "data_*.json")
                        .OrderBy(f => File.GetCreationTime(f))
                        .ToArray();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi khi backup data.json: {ex.Message}", "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void StartMonitoring()
        {
            Task.Run(() =>
            {
                while (isMonitoring)
                {
                    try
                    {
                        RobloxProcesses();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex);
                    }
                    Thread.Sleep(1000);
                }
            });
        }

        private void RobloxProcesses()
        {
            try
            {
                Process[] processes = Process.GetProcessesByName("RobloxPlayerBeta");

                List<int> pidsToRemove = new List<int>();
                foreach (var pid in trackedProcesses.Keys)
                {
                    if (!processes.Any(p => p.Id == pid))
                        pidsToRemove.Add(pid);
                }
                foreach (var pid in pidsToRemove)
                {
                    trackedProcesses.Remove(pid);
                    this.Invoke((MethodInvoker)delegate
                    {
                        foreach (DataGridViewRow row in dataGridView.Rows)
                        {
                            if (row.Cells[0].Value != null && row.Cells[0].Value.ToString() == pid.ToString())
                            {
                                dataGridView.Rows.Remove(row);
                                break;
                            }
                        }
                    });
                }
                foreach (Process process in processes)
                {
                    if (!trackedProcesses.ContainsKey(process.Id))
                    {
                        string place = "";
                        string username = "";
                        IntPtr hWnd = process.MainWindowHandle;
                        StringBuilder title = new StringBuilder(256);
                        GetWindowText(hWnd, title, title.Capacity);
                        string logPath = GetLogFilePath(process.Id);

                        //check title name for real main window
                        if (title.ToString() != "Roblox" && !string.IsNullOrEmpty(title.ToString()))
                        {
                            this.Invoke((MethodInvoker)delegate
                            {
                                bool exists = false;
                                foreach (DataGridViewRow row in dataGridView.Rows)
                                {
                                    if (row.Cells[0].Value.ToString() == process.Id.ToString())
                                    {
                                        exists = true;
                                        break;
                                    }
                                }
                                if (!exists)
                                {
                                    place = title.ToString().Split('-')[1].Trim();
                                    username = title.ToString().Split('-')[0].Trim();
                                    this.Invoke((MethodInvoker)delegate
                                    {
                                        if (!cb_game.Items.Contains(place))
                                        {
                                            cb_game.Items.Add(place);
                                        }
                                    });
                                    dataGridView.Rows.Add(process.Id, place, username, logPath);
                                    trackedProcesses.Add(process.Id, new RobloxProcessInfo
                                    {
                                        PID = process.Id,
                                        Status = "Running",
                                        Username = username,
                                        Process = process,
                                        LogPath = logPath
                                    });
                                }
                            });
                            continue;
                        }
                        if (logPath != null)
                        {
                            (place, username) = GetDataFromLog(logPath);
                            if (username != "-")
                            {
                                // Get main window handle again for avoid old main window
                                hWnd = process.MainWindowHandle;

                                SetWindowText(hWnd, $"{username} - {place}");
                            }
                            this.Invoke((MethodInvoker)delegate
                            {
                                bool exists = false;
                                foreach (DataGridViewRow row in dataGridView.Rows)
                                {
                                    if (row.Cells[0].Value.ToString() == process.Id.ToString())
                                    {
                                        exists = true;
                                        break;
                                    }
                                }
                                if (!exists)
                                {
                                    dataGridView.Rows.Add(process.Id, place, username, logPath);
                                    trackedProcesses.Add(process.Id, new RobloxProcessInfo
                                    {
                                        PID = process.Id,
                                        Status = "Running",
                                        Username = username,
                                        Process = process,
                                        LogPath = logPath
                                    });
                                }
                            });
                        }
                    }
                }
                this.Invoke((MethodInvoker)delegate
                {
                    lb_tab.Text = $"Số lượng tab: {trackedProcesses.Count}";
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Đã xảy ra lỗi process: {ex.Message}", "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private string GetLogFilePath(int pid)
        {
            for (int i = 0; i < 10; i++)
            {
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo
                    {
                        FileName = handlePath,
                        Arguments = $"-p {pid} -nobanner",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    using (Process handleProcess = Process.Start(psi))
                    {
                        string output = handleProcess.StandardOutput.ReadToEnd();
                        handleProcess.WaitForExit();

                        string pattern = @"File\s+(C:\\.*?\.log)";
                        Match match = Regex.Match(output, pattern);
                        if (match.Success)
                        {
                            string logPath = match.Groups[1].Value.Trim();
                            if (File.Exists(logPath))
                            {
                                return logPath;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Đã xảy ra lỗi khi lấy đường dẫn file log: {ex.Message}", "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                Thread.Sleep(2000);
            }
            return null;
        }

        private (string,string) GetDataFromLog(string logPath)
        {
            for (int attempt = 0; attempt < 3; attempt++) // Maximum 3 attempts
            {
                try
                {
                    using (FileStream fs = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (StreamReader sr = new StreamReader(fs, Encoding.UTF8, true))
                    {
                        string content = sr.ReadToEnd();
                        string[] lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

                        // Load data.json once
                        string jsonData = File.Exists("data.json")
                            ? File.ReadAllText("data.json", Encoding.UTF8)
                            : JsonConvert.SerializeObject(new { Accounts = new List<object>(), Places = new List<object>() });
                        dynamic json = JsonConvert.DeserializeObject(jsonData);

                        foreach (string line in lines)
                        {
                            if (line.Contains("universeid:") && line.Contains("userid:"))
                            {
                                string sourcename = "";
                                string username = "";
                                string universeid = "";
                                string userid = "";

                                // Extract universeid
                                string pattern = @"universeid:\s*(\d+)";
                                Match match = Regex.Match(line, pattern);
                                if (match.Success)
                                {
                                    universeid = match.Groups[1].Value;
                                    foreach (var place in json.Places)
                                    {
                                        if (place.PlaceID.ToString() == universeid)
                                        {
                                            sourcename = place.Placename.ToString();
                                        }
                                    }
                                    if (string.IsNullOrEmpty(sourcename))
                                    {
                                        string apiUrl = $"https://games.roblox.com/v1/games?universeIds={universeid}";
                                        using (WebClient client = new WebClient())
                                        {
                                            client.Headers[HttpRequestHeader.ContentType] = "application/json";
                                            client.Encoding = Encoding.UTF8;
                                            string jsonResponse = client.DownloadString(apiUrl);
                                            dynamic gameJson = JsonConvert.DeserializeObject(jsonResponse);
                                            if (gameJson?.data?.Count > 0)
                                            {
                                                sourcename = gameJson.data[0].sourceName?.ToString() ?? "Unknown Place";

                                                // Save PlaceID and Placename to data.json
                                                var places = json.Places.ToObject<List<dynamic>>();
                                                places.Add(new { PlaceID = universeid, Placename = sourcename });
                                                json.Places = JsonConvert.DeserializeObject(JsonConvert.SerializeObject(places));
                                            }
                                        }
                                    }
                                }

                                // Add sourcename for cb_game
                                if (!string.IsNullOrEmpty(universeid) && !string.IsNullOrEmpty(sourcename))
                                {
                                    this.Invoke((MethodInvoker)delegate
                                    {
                                        if (!cb_game.Items.Contains(sourcename))
                                        {
                                            cb_game.Items.Add(sourcename);
                                        }
                                    });
                                }

                                // Extract userid
                                pattern = @"userid:\s*(\d+)";
                                match = Regex.Match(line, pattern);
                                if (match.Success)
                                {
                                    userid = match.Groups[1].Value;
                                    foreach (var account in json.Accounts)
                                    {
                                        if (account.UserID.ToString() == userid)
                                        {
                                            username = account.Username.ToString();
                                        }
                                    }
                                    if (string.IsNullOrEmpty(username))
                                    {
                                        string apiUrl = $"https://users.roblox.com/v1/users/{userid}";
                                        using (WebClient client = new WebClient())
                                        {
                                            client.Headers[HttpRequestHeader.ContentType] = "application/json";
                                            string jsonResponse = client.DownloadString(apiUrl);
                                            dynamic userJson = JsonConvert.DeserializeObject(jsonResponse);
                                            username = userJson?.name?.ToString() ?? "Unknown User";

                                            // Save UserID and Username to data.json
                                            var accounts = json.Accounts.ToObject<List<dynamic>>();
                                            accounts.Add(new { UserID = userid, Username = username });
                                            json.Accounts = JsonConvert.DeserializeObject(JsonConvert.SerializeObject(accounts));
                                        }
                                    }
                                }

                                File.WriteAllText("data.json", JsonConvert.SerializeObject(json, Formatting.Indented), Encoding.UTF8);

                                if (!string.IsNullOrEmpty(sourcename) && !string.IsNullOrEmpty(username))
                                {
                                    return (sourcename,username);
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Đã xảy ra lỗi khi đọc file log: {ex.Message}", "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }

                if (attempt < 2)
                {
                    Thread.Sleep(5000);
                }
            }
            return ("-","-");
        }

        private void bt_show_Click(object sender, EventArgs e)
        {
            try
            {
                if (dataGridView.SelectedRows.Count > 0)
                {
                    foreach (DataGridViewRow row in dataGridView.SelectedRows)
                    {
                        int pid = Convert.ToInt32(row.Cells["PID"].Value);
                        if (trackedProcesses.ContainsKey(pid))
                        {
                            Process process = trackedProcesses[pid].Process;
                            IntPtr hWnd = process.MainWindowHandle;
                            ShowWindow(hWnd, 9);
                            SetForegroundWindow(hWnd);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Đã xảy ra lỗi khi hiển thị process: {ex.Message}", "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void bt_clean_Click(object sender, EventArgs e)
        {
            try
            {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string robloxLogsPath = Path.Combine(localAppData, "Roblox", "Logs");
                if (!Directory.Exists(robloxLogsPath))
                {
                    MessageBox.Show("Ê ê sao không có folder logs vậy!", "Lao công said:", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                string[] logFiles = Directory.GetFiles(robloxLogsPath, "*.log", SearchOption.TopDirectoryOnly);
                int deletedCount = 0;
                foreach (string logFile in logFiles)
                {
                    try
                    {
                        // Check if the file is in use by any tracked process
                        bool isInUse = trackedProcesses.Values.Any(p => p.LogPath != null && p.LogPath.Equals(logFile, StringComparison.OrdinalIgnoreCase));
                        if (!isInUse)
                        {
                            File.Delete(logFile);
                            deletedCount++;
                        }
                    }
                    catch (IOException)
                    {
                        // Skip
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // Skip
                    }
                }

                MessageBox.Show($"Đã đốt {deletedCount} file log.", "Lao công said:", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Đã xảy ra lỗi khi xóa file log: {ex.Message}", "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void bt_close_Click(object sender, EventArgs e)
        {
            try
            {
                if (dataGridView.SelectedRows.Count > 0)
                {
                    if (MessageBox.Show("Bro muốn tắt process này thật à?", "Hỏi chơi cái (´。＿。｀)",
                        MessageBoxButtons.YesNo) == DialogResult.Yes)
                    {
                        foreach (DataGridViewRow row in dataGridView.SelectedRows)
                        {
                            int pid = Convert.ToInt32(row.Cells["PID"].Value);
                            if (trackedProcesses.ContainsKey(pid))
                            {
                                Process process = trackedProcesses[pid].Process;
                                process.Kill();
                                trackedProcesses.Remove(pid);
                                dataGridView.Rows.Remove(row);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Đã xảy ra lỗi khi tắt process: {ex.Message}", "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        private void cb_game_SelectedIndexChanged(object sender, EventArgs e)
        {
            string selectedGame = cb_game.SelectedItem?.ToString();

            foreach (DataGridViewRow row in dataGridView.Rows)
            {
                string place = row.Cells[1].Value?.ToString();
                if (selectedGame == "All")
                {
                    row.Visible = true;
                }
                else
                {
                    row.Visible = place == selectedGame;
                }
            }
        }

        private void link_qbgamer_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            Process.Start(new ProcessStartInfo("https://github.com/QBGamer") { UseShellExecute = true });
        }
    }
    public class RobloxProcessInfo
    {
        public int PID { get; set; }
        public string Status { get; set; }
        public string Username { get; set; }
        public Process Process { get; set; }
        public string LogPath { get; set; }
    }
}