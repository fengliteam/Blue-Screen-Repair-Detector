using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
using System.Text.Json;
using System.Net.Http;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Security.Principal;

namespace Blue_Screen_Repair_Detector
{
    public partial class Form1 : Form
    {
        #region 字段和常量
        private BackgroundWorker _backgroundWorker;
        private BackgroundWorker _repairWorker;
        private List<ShutdownEvent> _shutdownEvents;
        private BlueScreenInfo _blueScreenInfo;
        private bool _dumpFilesEnabled;
        private int _currentStep = 0;
        private const int TOTAL_STEPS = 6;
        private System.Data.DataTable _diagnosticDataTable;
        private ConfigManager _configManager;
        private Label _configStatusLabel;
        private static readonly Logger Logger = new Logger();
        private readonly Stopwatch _diagnosticStopwatch = new Stopwatch();
        private delegate void SafeUpdateUIDelegate(string text, int progress);
        private delegate void SafeUpdateProgressDelegate(int progress, string message);
        private const string REMOTE_CONFIG_URL = "https://raw.githubusercontent.com/fengliteam/BARD-File/refs/heads/main/gx.json";
        private const string USER_CONFIG_PATH = "user_config.json";
        #endregion

        #region 构造函数
        public Form1()
        {
            InitializeComponent();
            Logger.LogSystemInfo();
            CreateConfigStatusLabel();
            _configManager = new ConfigManager(Logger, REMOTE_CONFIG_URL, USER_CONFIG_PATH);
            _configManager.Initialize();
            _shutdownEvents = new List<ShutdownEvent>();
            _blueScreenInfo = new BlueScreenInfo();
            _diagnosticDataTable = new System.Data.DataTable();
            InitializeBackgroundWorker();
            InitializeRepairWorker();
            label1.Text = "准备开始诊断...";
            UpdateProgressText(0);
            DisplayConfigInfo();
        }
        #endregion

        #region UI初始化
        private void CreateConfigStatusLabel()
        {
            _configStatusLabel = new Label
            {
                Text = "Loading configuration...",
                Dock = DockStyle.Bottom,
                Height = 20,
                TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = System.Drawing.Color.LightYellow
            };
            Controls.Add(_configStatusLabel);
        }

        private void InitializeBackgroundWorker()
        {
            _backgroundWorker = new BackgroundWorker
            {
                WorkerReportsProgress = true,
                WorkerSupportsCancellation = true
            };
            _backgroundWorker.DoWork += BackgroundWorker_DoWork;
            _backgroundWorker.ProgressChanged += BackgroundWorker_ProgressChanged;
            _backgroundWorker.RunWorkerCompleted += BackgroundWorker_RunWorkerCompleted;
        }

        private void InitializeRepairWorker()
        {
            _repairWorker = new BackgroundWorker
            {
                WorkerReportsProgress = true,
                WorkerSupportsCancellation = true
            };
            _repairWorker.DoWork += RepairWorker_DoWork;
            _repairWorker.ProgressChanged += RepairWorker_ProgressChanged;
            _repairWorker.RunWorkerCompleted += RepairWorker_RunWorkerCompleted;
        }
        #endregion

        #region 线程安全的UI更新方法
        private void SafeUpdateUI(string text, int progress)
        {
            if (label1.InvokeRequired || 进度条且拥有6个格子.InvokeRequired)
            {
                Invoke(new SafeUpdateUIDelegate(SafeUpdateUI), text, progress);
            }
            else
            {
                label1.Text = text;
                进度条且拥有6个格子.Value = progress;
            }
        }

        private void SafeUpdateProgress(int progress, string message)
        {
            if (进度条且拥有6个格子.InvokeRequired || label1.InvokeRequired)
            {
                Invoke(new SafeUpdateProgressDelegate(SafeUpdateProgress), progress, message);
            }
            else
            {
                进度条且拥有6个格子.Value = progress;
                label1.Text = message;
                UpdateStepFromProgress(progress);
            }
        }

        private void SafeUpdateProgressText(int step)
        {
            if (进度条且拥有6个格子.InvokeRequired)
            {
                Invoke(() => SafeUpdateProgressText(step));
            }
            else
            {
                _currentStep = step;
                进度条且拥有6个格子.Text = $"{step}/{TOTAL_STEPS}";
            }
        }

        private void SafeEnableStartButton(bool enable)
        {
            if (开始检测的按钮.InvokeRequired)
            {
                Invoke(() => SafeEnableStartButton(enable));
            }
            else
            {
                开始检测的按钮.Enabled = enable;
            }
        }
        #endregion

        #region 进度管理
        private void UpdateProgressText(int step) => _currentStep = step;

        private void UpdateStepFromProgress(int progressPercentage) =>
            SafeUpdateProgressText(progressPercentage switch
            {
                < 15 => 0,
                < 30 => 1,
                < 45 => 2,
                < 60 => 3,
                < 75 => 4,
                < 90 => 5,
                _ => 6
            });
        #endregion

        #region 诊断功能
        private async void 开始检测的按钮_Click(object sender, EventArgs e)
        {
            SafeUpdateProgress(0, "准备开始诊断...");
            SafeUpdateProgressText(0);
            SafeEnableStartButton(false);
            ClearTableData();
            _diagnosticStopwatch.Restart();
            await Task.Run(() => _backgroundWorker.RunWorkerAsync());
        }

        private void ClearTableData()
        {
            if (table1.InvokeRequired)
            {
                Invoke(ClearTableData);
                return;
            }
            table1.DataSource = null;
            _diagnosticDataTable?.Clear();
        }

        private void BackgroundWorker_DoWork(object sender, DoWorkEventArgs e)
        {
            var tasks = new List<Task>();
            Task<List<ShutdownEvent>> shutdownEventsTask = Task.Run(AnalyzeShutdownEvents);
            Task<BlueScreenInfo> blueScreenTask = Task.Run(AnalyzeBlueScreen);
            Task<bool> dumpConfigTask = Task.Run(CheckDumpFileSettings);
            Task.WhenAll(shutdownEventsTask, blueScreenTask, dumpConfigTask).Wait();
            _backgroundWorker.ReportProgress(30, "正在分析系统事件日志...");
            _shutdownEvents = shutdownEventsTask.Result;
            _backgroundWorker.ReportProgress(45, "正在检查蓝屏崩溃记录...");
            _blueScreenInfo = blueScreenTask.Result;
            _backgroundWorker.ReportProgress(60, "正在检查系统转储配置...");
            _dumpFilesEnabled = dumpConfigTask.Result;
            if (_blueScreenInfo != null && !string.IsNullOrEmpty(_blueScreenInfo.StopCode))
            {
                _backgroundWorker.ReportProgress(75, "正在分析内存转储文件...");
                AnalyzeDumpFilesInDepth();
            }
            else
            {
                _backgroundWorker.ReportProgress(75, "跳过转储文件分析...");
            }
            _backgroundWorker.ReportProgress(90, "正在生成诊断报告...");
            _backgroundWorker.ReportProgress(100, "诊断完成");
        }

        private void BackgroundWorker_ProgressChanged(object sender, ProgressChangedEventArgs e) =>
            SafeUpdateProgress(e.ProgressPercentage, e.UserState?.ToString() ?? "Processing...");

        private void BackgroundWorker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            _diagnosticStopwatch.Stop();
            SafeUpdateProgress(100, "诊断完成");
            SafeUpdateProgressText(6);
            SafeEnableStartButton(true);
            if (e.Error != null)
            {
                SafeUpdateUI($"诊断错误: {e.Error.Message}", 0);
                return;
            }
            if (e.Cancelled)
            {
                SafeUpdateUI("诊断已取消", 0);
                return;
            }
            if (InvokeRequired)
                Invoke(DisplayDiagnosticResults);
            else
                DisplayDiagnosticResults();
        }
        #endregion

        #region 事件分析
        private List<ShutdownEvent> AnalyzeShutdownEvents()
        {
            var events = new List<ShutdownEvent>();
            var startTime = DateTime.Now.AddDays(-30);
            var shutdownEventIds = new HashSet<int> { 6006, 6008, 6009, 41, 1074, 1076, 109, 1001, 6005, 6007 };
            try
            {
                using var log = new EventLog("System");
                int checkedEntries = 0;
                int maxEntriesToCheck = 1000;
                for (int i = log.Entries.Count - 1; i >= 0 && checkedEntries < maxEntriesToCheck; i--)
                {
                    var entry = log.Entries[i];
                    checkedEntries++;
                    if (entry.TimeGenerated < startTime) continue;
                    if (!shutdownEventIds.Contains((int)entry.InstanceId)) continue;
                    events.Add(new ShutdownEvent
                    {
                        Timestamp = entry.TimeGenerated,
                        EventId = entry.InstanceId.ToString(),
                        Source = entry.Source,
                        Description = GetEventDescription(entry),
                        Type = DetermineShutdownType(entry)
                    });
                    if (events.Count >= 50) break;
                }
                events = events.OrderByDescending(x => x.Timestamp).Take(20).ToList();
            }
            catch { }
            return events;
        }

        private string GetEventDescription(EventLogEntry entry) => entry.InstanceId switch
        {
            109 => "系统休眠/唤醒事件",
            41 => "系统意外重启 - 可能由于电源问题或系统崩溃",
            6006 => "正常系统关机",
            6008 => "系统异常关机",
            1074 => "用户发起的系统关机",
            1001 => "Windows错误报告事件",
            _ => string.IsNullOrEmpty(entry.Message) ? "无详细描述" :
                (entry.Message.Length > 80 ? entry.Message[..80] + "..." : entry.Message)
        };

        private ShutdownType DetermineShutdownType(EventLogEntry entry) => entry.InstanceId switch
        {
            6006 or 6005 or 6007 or 109 or 1074 => ShutdownType.Normal,
            6008 => ShutdownType.ForceShutdown,
            41 => AnalyzeEvent41(entry),
            1001 => AnalyzeEvent1001(entry),
            _ => ShutdownType.Unknown
        };

        private ShutdownType AnalyzeEvent41(EventLogEntry entry)
        {
            var message = entry.Message.AsSpan();
            if (message.IndexOf("电源".AsSpan(), StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("power".AsSpan(), StringComparison.OrdinalIgnoreCase) >= 0)
                return ShutdownType.PowerLoss;
            if (message.IndexOf("BugCheck".AsSpan(), StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("0x".AsSpan()) >= 0)
                return ShutdownType.BlueScreen;
            return ShutdownType.ForceShutdown;
        }

        private ShutdownType AnalyzeEvent1001(EventLogEntry entry)
        {
            var message = entry.Message.AsSpan();
            if (message.IndexOf("BugCheck".AsSpan(), StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("蓝屏".AsSpan(), StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("crash".AsSpan(), StringComparison.OrdinalIgnoreCase) >= 0)
                return ShutdownType.BlueScreen;
            return ShutdownType.Unknown;
        }
        #endregion

        #region 蓝屏分析
        private BlueScreenInfo AnalyzeBlueScreen()
        {
            var bsInfo = new BlueScreenInfo();
            var analysisMethods = new Func<BlueScreenInfo>[]
            {
                GetBlueScreenFromWER,
                GetBlueScreenFromSystemEvents,
                GetBlueScreenFromDumpFiles
            };
            var parallelResult = analysisMethods
                .AsParallel()
                .WithDegreeOfParallelism(3)
                .Select(method => method())
                .FirstOrDefault(result => result != null && !string.IsNullOrEmpty(result.StopCode) && result.StopCode != "UNKNOWN_ERROR");
            if (parallelResult != null)
            {
                bsInfo = parallelResult;
            }
            return bsInfo;
        }

        private BlueScreenInfo GetBlueScreenFromWER()
        {
            var bsInfo = new BlueScreenInfo();
            try
            {
                if (EventLog.SourceExists("Windows Error Reporting"))
                {
                    using var log = new EventLog("Application");
                    var startTime = DateTime.Now.AddDays(-30);
                    for (int i = log.Entries.Count - 1; i >= Math.Max(0, log.Entries.Count - 200); i--)
                    {
                        var entry = log.Entries[i];
                        if (entry.TimeGenerated < startTime) continue;
                        if (entry.Source == "Windows Error Reporting" &&
                            (entry.Message.IndexOf("blue", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             entry.Message.IndexOf("crash", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             entry.Message.IndexOf("BugCheck", StringComparison.OrdinalIgnoreCase) >= 0))
                        {
                            bsInfo.CrashTime = entry.TimeGenerated;
                            bsInfo.StopCode = ExtractStopCodeFromMessage(entry.Message);
                            bsInfo.Description = "检测到Windows错误报告";
                            break;
                        }
                    }
                }
            }
            catch { }
            return bsInfo;
        }

        private BlueScreenInfo GetBlueScreenFromSystemEvents()
        {
            var bsInfo = new BlueScreenInfo();
            try
            {
                using var log = new EventLog("System");
                var startTime = DateTime.Now.AddDays(-30);
                for (int i = log.Entries.Count - 1; i >= Math.Max(0, log.Entries.Count - 200); i--)
                {
                    var entry = log.Entries[i];
                    if (entry.TimeGenerated < startTime) continue;
                    if (entry.Source == "BugCheck" || (entry.InstanceId == 1001 && entry.Message.IndexOf("BugCheck", StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        bsInfo.CrashTime = entry.TimeGenerated;
                        bsInfo.StopCode = ExtractStopCodeFromMessage(entry.Message);
                        bsInfo.Description = "检测到系统蓝屏事件";
                        break;
                    }
                }
            }
            catch { }
            return bsInfo;
        }

        private BlueScreenInfo GetBlueScreenFromDumpFiles()
        {
            var bsInfo = new BlueScreenInfo();
            try
            {
                string[] dumpPaths = {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Minidump"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "MEMORY.DMP"),
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows)
                };
                var dumpFileTasks = dumpPaths
                    .AsParallel()
                    .WithDegreeOfParallelism(2)
                    .Select(SearchDumpFile)
                    .Where(result => !string.IsNullOrEmpty(result))
                    .ToList();
                if (dumpFileTasks.Any())
                {
                    bsInfo.DumpFilePath = dumpFileTasks[0];
                    bsInfo.CrashTime = File.GetLastWriteTime(bsInfo.DumpFilePath);
                    bsInfo.StopCode = "发现转储文件";
                    bsInfo.Description = "存在系统内存转储文件";
                }
            }
            catch { }
            return bsInfo;
        }

        private string SearchDumpFile(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    return Directory.EnumerateFiles(path, "*.dmp", SearchOption.TopDirectoryOnly)
                        .OrderByDescending(f => new FileInfo(f).LastWriteTime)
                        .FirstOrDefault();
                }
                if (File.Exists(path) && path.EndsWith(".dmp", StringComparison.OrdinalIgnoreCase))
                {
                    return path;
                }
            }
            catch { }
            return null;
        }

        private string ExtractStopCodeFromMessage(string message)
        {
            if (string.IsNullOrEmpty(message)) return null;
            try
            {
                var config = _configManager.GetConfig();
                if (config?.ErrorPatterns != null)
                {
                    var matchedPattern = config.ErrorPatterns
                        .AsParallel()
                        .FirstOrDefault(pattern => message.IndexOf(pattern.Pattern, StringComparison.OrdinalIgnoreCase) >= 0);
                    if (matchedPattern != null)
                    {
                        if (matchedPattern.Type == "Hexadecimal")
                        {
                            return ExtractHexadecimalCode(message);
                        }
                        return matchedPattern.Pattern;
                    }
                }
                return ExtractStopCodeWithSpan(message);
            }
            catch
            {
                return "EXTRACTION_ERROR";
            }
        }

        private string ExtractStopCodeWithSpan(string message)
        {
            ReadOnlySpan<char> span = message.AsSpan();
            if (span.Contains("BugCheck".AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                int index = span.IndexOf("BugCheck".AsSpan(), StringComparison.OrdinalIgnoreCase);
                if (index >= 0)
                {
                    ReadOnlySpan<char> remaining = span.Slice(index + 8);
                    int spaceIndex = remaining.IndexOf(' ');
                    if (spaceIndex > 0)
                    {
                        return remaining.Slice(0, spaceIndex).Trim().ToString();
                    }
                }
            }
            int hexIndex = span.IndexOf("0x".AsSpan());
            if (hexIndex >= 0)
            {
                ReadOnlySpan<char> hexPart = span.Slice(hexIndex);
                int endIndex = hexPart.IndexOfAny(new char[] { ' ', ',', '\n', '\r', ':', ';', ')' });
                if (endIndex > 0)
                {
                    return hexPart.Slice(0, endIndex).ToString();
                }
            }
            return "UNKNOWN_ERROR";
        }

        private string ExtractHexadecimalCode(string message)
        {
            try
            {
                ReadOnlySpan<char> span = message.AsSpan();
                string[] patterns = { "0x", "BugCheck", "停止代码", "stop code" };
                foreach (string pattern in patterns)
                {
                    int index = span.IndexOf(pattern.AsSpan(), StringComparison.OrdinalIgnoreCase);
                    if (index >= 0)
                    {
                        ReadOnlySpan<char> remaining = span.Slice(index + pattern.Length).TrimStart();
                        int spaceIndex = remaining.IndexOfAny(new char[] { ' ', ',', '\n', '\r', ':', ';', ')' });
                        if (spaceIndex > 0)
                        {
                            ReadOnlySpan<char> code = remaining.Slice(0, spaceIndex);
                            if (code.Length <= 10 && code.IndexOf("0x".AsSpan(), StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                return code.ToString();
                            }
                        }
                    }
                }
            }
            catch { }
            return "UNKNOWN_HEX_CODE";
        }
        #endregion

        #region 系统配置检查
        private bool CheckDumpFileSettings()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\CrashControl");
                if (key == null) return false;
                var crashDumpEnabled = key.GetValue("CrashDumpEnabled");
                if (crashDumpEnabled != null)
                {
                    int dumpType = (int)crashDumpEnabled;
                    return dumpType == 1 || dumpType == 2 || dumpType == 3 || dumpType == 7;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        private void AnalyzeDumpFilesInDepth() => Thread.Sleep(200);
        #endregion

        #region 结果显示
        private void DisplayDiagnosticResults()
        {
            try
            {
                _diagnosticDataTable = new System.Data.DataTable();
                _diagnosticDataTable.Columns.Add("时间", typeof(string));
                _diagnosticDataTable.Columns.Add("事件类型", typeof(string));
                _diagnosticDataTable.Columns.Add("事件ID", typeof(string));
                _diagnosticDataTable.Columns.Add("描述", typeof(string));
                if (_shutdownEvents != null && _shutdownEvents.Count > 0)
                {
                    var abnormalEvents = _shutdownEvents.Where(e => e.Type != ShutdownType.Normal).ToList();
                    foreach (var eventItem in abnormalEvents)
                    {
                        _diagnosticDataTable.Rows.Add(
                            eventItem.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"),
                            eventItem.Type.ToString(),
                            eventItem.EventId,
                            eventItem.Description
                        );
                    }
                }
                if (_diagnosticDataTable.Rows.Count == 0 && _shutdownEvents != null && _shutdownEvents.Count > 0)
                {
                    _diagnosticDataTable.Rows.Add(
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                        "系统状态",
                        "INFO",
                        "✅ 未发现异常关机事件，系统运行正常"
                    );
                }
                string bsodStatus = "✅ 未检测到蓝屏记录";
                string bsodTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                if (_blueScreenInfo != null && !string.IsNullOrEmpty(_blueScreenInfo.StopCode))
                {
                    bsodStatus = $"⚠️ 检测到蓝屏事件 - 停止代码: {_blueScreenInfo.StopCode}";
                    if (_blueScreenInfo.CrashTime > DateTime.MinValue)
                        bsodTime = _blueScreenInfo.CrashTime.ToString("yyyy-MM-dd HH:mm:ss");
                }
                _diagnosticDataTable.Rows.Add(bsodTime, "蓝屏检测", "BSOD", bsodStatus);
                if (!_dumpFilesEnabled)
                {
                    _diagnosticDataTable.Rows.Add(
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                        "系统配置",
                        "CONFIG",
                        "⚠️ 内存转储功能未启用 - 建议启用以便分析系统问题"
                    );
                }
                var config = _configManager.GetConfig();
                if (config != null)
                {
                    _diagnosticDataTable.Rows.Add(
                        config.LastUpdated,
                        "配置信息",
                        "CONFIG",
                        $"识别库版本: {config.Version} | 最后更新: {config.LastUpdated} | 服务器: {REMOTE_CONFIG_URL}"
                    );
                }
                if (table1.InvokeRequired)
                    table1.Invoke(() => table1.DataSource = _diagnosticDataTable);
                else
                    table1.DataSource = _diagnosticDataTable;
            }
            catch { }
        }

        private void DisplayConfigInfo()
        {
            var config = _configManager.GetConfig();
            if (config != null)
            {
                string configText = $"识别库版本: {config.Version} | 最后更新: {config.LastUpdated} | 服务器: {REMOTE_CONFIG_URL}";
                if (_configStatusLabel.InvokeRequired)
                    _configStatusLabel.Invoke(() => _configStatusLabel.Text = configText);
                else
                    _configStatusLabel.Text = configText;
            }
        }
        #endregion

        #region 表格交互
        private void table1_CellClick(object sender, AntdUI.TableClickEventArgs e)
        {
            if (_diagnosticDataTable == null || e.RowIndex < 0 || e.RowIndex >= _diagnosticDataTable.Rows.Count) return;
            var row = _diagnosticDataTable.Rows[e.RowIndex];
            string time = row["时间"].ToString();
            string eventType = row["事件类型"].ToString();
            string eventId = row["事件ID"].ToString();
            string description = row["描述"].ToString();
            string detailedInfo = $"时间: {time}\n事件类型: {eventType}\n事件ID: {eventId}\n描述: {description}\n";
            detailedInfo += GetProblemAnalysis(eventType, eventId, description);
            if (eventType == "蓝屏检测" && description.Contains("检测到蓝屏事件"))
            {
                var result = MessageBox.Show(detailedInfo + "\n是否立即尝试自动修复？",
                    "问题分析", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (result == DialogResult.Yes)
                {
                    StartAutoRepair(_blueScreenInfo?.StopCode);
                }
            }
            else
            {
                MessageBox.Show(detailedInfo, "问题分析", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private string GetProblemAnalysis(string eventType, string eventId, string description) => eventType switch
        {
            "ForceShutdown" => GetForceShutdownAnalysis(),
            "PowerLoss" => GetPowerLossAnalysis(),
            "BlueScreen" => GetBlueScreenAnalysis(),
            "蓝屏检测" when description.Contains("检测到蓝屏事件") => GetBlueScreenDetectionAnalysis(),
            "系统配置" => GetSystemConfigAnalysis(),
            "系统状态" when description.Contains("运行正常") => GetSystemNormalAnalysis(),
            "配置信息" => GetConfigInfoAnalysis(),
            _ => GetDefaultAnalysis()
        };

        private string GetForceShutdownAnalysis() =>
            "🔍 问题分析：\n" +
            "• 系统被强制关闭\n" +
            "• 可能由于长按电源键或系统无响应\n" +
            "💡 解决方案：\n" +
            "1. 尽量避免强制关机，使用正常关机流程\n" +
            "2. 如系统无响应，可尝试Ctrl+Alt+Del调出任务管理器\n" +
            "3. 定期保存工作数据，防止意外丢失";

        private string GetPowerLossAnalysis() =>
            "🔍 问题分析：\n" +
            "• 检测到电源中断\n" +
            "• 可能由于停电、电源线松动或电源故障\n" +
            "💡 解决方案：\n" +
            "1. 检查电源连接是否牢固\n" +
            "2. 考虑使用UPS不间断电源\n" +
            "3. 如频繁发生，检查电源设备和电路";

        private string GetBlueScreenAnalysis()
        {
            string stopCode = _blueScreenInfo?.StopCode ?? "未知";
            var strategy = GetRepairStrategy(stopCode);
            return $"🔍 问题分析：\n" +
                   $"• 系统发生蓝屏崩溃\n" +
                   $"• 停止代码: {stopCode}\n" +
                   $"• {strategy?.UserMessage ?? "可能由于驱动程序冲突、硬件故障或系统文件损坏"}\n" +
                   $"💡 解决方案：\n" +
                   $"1. 尝试自动修复\n" +
                   $"2. 查看具体停止代码以确定根本原因";
        }

        private string GetBlueScreenDetectionAnalysis()
        {
            string stopCode = _blueScreenInfo?.StopCode ?? "未知";
            var strategy = GetRepairStrategy(stopCode);
            return $"🔍 问题分析：\n" +
                   $"• 系统历史记录中存在蓝屏事件\n" +
                   $"• 停止代码: {stopCode}\n" +
                   $"• {strategy?.UserMessage ?? "需要进一步分析确定根本原因"}\n" +
                   $"💡 解决方案：\n" +
                   $"1. 尝试自动修复\n" +
                   $"2. 或手动执行修复命令";
        }

        private string GetSystemConfigAnalysis() =>
            "🔍 问题分析：\n" +
            "• 内存转储功能未启用\n" +
            "• 发生系统崩溃时无法生成分析文件\n" +
            "💡 解决方案：\n" +
            "1. 右键点击'此电脑'→属性\n" +
            "2. 选择'高级系统设置'\n" +
            "3. 在'启动和故障恢复'中点击'设置'\n" +
            "4. 将'写入调试信息'设置为'小内存转储(256 KB)'";

        private string GetSystemNormalAnalysis() =>
            "✅ 系统状态良好\n" +
            "• 未发现异常关机事件\n" +
            "• 系统运行稳定\n" +
            "💡 维护建议：\n" +
            "1. 定期进行系统更新\n" +
            "2. 保持驱动程序最新\n" +
            "3. 定期备份重要数据";

        private string GetConfigInfoAnalysis()
        {
            var config = _configManager.GetConfig();
            return $"ℹ️ 识别库信息\n" +
                   $"• 版本: {config?.Version ?? "未知"}\n" +
                   $"• 最后更新: {config?.LastUpdated ?? "未知"}\n" +
                   $"• 支持的错误模式: {config?.ErrorPatterns?.Count ?? 0} 种\n" +
                   $"• 修复策略: {config?.RepairStrategies?.Count ?? 0} 种\n" +
                   $"• 配置源: {REMOTE_CONFIG_URL}";
        }

        private string GetDefaultAnalysis() =>
            "ℹ️ 系统事件记录\n" +
            "• 这是正常的系统操作记录\n" +
            "• 无需特别处理";
        #endregion

        #region 自动修复
        private void StartAutoRepair(string stopCode)
        {
            if (string.IsNullOrEmpty(stopCode)) return;
            var strategy = GetRepairStrategy(stopCode);
            if (strategy == null)
            {
                MessageBox.Show($"未找到针对错误代码 '{stopCode}' 的修复策略。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (!CheckAdminPrivileges())
            {
                MessageBox.Show("自动修复需要管理员权限。请以管理员身份运行此程序。", "权限不足",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            string confirmMessage = $"确定要执行以下修复操作吗？\n" +
                                  $"错误代码: {stopCode}\n" +
                                  $"问题描述: {strategy.Description}\n" +
                                  $"将执行 {strategy.RepairCommands.Count} 个修复命令";
            var result = MessageBox.Show(confirmMessage, "确认修复",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (result == DialogResult.Yes)
            {
                SafeEnableStartButton(false);
                _repairWorker.RunWorkerAsync(strategy);
            }
        }

        private RepairStrategy GetRepairStrategy(string stopCode)
        {
            if (string.IsNullOrEmpty(stopCode)) return null;
            var strategy = _configManager.GetRepairStrategy(stopCode);
            if (strategy != null)
            {
                return strategy;
            }
            return _configManager.GetRepairStrategy("GENERAL_SYSTEM_REPAIR");
        }

        private bool CheckAdminPrivileges()
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        private void RepairWorker_DoWork(object sender, DoWorkEventArgs e)
        {
            var strategy = e.Argument as RepairStrategy;
            if (strategy == null) return;
            _repairWorker.ReportProgress(0, "开始自动修复...");
            var commands = strategy.RepairCommands;
            int completedCount = 0;
            object lockObj = new object();
            Parallel.For(0, commands.Count, new ParallelOptions { MaxDegreeOfParallelism = 2 }, i =>
            {
                if (_repairWorker.CancellationPending)
                {
                    e.Cancel = true;
                    return;
                }
                var command = commands[i];
                ExecuteRepairCommand(command);
                lock (lockObj)
                {
                    completedCount++;
                    int progress = (completedCount * 100) / commands.Count;
                    _repairWorker.ReportProgress(progress, $"已完成: {command.Description}");
                }
            });
            _repairWorker.ReportProgress(100, "修复完成");
        }

        private void ExecuteRepairCommand(RepairCommand command)
        {
            string output = "";
            string error = "";
            bool success = false;
            switch (command.Type.ToLower())
            {
                case "powershell":
                    success = TryExecutePowerShell(command.Command, out output, out error);
                    break;
                case "cmd":
                    success = TryExecuteCMD(command.Command, out output, out error);
                    break;
            }
            Thread.Sleep(200);
        }

        private bool TryExecutePowerShell(string command, out string output, out string error)
        {
            output = "";
            error = "";
            try
            {
                var processInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-Command \"{command}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    Verb = "runas"
                };
                using var process = Process.Start(processInfo);
                if (process == null) return false;
                output = process.StandardOutput.ReadToEnd();
                error = process.StandardError.ReadToEnd();
                process.WaitForExit(20000);
                return process.ExitCode == 0 && string.IsNullOrEmpty(error);
            }
            catch
            {
                return false;
            }
        }

        private bool TryExecuteCMD(string command, out string output, out string error)
        {
            output = "";
            error = "";
            try
            {
                var processInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/C \"{command}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    Verb = "runas"
                };
                using var process = Process.Start(processInfo);
                if (process == null) return false;
                output = process.StandardOutput.ReadToEnd();
                error = process.StandardError.ReadToEnd();
                process.WaitForExit(20000);
                return process.ExitCode == 0 && string.IsNullOrEmpty(error);
            }
            catch
            {
                return false;
            }
        }

        private void RepairWorker_ProgressChanged(object sender, ProgressChangedEventArgs e) =>
            SafeUpdateProgress(e.ProgressPercentage, e.UserState?.ToString() ?? "Processing...");

        private void RepairWorker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            SafeEnableStartButton(true);
            if (e.Error != null)
            {
                SafeUpdateUI($"修复错误: {e.Error.Message}", 0);
            }
            else if (e.Cancelled)
            {
                SafeUpdateUI("修复过程已被取消。", 0);
            }
            else
            {
                SafeUpdateUI("自动修复完成！建议重启计算机以使修复生效。", 100);
                if (InvokeRequired)
                    Invoke(() => MessageBox.Show("自动修复完成！建议重启计算机以使修复生效。", "修复完成",
                        MessageBoxButtons.OK, MessageBoxIcon.Information));
                else
                    MessageBox.Show("自动修复完成！建议重启计算机以使修复生效。", "修复完成",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        #endregion

        #region 其他UI事件
        private void 手动修复按钮_Click(object sender, EventArgs e)
        {
            using var inputForm = new Form()
            {
                Text = "手动修复",
                Size = new System.Drawing.Size(400, 200),
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                MaximizeBox = false,
                MinimizeBox = false
            };
            var lbl = new Label() { Text = "请输入蓝屏错误代码:", Left = 20, Top = 20, Width = 350 };
            var txtErrorCode = new TextBox() { Left = 20, Top = 50, Width = 350 };
            var btnOk = new Button() { Text = "确定", Left = 240, Top = 90, Width = 60 };
            var btnCancel = new Button() { Text = "取消", Left = 310, Top = 90, Width = 60 };
            btnOk.Click += (_, _) => inputForm.DialogResult = DialogResult.OK;
            btnCancel.Click += (_, _) => inputForm.DialogResult = DialogResult.Cancel;
            inputForm.Controls.AddRange(new Control[] { lbl, txtErrorCode, btnOk, btnCancel });
            inputForm.AcceptButton = btnOk;
            inputForm.CancelButton = btnCancel;
            if (inputForm.ShowDialog() == DialogResult.OK && !string.IsNullOrEmpty(txtErrorCode.Text))
            {
                string errorCode = txtErrorCode.Text.Trim();
                StartAutoRepair(errorCode);
            }
        }

        private void 配置管理按钮_Click(object sender, EventArgs e)
        {
            var configForm = new ConfigManagementForm(_configManager, Logger);
            configForm.ShowDialog();
        }

        private void 检查更新按钮_Click(object sender, EventArgs e)
        {
            var result = _configManager.CheckForUpdates();
            if (result.Success)
            {
                if (result.HasUpdate)
                {
                    var updateResult = MessageBox.Show(
                        $"发现新版本: {result.NewVersion}\n更新内容:\n{result.ChangeLog}\n是否立即更新？\n配置源: {REMOTE_CONFIG_URL}",
                        "发现更新", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                    if (updateResult == DialogResult.Yes)
                    {
                        if (_configManager.UpdateConfig())
                        {
                            DisplayConfigInfo();
                            MessageBox.Show("配置更新成功！", "更新完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }
                        else
                        {
                            MessageBox.Show("配置更新失败，请检查网络连接。", "更新失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                    }
                }
                else
                {
                    MessageBox.Show($"当前已是最新版本！\n配置源: {REMOTE_CONFIG_URL}", "检查更新",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            else
            {
                MessageBox.Show($"检查更新失败: {result.ErrorMessage}\n配置源: {REMOTE_CONFIG_URL}", "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        #endregion
    }

    #region 配置管理
    public class ConfigManager
    {
        private readonly Logger _logger;
        private readonly string _remoteConfigUrl;
        private readonly string _userConfigPath;
        private RepairConfig _builtInConfig;
        private RepairConfig _userConfig;
        private RepairConfig _remoteConfig;
        private DateTime _lastRemoteFetch = DateTime.MinValue;
        private readonly object _remoteLock = new object();
        private readonly HttpClient _httpClient;
        private Dictionary<string, RepairStrategy> _strategyMap = new Dictionary<string, RepairStrategy>(StringComparer.OrdinalIgnoreCase);
        private const int REMOTE_CACHE_MINUTES = 5;

        public ConfigManager(Logger logger, string remoteConfigUrl, string userConfigPath)
        {
            _logger = logger;
            _remoteConfigUrl = remoteConfigUrl;
            _userConfigPath = userConfigPath;
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "BlueScreenDetector/1.0");
        }

        public void Initialize()
        {
            _builtInConfig = CreateBuiltInConfig();
            LoadUserConfig();
            RefreshConfig();
        }

        private void LoadUserConfig()
        {
            try
            {
                if (File.Exists(_userConfigPath))
                {
                    var json = File.ReadAllText(_userConfigPath);
                    _userConfig = JsonSerializer.Deserialize<RepairConfig>(json);
                }
                else
                {
                    _userConfig = new RepairConfig
                    {
                        Version = "1.0.0-user",
                        LastUpdated = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                        RepairStrategies = new List<RepairStrategy>()
                    };
                    SaveUserConfig();
                }
            }
            catch
            {
                _userConfig = new RepairConfig
                {
                    Version = "1.0.0-user",
                    LastUpdated = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    RepairStrategies = new List<RepairStrategy>()
                };
            }
        }

        private RepairConfig CreateBuiltInConfig() => new RepairConfig
        {
            Version = "1.0.0-built-in",
            LastUpdated = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            ErrorPatterns = new List<ErrorPattern>
            {
                new ErrorPattern { Pattern = "CRITICAL_PROCESS_DIED", Type = "SystemProcess", Description = "关键进程终止" },
                new ErrorPattern { Pattern = "SYSTEM_THREAD_EXCEPTION_NOT_HANDLED", Type = "Driver", Description = "系统线程异常" },
                new ErrorPattern { Pattern = "IRQL_NOT_LESS_OR_EQUAL", Type = "Driver", Description = "中断请求级别错误" },
                new ErrorPattern { Pattern = "PAGE_FAULT_IN_NONPAGED_AREA", Type = "Memory", Description = "内存页面错误" },
                new ErrorPattern { Pattern = "KERNEL_SECURITY_CHECK_FAILURE", Type = "Security", Description = "内核安全检查失败" },
                new ErrorPattern { Pattern = "MEMORY_MANAGEMENT", Type = "Memory", Description = "内存管理错误" },
                new ErrorPattern { Pattern = "DRIVER_IRQL_NOT_LESS_OR_EQUAL", Type = "Driver", Description = "驱动程序IRQL错误" },
                new ErrorPattern { Pattern = "SYSTEM_SERVICE_EXCEPTION", Type = "SystemService", Description = "系统服务异常" },
                new ErrorPattern { Pattern = "0x", Type = "Hexadecimal", Description = "十六进制错误代码" }
            },
            RepairStrategies = new List<RepairStrategy>
            {
                new RepairStrategy {
                    ErrorCode = "CRITICAL_PROCESS_DIED",
                    Description = "关键系统进程意外终止",
                    UserMessage = "系统关键进程异常终止，可能由于系统文件损坏或软件冲突",
                    RepairCommands = new List<RepairCommand> {
                        new RepairCommand { Type = "PowerShell", Command = "sfc /scannow", Description = "扫描并修复系统文件" },
                        new RepairCommand { Type = "PowerShell", Command = "DISM /Online /Cleanup-Image /RestoreHealth", Description = "修复Windows映像" }
                    }
                },
                new RepairStrategy {
                    ErrorCode = "SYSTEM_THREAD_EXCEPTION_NOT_HANDLED",
                    Description = "系统线程异常未处理",
                    UserMessage = "驱动程序或系统组件引发未处理的异常",
                    RepairCommands = new List<RepairCommand> {
                        new RepairCommand { Type = "CMD", Command = "chkdsk C: /f /r", Description = "检查并修复磁盘错误" }
                    }
                },
                new RepairStrategy {
                    ErrorCode = "GENERAL_SYSTEM_REPAIR",
                    Description = "通用系统修复",
                    UserMessage = "执行常规系统维护和修复",
                    RepairCommands = new List<RepairCommand> {
                        new RepairCommand { Type = "PowerShell", Command = "sfc /scannow", Description = "扫描并修复受保护的系统文件" },
                        new RepairCommand { Type = "PowerShell", Command = "DISM /Online /Cleanup-Image /RestoreHealth", Description = "修复Windows系统映像" }
                    }
                }
            }
        };

        private async Task<RepairConfig> FetchRemoteConfigAsync()
        {
            try
            {
                var response = await _httpClient.GetStringAsync(_remoteConfigUrl);
                var config = JsonSerializer.Deserialize<RepairConfig>(response);
                if (config != null && !string.IsNullOrEmpty(config.Version))
                {
                    config.LastUpdated = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    return config;
                }
            }
            catch { }
            return null;
        }

        private RepairConfig GetRemoteConfig()
        {
            lock (_remoteLock)
            {
                if (_remoteConfig != null && (DateTime.Now - _lastRemoteFetch).TotalMinutes < REMOTE_CACHE_MINUTES)
                    return _remoteConfig;

                try
                {
                    var task = FetchRemoteConfigAsync();
                    if (task.Wait(5000))
                    {
                        _remoteConfig = task.Result;
                        _lastRemoteFetch = DateTime.Now;
                        return _remoteConfig;
                    }
                }
                catch { }
                return null;
            }
        }

        private void RefreshConfig()
        {
            _remoteConfig = null;
            BuildStrategyMap();
        }

        private void BuildStrategyMap()
        {
            _strategyMap.Clear();
            var remoteConfig = GetRemoteConfig();
            if (remoteConfig != null && remoteConfig.RepairStrategies != null)
            {
                foreach (var strategy in remoteConfig.RepairStrategies)
                {
                    if (!string.IsNullOrEmpty(strategy.ErrorCode) && !_strategyMap.ContainsKey(strategy.ErrorCode))
                    {
                        _strategyMap[strategy.ErrorCode] = strategy;
                    }
                }
            }
            if (_userConfig != null && _userConfig.RepairStrategies != null)
            {
                foreach (var strategy in _userConfig.RepairStrategies)
                {
                    if (!string.IsNullOrEmpty(strategy.ErrorCode) && !_strategyMap.ContainsKey(strategy.ErrorCode))
                    {
                        _strategyMap[strategy.ErrorCode] = strategy;
                    }
                }
            }
            if (_builtInConfig != null && _builtInConfig.RepairStrategies != null)
            {
                foreach (var strategy in _builtInConfig.RepairStrategies)
                {
                    if (!string.IsNullOrEmpty(strategy.ErrorCode) && !_strategyMap.ContainsKey(strategy.ErrorCode))
                    {
                        _strategyMap[strategy.ErrorCode] = strategy;
                    }
                }
            }
        }

        public RepairConfig GetConfig()
        {
            var remoteConfig = GetRemoteConfig();
            if (remoteConfig != null)
                return remoteConfig;

            if (_userConfig != null)
                return _userConfig;

            return _builtInConfig;
        }

        public RepairStrategy GetRepairStrategy(string errorCode)
        {
            if (string.IsNullOrEmpty(errorCode)) return null;
            BuildStrategyMap();
            return _strategyMap.GetValueOrDefault(errorCode.ToUpperInvariant());
        }

        public void AddUserStrategy(RepairStrategy strategy)
        {
            if (_userConfig.RepairStrategies == null)
                _userConfig.RepairStrategies = new List<RepairStrategy>();

            var existingIndex = _userConfig.RepairStrategies.FindIndex(s => s.ErrorCode.Equals(strategy.ErrorCode, StringComparison.OrdinalIgnoreCase));
            if (existingIndex >= 0)
                _userConfig.RepairStrategies[existingIndex] = strategy;
            else
                _userConfig.RepairStrategies.Add(strategy);

            _userConfig.LastUpdated = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            SaveUserConfig();
            BuildStrategyMap();
        }

        public void RemoveUserStrategy(string errorCode)
        {
            if (_userConfig.RepairStrategies == null) return;
            _userConfig.RepairStrategies.RemoveAll(s => s.ErrorCode.Equals(errorCode, StringComparison.OrdinalIgnoreCase));
            _userConfig.LastUpdated = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            SaveUserConfig();
            BuildStrategyMap();
        }

        private void SaveUserConfig()
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(_userConfig, options);
                File.WriteAllText(_userConfigPath, json);
            }
            catch { }
        }

        public UpdateCheckResult CheckForUpdates()
        {
            try
            {
                var remoteConfig = GetRemoteConfig();
                if (remoteConfig == null)
                {
                    return new UpdateCheckResult
                    {
                        Success = false,
                        ErrorMessage = "无法连接到远程服务器"
                    };
                }
                var currentVersion = new Version((_userConfig ?? _builtInConfig).Version.Replace("-user", "").Replace("-built-in", ""));
                var remoteVersion = new Version(remoteConfig.Version.Replace("-user", "").Replace("-built-in", ""));
                bool hasUpdate = remoteVersion > currentVersion;
                return new UpdateCheckResult
                {
                    Success = true,
                    HasUpdate = hasUpdate,
                    CurrentVersion = currentVersion.ToString(),
                    NewVersion = remoteVersion.ToString(),
                    ChangeLog = remoteConfig.ChangeLog ?? "无更新日志",
                    RemoteConfig = remoteConfig
                };
            }
            catch
            {
                return new UpdateCheckResult
                {
                    Success = false,
                    ErrorMessage = "检查更新失败"
                };
            }
        }

        public bool UpdateConfig()
        {
            try
            {
                var result = CheckForUpdates();
                if (result.Success && result.HasUpdate && result.RemoteConfig != null)
                {
                    _remoteConfig = result.RemoteConfig;
                    _lastRemoteFetch = DateTime.Now;
                    BuildStrategyMap();
                    return true;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }
    }

    public class ConfigManagementForm : Form
    {
        private readonly ConfigManager _configManager;
        private readonly Logger _logger;
        private TextBox _customStrategyTextBox;
        private Button _addButton;
        private Button _removeButton;
        private ListBox _strategyListBox;

        public ConfigManagementForm(ConfigManager configManager, Logger logger)
        {
            _configManager = configManager;
            _logger = logger;
            InitializeComponents();
            LoadUserStrategies();
        }

        private void InitializeComponents()
        {
            Text = "配置管理";
            Size = new System.Drawing.Size(600, 400);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;

            var label = new Label
            {
                Text = "自定义修复策略 (JSON格式):",
                Left = 20,
                Top = 20,
                Width = 560
            };

            _customStrategyTextBox = new TextBox
            {
                Left = 20,
                Top = 45,
                Width = 560,
                Height = 100,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical
            };

            _addButton = new Button
            {
                Text = "添加策略",
                Left = 20,
                Top = 155,
                Width = 100
            };
            _addButton.Click += AddButton_Click;

            _removeButton = new Button
            {
                Text = "移除选中",
                Left = 130,
                Top = 155,
                Width = 100
            };
            _removeButton.Click += RemoveButton_Click;

            var listLabel = new Label
            {
                Text = "已添加的策略:",
                Left = 20,
                Top = 190,
                Width = 560
            };

            _strategyListBox = new ListBox
            {
                Left = 20,
                Top = 215,
                Width = 560,
                Height = 120,
                SelectionMode = SelectionMode.MultiExtended
            };

            Controls.AddRange(new Control[] { label, _customStrategyTextBox, _addButton, _removeButton, listLabel, _strategyListBox });
        }

        private void LoadUserStrategies()
        {
            _strategyListBox.Items.Clear();
            var userConfig = _configManager.GetConfig();
            if (userConfig?.RepairStrategies != null)
            {
                foreach (var strategy in userConfig.RepairStrategies)
                {
                    _strategyListBox.Items.Add($"{strategy.ErrorCode}: {strategy.Description}");
                }
            }
        }

        private void AddButton_Click(object sender, EventArgs e)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_customStrategyTextBox.Text))
                {
                    MessageBox.Show("请输入策略JSON", "输入错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                var newStrategy = JsonSerializer.Deserialize<RepairStrategy>(_customStrategyTextBox.Text);
                if (newStrategy == null || string.IsNullOrEmpty(newStrategy.ErrorCode))
                {
                    MessageBox.Show("无效的策略格式", "验证错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                _configManager.AddUserStrategy(newStrategy);
                LoadUserStrategies();
                _customStrategyTextBox.Clear();
                MessageBox.Show("策略添加成功！", "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch
            {
                MessageBox.Show("添加策略失败", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void RemoveButton_Click(object sender, EventArgs e)
        {
            if (_strategyListBox.SelectedIndex == -1)
            {
                MessageBox.Show("请先选择要移除的策略", "选择错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var selectedIndex = _strategyListBox.SelectedIndex;
            var userConfig = _configManager.GetConfig();
            if (userConfig?.RepairStrategies != null && selectedIndex < userConfig.RepairStrategies.Count)
            {
                var removedStrategy = userConfig.RepairStrategies[selectedIndex];
                _configManager.RemoveUserStrategy(removedStrategy.ErrorCode);
                LoadUserStrategies();
                MessageBox.Show("策略移除成功！", "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
    }
    #endregion

    #region 日志系统
    public class Logger
    {
        private readonly string _logFilePath;
        private readonly ConcurrentQueue<string> _logQueue = new ConcurrentQueue<string>();
        private readonly object _fileLock = new object();
        private readonly Timer _flushTimer;
        private long _logSize;
        private const long MAX_LOG_SIZE = 10 * 1024 * 1024;
        private bool _isFlushing;
        private static readonly Lazy<Logger> _instance = new Lazy<Logger>(() => new Logger());

        public static Logger Instance => _instance.Value;

        private Logger()
        {
            var appDirectory = AppDomain.CurrentDomain.BaseDirectory;
            var logDirectory = Path.Combine(appDirectory, "logs");
            Directory.CreateDirectory(logDirectory);
            _logFilePath = Path.Combine(logDirectory, $"diagnostic_{DateTime.Now:yyyyMMdd_HHmmss}.log");
            _flushTimer = new Timer(FlushLogs, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
            AppDomain.CurrentDomain.ProcessExit += (s, e) => FlushAllLogs();
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                Error("Unhandled exception");
                FlushAllLogs();
            };
        }

        public static void LogSystemInfo()
        {
            Instance._logSize = 0;
            var osVersion = Environment.OSVersion;
            var dotnetVersion = Environment.Version;
            var processorCount = Environment.ProcessorCount;
            var totalMemory = GC.GetTotalMemory(false) / 1024 / 1024;
            var machineName = Environment.MachineName;
            var userName = Environment.UserName;

            Instance.Info($"System Information:");
            Instance.Info($"OS: {osVersion}");
            Instance.Info($".NET Runtime: {dotnetVersion}");
            Instance.Info($"Processor Count: {processorCount}");
            Instance.Info($"Current Memory Usage: {totalMemory} MB");
            Instance.Info($"Machine Name: {machineName}");
            Instance.Info($"User: {userName}");
            Instance.Info($"Start Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
        }

        private void EnsureLogDirectoryExists()
        {
            var directory = Path.GetDirectoryName(_logFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);
        }

        private string FormatLogEntry(string level, string message) =>
            $"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")} [{level}] {message}{Environment.NewLine}";

        private void EnqueueLog(string level, string message)
        {
            var entry = FormatLogEntry(level, message);
            _logQueue.Enqueue(entry);
            Interlocked.Add(ref _logSize, entry.Length);
        }

        public void Info(string message) => EnqueueLog("INFO", message);
        public void Warn(string message) => EnqueueLog("WARN", message);
        public void Error(string message) => EnqueueLog("ERROR", message);

        private void FlushLogs(object state = null)
        {
            if (_isFlushing) return;
            _isFlushing = true;
            try
            {
                if (_logQueue.IsEmpty) return;
                EnsureLogDirectoryExists();
                var entriesToWrite = new List<string>();
                while (_logQueue.TryDequeue(out var entry))
                {
                    entriesToWrite.Add(entry);
                }
                if (entriesToWrite.Count == 0) return;
                lock (_fileLock)
                {
                    File.AppendAllLines(_logFilePath, entriesToWrite, Encoding.UTF8);
                }
            }
            finally
            {
                _isFlushing = false;
            }
        }

        public void FlushAllLogs()
        {
            while (!_logQueue.IsEmpty)
                FlushLogs();
        }
    }
    #endregion

    #region 数据模型
    public class RepairConfig
    {
        public string Version { get; set; }
        public string LastUpdated { get; set; }
        public string ChangeLog { get; set; }
        public List<ErrorPattern> ErrorPatterns { get; set; }
        public List<RepairStrategy> RepairStrategies { get; set; }
    }

    public class ErrorPattern
    {
        public string Pattern { get; set; }
        public string Type { get; set; }
        public string Description { get; set; }
    }

    public class RepairStrategy
    {
        public string ErrorCode { get; set; }
        public string Description { get; set; }
        public string UserMessage { get; set; }
        public List<RepairCommand> RepairCommands { get; set; }
    }

    public class RepairCommand
    {
        public string Type { get; set; }
        public string Command { get; set; }
        public string Description { get; set; }
    }

    public class UpdateCheckResult
    {
        public bool Success { get; set; }
        public bool HasUpdate { get; set; }
        public string CurrentVersion { get; set; }
        public string NewVersion { get; set; }
        public string ChangeLog { get; set; }
        public string ErrorMessage { get; set; }
        public RepairConfig RemoteConfig { get; set; }
    }

    public class ShutdownEvent
    {
        public DateTime Timestamp { get; set; }
        public ShutdownType Type { get; set; }
        public string Description { get; set; }
        public string EventId { get; set; }
        public string Source { get; set; }
    }

    public class BlueScreenInfo
    {
        public string StopCode { get; set; }
        public DateTime CrashTime { get; set; }
        public string DumpFilePath { get; set; }
        public string Description { get; set; }
    }

    public enum ShutdownType
    {
        Normal,
        BlueScreen,
        PowerLoss,
        UpdateRestart,
        ForceShutdown,
        Unknown
    }
    #endregion
}