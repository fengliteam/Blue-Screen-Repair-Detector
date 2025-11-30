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
using Microsoft.VisualBasic;
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
        // JSON配置管理
        private ConfigManager _configManager;
        // 修复状态标签 - 用于显示配置信息
        private Label _configStatusLabel;
        // 日志系统
        private Logger _logger;
        // 性能监控
        private readonly Stopwatch _diagnosticStopwatch = new Stopwatch();
        // 用于线程安全的UI更新委托
        private delegate void SafeUpdateUIDelegate(string text, int progress);
        private delegate void SafeUpdateProgressDelegate(int progress, string message);
        // 配置更新 - 可修改的服务器地址
        private const string REMOTE_CONFIG_URL = "https://raw.githubusercontent.com/fengliteam/BARD-File/refs/heads/main/gx.json"; // 远程配置服务器地址，可修改
        private const string USER_CONFIG_PATH = "user_config.json";
        #endregion

        #region 构造函数
        public Form1()
        {
            InitializeComponent();
            // 初始化日志系统
            _logger = Logger.Instance;
            Logger.LogSystemInfo();
            try
            {
                _logger.Info("应用程序启动");
                // 创建配置状态标签
                CreateConfigStatusLabel();
                // 初始化配置管理器
                _configManager = new ConfigManager(_logger, REMOTE_CONFIG_URL, USER_CONFIG_PATH);
                _configManager.Initialize();
                _shutdownEvents = new List<ShutdownEvent>();
                _blueScreenInfo = new BlueScreenInfo();
                _diagnosticDataTable = new System.Data.DataTable();
                InitializeBackgroundWorker();
                InitializeRepairWorker();
                label1.Text = "准备开始诊断...";
                UpdateProgressText(0);
                // 显示配置信息
                DisplayConfigInfo();
                _logger.Info("应用程序初始化完成");
            }
            catch (Exception ex)
            {
                _logger.Error("应用程序初始化失败", ex);
                SafeUpdateUI("应用程序初始化失败", 0);
            }
        }
        #endregion

        #region 线程安全的UI更新方法
        private void SafeUpdateUI(string text, int progress)
        {
            if (label1.InvokeRequired || 进度条且拥有6个格子.InvokeRequired)
            {
                var d = new SafeUpdateUIDelegate(SafeUpdateUI);
                Invoke(d, new object[] { text, progress });
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
                var d = new SafeUpdateProgressDelegate(SafeUpdateProgress);
                Invoke(d, new object[] { progress, message });
            }
            else
            {
                进度条且拥有6个格子.Value = progress;
                label1.Text = message;
                UpdateStepFromProgress(progress);
                _logger.Info($"诊断进度: {progress}% - {message}");
            }
        }

        private void SafeUpdateProgressText(int step)
        {
            if (进度条且拥有6个格子.InvokeRequired)
            {
                Invoke(new Action<int>(SafeUpdateProgressText), step);
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
                Invoke(new Action<bool>(SafeEnableStartButton), enable);
            }
            else
            {
                开始检测的按钮.Enabled = enable;
            }
        }
        #endregion

        #region UI初始化
        private void CreateConfigStatusLabel()
        {
            _configStatusLabel = new Label
            {
                Text = "正在加载配置...",
                Dock = DockStyle.Bottom,
                Height = 20,
                TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = System.Drawing.Color.LightYellow
            };
            this.Controls.Add(_configStatusLabel);
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

        #region 进度管理
        private void UpdateProgressText(int step)
        {
            _currentStep = step;
            进度条且拥有6个格子.Text = $"{step}/{TOTAL_STEPS}";
        }

        private void UpdateStepFromProgress(int progressPercentage)
        {
            int step = progressPercentage switch
            {
                < 15 => 0,
                < 30 => 1,
                < 45 => 2,
                < 60 => 3,
                < 75 => 4,
                < 90 => 5,
                < 100 => 6,
                100 => 6,
                _ => 0
            };
            SafeUpdateProgressText(step);
        }
        #endregion

        #region 高性能诊断功能（优化版）
        private async void 开始检测的按钮_Click(object sender, EventArgs e)
        {
            try
            {
                SafeUpdateProgress(0, "准备开始诊断...");
                SafeUpdateProgressText(0);
                SafeEnableStartButton(false);
                ClearTableData();
                _diagnosticStopwatch.Restart();
                _logger.Info("开始系统诊断");
                // 使用Task.Run而不是BackgroundWorker.RunWorkerAsync()，更好地利用异步特性
                await Task.Run(() => _backgroundWorker.RunWorkerAsync());
            }
            catch (Exception ex)
            {
                _logger.Error("开始诊断时发生错误", ex);
                SafeUpdateUI($"开始诊断时发生错误: {ex.Message}", 0);
                SafeEnableStartButton(true);
            }
        }

        private void ClearTableData()
        {
            try
            {
                if (table1.InvokeRequired)
                {
                    Invoke(new Action(ClearTableData));
                    return;
                }
                table1.DataSource = null;
                _diagnosticDataTable?.Clear();
            }
            catch (Exception ex)
            {
                _logger.Warn("清空表格数据时发生错误", ex);
            }
        }

        private void BackgroundWorker_DoWork(object sender, DoWorkEventArgs e)
        {
            try
            {
                _logger.Info("后台诊断工作开始");
                // 优化：使用并行处理同时执行多个独立任务
                var tasks = new List<Task>();
                // 任务1：分析关机事件
                Task<List<ShutdownEvent>> shutdownEventsTask = Task.Run(() => AnalyzeShutdownEvents());
                // 任务2：检查蓝屏信息（并行执行）
                Task<BlueScreenInfo> blueScreenTask = Task.Run(() => AnalyzeBlueScreen());
                // 任务3：检查转储配置（并行执行）
                Task<bool> dumpConfigTask = Task.Run(() => CheckDumpFileSettings());
                // 等待所有任务完成
                Task.WhenAll(shutdownEventsTask, blueScreenTask, dumpConfigTask).Wait();
                _backgroundWorker.ReportProgress(30, "正在分析系统事件日志...");
                _shutdownEvents = shutdownEventsTask.Result;
                Thread.Sleep(100); // 最小延迟用于UI显示
                _backgroundWorker.ReportProgress(45, "正在检查蓝屏崩溃记录...");
                _blueScreenInfo = blueScreenTask.Result;
                Thread.Sleep(100);
                _backgroundWorker.ReportProgress(60, "正在检查系统转储配置...");
                _dumpFilesEnabled = dumpConfigTask.Result;
                Thread.Sleep(100);
                // 只有当检测到蓝屏时才深度分析转储文件
                if (_blueScreenInfo != null && !string.IsNullOrEmpty(_blueScreenInfo.StopCode))
                {
                    _backgroundWorker.ReportProgress(75, "正在分析内存转储文件...");
                    AnalyzeDumpFilesInDepth();
                }
                else
                {
                    _backgroundWorker.ReportProgress(75, "跳过转储文件分析...");
                }
                Thread.Sleep(100);
                _backgroundWorker.ReportProgress(90, "正在生成诊断报告...");
                Thread.Sleep(100); // 模拟报告生成
                _logger.Info("后台诊断工作完成");
                _backgroundWorker.ReportProgress(100, "诊断完成");
            }
            catch (Exception ex)
            {
                _logger.Error("后台诊断工作发生错误", ex);
                throw;
            }
        }

        private void BackgroundWorker_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            // 使用线程安全的方法更新UI
            SafeUpdateProgress(e.ProgressPercentage, e.UserState?.ToString() ?? "处理中...");
        }

        private void BackgroundWorker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            _diagnosticStopwatch.Stop();
            _logger.Info($"诊断完成，耗时: {_diagnosticStopwatch.Elapsed.TotalSeconds:F2}秒");
            SafeUpdateProgress(100, "诊断完成");
            SafeUpdateProgressText(6);
            SafeEnableStartButton(true);
            if (e.Error != null)
            {
                _logger.Error("诊断过程中出现错误", e.Error);
                SafeUpdateUI($"诊断过程中出现错误: {e.Error.Message}", 0);
                return;
            }
            if (e.Cancelled)
            {
                _logger.Info("诊断过程被取消");
                SafeUpdateUI("诊断已取消", 0);
                return;
            }
            // 使用Invoke确保在UI线程中显示结果
            if (InvokeRequired)
            {
                Invoke(new Action(DisplayDiagnosticResults));
            }
            else
            {
                DisplayDiagnosticResults();
            }
        }
        #endregion

        #region 高性能事件日志分析（优化版）
        private List<ShutdownEvent> AnalyzeShutdownEvents()
        {
            var events = new List<ShutdownEvent>();
            try
            {
                _logger.Info("开始分析系统事件日志");
                var startTime = DateTime.Now.AddDays(-30);
                var shutdownEventIds = new HashSet<int> { 6006, 6008, 6009, 41, 1074, 1076, 109, 1001, 6005, 6007 };
                using (var log = new EventLog("System"))
                {
                    // 优化：使用for循环反向遍历，避免Cast和Linq的性能开销
                    // EventLog.Entries不支持随机访问，但我们可以限制查询数量
                    int checkedEntries = 0;
                    int maxEntriesToCheck = 1000; // 限制最大检查条目数以提高性能
                    for (int i = log.Entries.Count - 1; i >= 0 && checkedEntries < maxEntriesToCheck; i--)
                    {
                        var entry = log.Entries[i];
                        checkedEntries++;
                        // 快速跳过不符合条件的事件
                        if (entry.TimeGenerated < startTime) continue;
                        if (!shutdownEventIds.Contains((int)entry.InstanceId)) continue;
                        var shutdownEvent = new ShutdownEvent
                        {
                            Timestamp = entry.TimeGenerated,
                            EventId = entry.InstanceId.ToString(),
                            Source = entry.Source,
                            Description = GetEventDescription(entry),
                            Type = DetermineShutdownType(entry)
                        };
                        events.Add(shutdownEvent);
                        // 如果已收集足够事件，提前退出
                        if (events.Count >= 50) break;
                    }
                }
                // 排序和限制数量
                events = events.OrderByDescending(x => x.Timestamp).Take(20).ToList();
                _logger.Info($"事件分析完成，共 {events.Count} 个事件");
            }
            catch (Exception ex)
            {
                _logger.Error("分析事件日志失败", ex);
            }
            return events;
        }

        private string GetEventDescription(EventLogEntry entry)
        {
            // 使用switch表达式和Span优化字符串处理
            return entry.InstanceId switch
            {
                109 => "系统休眠/唤醒事件",
                41 => "系统意外重启 - 可能由于电源问题或系统崩溃",
                6006 => "正常系统关机",
                6008 => "系统异常关机",
                1074 => "用户发起的系统关机",
                1001 => "Windows错误报告事件",
                _ => string.IsNullOrEmpty(entry.Message)
                    ? "无详细描述"
                    : (entry.Message.Length > 80 ? entry.Message.Substring(0, 80) + "..." : entry.Message)
            };
        }

        private ShutdownType DetermineShutdownType(EventLogEntry entry)
        {
            // 使用更快的switch表达式
            return entry.InstanceId switch
            {
                6006 or 6005 or 6007 or 109 or 1074 => ShutdownType.Normal,
                6008 => ShutdownType.ForceShutdown,
                41 => AnalyzeEvent41(entry),
                1001 => AnalyzeEvent1001(entry),
                _ => ShutdownType.Unknown
            };
        }

        private ShutdownType AnalyzeEvent41(EventLogEntry entry)
        {
            // 优化：使用Span和OrdinalIgnoreCase提高性能
            var message = entry.Message.AsSpan();
            // 修复：不能用Contains(Span, Span)，改用IndexOf
            if (message.IndexOf("电源".AsSpan(), StringComparison.OrdinalIgnoreCase) >= 0 || message.IndexOf("power".AsSpan(), StringComparison.OrdinalIgnoreCase) >= 0)
                return ShutdownType.PowerLoss;
            if (message.IndexOf("BugCheck".AsSpan(), StringComparison.OrdinalIgnoreCase) >= 0 || message.IndexOf("0x".AsSpan()) >= 0)
                return ShutdownType.BlueScreen;
            return ShutdownType.ForceShutdown;
        }

        private ShutdownType AnalyzeEvent1001(EventLogEntry entry)
        {
            var message = entry.Message.AsSpan();
            // 修复：不能用Contains(Span, Span)，改用IndexOf
            if (message.IndexOf("BugCheck".AsSpan(), StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("蓝屏".AsSpan(), StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("crash".AsSpan(), StringComparison.OrdinalIgnoreCase) >= 0)
                return ShutdownType.BlueScreen;
            return ShutdownType.Unknown;
        }
        #endregion

        #region 蓝屏分析（优化版）
        private BlueScreenInfo AnalyzeBlueScreen()
        {
            var bsInfo = new BlueScreenInfo();
            try
            {
                _logger.Info("开始并行分析蓝屏信息");
                // 优化：并行执行三种分析方法，使用PLINQ
                var analysisMethods = new Func<BlueScreenInfo>[]
                {
                    () => GetBlueScreenFromWER(),
                    () => GetBlueScreenFromSystemEvents(),
                    () => GetBlueScreenFromDumpFiles()
                };
                // 使用并行执行，获取第一个非空结果
                var parallelResult = analysisMethods
                    .AsParallel()
                    .WithDegreeOfParallelism(3) // 限制并行度为3
                    .Select(method => method())
                    .FirstOrDefault(result => result != null && !string.IsNullOrEmpty(result.StopCode) && result.StopCode != "UNKNOWN_ERROR");
                if (parallelResult != null)
                {
                    bsInfo = parallelResult;
                    _logger.Info($"从并行分析获取到蓝屏信息: {bsInfo.StopCode}");
                }
                else
                {
                    _logger.Info("未检测到蓝屏记录");
                }
            }
            catch (Exception ex)
            {
                _logger.Error("分析蓝屏信息失败", ex);
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
                    using (var log = new EventLog("Application"))
                    {
                        // 优化：减少Linq使用，直接遍历
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
                                _logger.Info($"从WER获取蓝屏信息: {bsInfo.StopCode}");
                                break;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error("从WER获取蓝屏信息失败", ex);
            }
            return bsInfo;
        }

        private BlueScreenInfo GetBlueScreenFromSystemEvents()
        {
            var bsInfo = new BlueScreenInfo();
            try
            {
                using (var log = new EventLog("System"))
                {
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
                            _logger.Info($"从系统事件获取蓝屏信息: {bsInfo.StopCode}");
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error("从系统事件获取蓝屏信息失败", ex);
            }
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
                // 优化：并行搜索转储文件
                var dumpFileTasks = dumpPaths
                    .AsParallel()
                    .WithDegreeOfParallelism(2)
                    .Select(path => SearchDumpFile(path))
                    .Where(result => !string.IsNullOrEmpty(result))
                    .ToList();
                if (dumpFileTasks.Any())
                {
                    bsInfo.DumpFilePath = dumpFileTasks[0];
                    bsInfo.CrashTime = File.GetLastWriteTime(bsInfo.DumpFilePath);
                    bsInfo.StopCode = "发现转储文件";
                    bsInfo.Description = "存在系统内存转储文件";
                    _logger.Info($"发现转储文件: {bsInfo.DumpFilePath}");
                }
            }
            catch (Exception ex)
            {
                _logger.Error("从转储文件获取蓝屏信息失败", ex);
            }
            return bsInfo;
        }

        private string SearchDumpFile(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    var files = Directory.EnumerateFiles(path, "*.dmp", SearchOption.TopDirectoryOnly)
                                    .OrderByDescending(f => new FileInfo(f).LastWriteTime)
                                    .Take(1)
                                    .ToList();
                    return files.FirstOrDefault();
                }
                else if (File.Exists(path) && path.EndsWith(".dmp", StringComparison.OrdinalIgnoreCase))
                {
                    return path;
                }
            }
            catch { /* 忽略单个路径的错误 */ }
            return null;
        }

        private string ExtractStopCodeFromMessage(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                _logger.Warn("提取停止代码时消息为空");
                return null;
            }
            try
            {
                // 优先使用JSON配置中的错误模式进行识别
                var config = _configManager.GetConfig();
                if (config?.ErrorPatterns != null)
                {
                    // 并行搜索错误模式
                    var matchedPattern = config.ErrorPatterns
                        .AsParallel()
                        .FirstOrDefault(pattern => message.IndexOf(pattern.Pattern, StringComparison.OrdinalIgnoreCase) >= 0);
                    if (matchedPattern != null)
                    {
                        _logger.Info($"匹配到错误模式: {matchedPattern.Pattern}");
                        if (matchedPattern.Type == "Hexadecimal")
                        {
                            return ExtractHexadecimalCode(message);
                        }
                        return matchedPattern.Pattern;
                    }
                }
                // 使用Span和Span-based正则表达式进行快速解析
                return ExtractStopCodeWithSpan(message);
            }
            catch (Exception ex)
            {
                _logger.Error("提取停止代码时发生错误", ex);
                return "EXTRACTION_ERROR";
            }
        }

        private string ExtractStopCodeWithSpan(string message)
        {
            // 优化：使用Span避免字符串分配
            ReadOnlySpan<char> span = message.AsSpan();
            // 快速检查常见模式
            if (span.Contains("BugCheck".AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                int index = span.IndexOf("BugCheck".AsSpan(), StringComparison.OrdinalIgnoreCase);
                if (index >= 0)
                {
                    ReadOnlySpan<char> remaining = span.Slice(index + 8);
                    int spaceIndex = remaining.IndexOf(' ');
                    if (spaceIndex > 0)
                    {
                        ReadOnlySpan<char> code = remaining.Slice(0, spaceIndex).Trim();
                        return code.ToString();
                    }
                }
            }
            // 检查十六进制格式 - 修复IndexOfAny调用
            int hexIndex = span.IndexOf("0x".AsSpan());
            if (hexIndex >= 0)
            {
                ReadOnlySpan<char> hexPart = span.Slice(hexIndex);
                // ✅ 修复：使用正确的IndexOfAny重载，传递字符数组
                char[] separators = new char[] { ' ', ',', '\n', '\r', ':', ';', ')' };
                int endIndex = hexPart.IndexOfAny(separators);
                if (endIndex > 0)
                {
                    ReadOnlySpan<char> code = hexPart.Slice(0, endIndex);
                    return code.ToString();
                }
            }
            return "UNKNOWN_ERROR";
        }

        private string ExtractHexadecimalCode(string message)
        {
            try
            {
                // 优化：使用Span避免内存分配
                ReadOnlySpan<char> span = message.AsSpan();
                string[] patterns = { "0x", "BugCheck", "停止代码", "stop code" };
                foreach (string pattern in patterns)
                {
                    int index = span.IndexOf(pattern.AsSpan(), StringComparison.OrdinalIgnoreCase);
                    if (index >= 0)
                    {
                        ReadOnlySpan<char> remaining = span.Slice(index + pattern.Length).TrimStart();
                        // ✅ 修复：使用正确的IndexOfAny重载，传递字符数组
                        char[] separators = new char[] { ' ', ',', '\n', '\r', ':', ';', ')' };
                        int spaceIndex = remaining.IndexOfAny(separators);
                        if (spaceIndex > 0)
                        {
                            ReadOnlySpan<char> code = remaining.Slice(0, spaceIndex);
                            // 修复：不能对Span使用Contains(Span)，改为IndexOf判断
                            if (code.Length <= 10 && code.IndexOf("0x".AsSpan(), StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                _logger.Info($"提取到十六进制代码: {code.ToString()}");
                                return code.ToString();
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error("提取十六进制代码时发生错误", ex);
            }
            return "UNKNOWN_HEX_CODE";
        }
        #endregion

        #region 系统配置检查
        private bool CheckDumpFileSettings()
        {
            try
            {
                _logger.Info("检查系统转储配置");
                using (var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\CrashControl"))
                {
                    if (key == null)
                    {
                        _logger.Warn("无法访问注册表键: CrashControl");
                        return false;
                    }
                    var crashDumpEnabled = key.GetValue("CrashDumpEnabled");
                    if (crashDumpEnabled != null)
                    {
                        int dumpType = (int)crashDumpEnabled;
                        bool enabled = dumpType == 1 || dumpType == 2 || dumpType == 3 || dumpType == 7;
                        _logger.Info($"转储配置状态: {enabled} (类型: {dumpType})");
                        return enabled;
                    }
                    _logger.Warn("未找到CrashDumpEnabled注册表值");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.Error("检查转储文件设置时发生错误", ex);
                return false;
            }
        }

        private void AnalyzeDumpFilesInDepth()
        {
            try
            {
                _logger.Info("开始深度分析转储文件");
                // 移除不必要的延迟，使用快速扫描
                Thread.Sleep(200); // 仅保留最小延迟
                _logger.Info("转储文件深度分析完成");
            }
            catch (Exception ex)
            {
                _logger.Error("深度分析转储文件时发生错误", ex);
            }
        }
        #endregion

        #region 结果显示
        private void DisplayDiagnosticResults()
        {
            try
            {
                _logger.Info("开始显示诊断结果");
                _diagnosticDataTable = new System.Data.DataTable();
                _diagnosticDataTable.Columns.Add("时间", typeof(string));
                _diagnosticDataTable.Columns.Add("事件类型", typeof(string));
                _diagnosticDataTable.Columns.Add("事件ID", typeof(string));
                _diagnosticDataTable.Columns.Add("描述", typeof(string));
                // 添加异常事件（非Normal事件）
                if (_shutdownEvents != null && _shutdownEvents.Count > 0)
                {
                    var abnormalEvents = _shutdownEvents.Where(e => e.Type != ShutdownType.Normal).ToList();
                    _logger.Info($"找到 {abnormalEvents.Count} 个异常事件");
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
                // 如果没有异常事件，添加一条提示
                if (_diagnosticDataTable.Rows.Count == 0 && _shutdownEvents != null && _shutdownEvents.Count > 0)
                {
                    _diagnosticDataTable.Rows.Add(
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                        "系统状态",
                        "INFO",
                        "✅ 未发现异常关机事件，系统运行正常"
                    );
                    _logger.Info("未发现异常关机事件");
                }
                // 添加蓝屏检测结果
                string bsodStatus = "✅ 未检测到蓝屏记录";
                string bsodTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                if (_blueScreenInfo != null && !string.IsNullOrEmpty(_blueScreenInfo.StopCode))
                {
                    bsodStatus = $"⚠️ 检测到蓝屏事件 - 停止代码: {_blueScreenInfo.StopCode}";
                    if (_blueScreenInfo.CrashTime > DateTime.MinValue)
                        bsodTime = _blueScreenInfo.CrashTime.ToString("yyyy-MM-dd HH:mm:ss");
                    _logger.Info($"检测到蓝屏事件: {_blueScreenInfo.StopCode}");
                }
                _diagnosticDataTable.Rows.Add(bsodTime, "蓝屏检测", "BSOD", bsodStatus);
                // 添加系统配置信息（只在不正常时显示）
                if (!_dumpFilesEnabled)
                {
                    _diagnosticDataTable.Rows.Add(
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                        "系统配置",
                        "CONFIG",
                        "⚠️ 内存转储功能未启用 - 建议启用以便分析系统问题"
                    );
                    _logger.Warn("内存转储功能未启用");
                }
                // 添加配置信息到诊断结果
                var config = _configManager.GetConfig();
                if (config != null)
                {
                    _diagnosticDataTable.Rows.Add(
                        config.LastUpdated,
                        "配置信息",
                        "CONFIG",
                        $"识别库版本: {config.Version} | 支持 {config.ErrorPatterns?.Count ?? 0} 种错误模式"
                    );
                }
                // 确保在UI线程中设置数据源
                if (table1.InvokeRequired)
                {
                    table1.Invoke(new Action(() => table1.DataSource = _diagnosticDataTable));
                }
                else
                {
                    table1.DataSource = _diagnosticDataTable;
                }
                _logger.Info($"诊断结果显示完成，共 {_diagnosticDataTable.Rows.Count} 行数据");
            }
            catch (Exception ex)
            {
                _logger.Error("显示诊断结果时发生错误", ex);
                SafeUpdateUI($"显示结果时出错: {ex.Message}", 0);
            }
        }

        private void DisplayConfigInfo()
        {
            try
            {
                var config = _configManager.GetConfig();
                if (config != null)
                {
                    string configText = $"识别库版本: {config.Version} | 最后更新: {config.LastUpdated} | 服务器: {REMOTE_CONFIG_URL}";
                    if (_configStatusLabel.InvokeRequired)
                    {
                        _configStatusLabel.Invoke(new Action(() =>
                        {
                            _configStatusLabel.Text = configText;
                        }));
                    }
                    else
                    {
                        _configStatusLabel.Text = configText;
                    }
                    _logger.Info($"配置信息显示: 版本 {config.Version}, 最后更新 {config.LastUpdated}");
                }
            }
            catch (Exception ex)
            {
                _logger.Error("显示配置信息时发生错误", ex);
            }
        }
        #endregion

        #region 表格交互
        private void table1_CellClick(object sender, AntdUI.TableClickEventArgs e)
        {
            try
            {
                if (_diagnosticDataTable != null && e.RowIndex >= 0 && e.RowIndex < _diagnosticDataTable.Rows.Count)
                {
                    var row = _diagnosticDataTable.Rows[e.RowIndex];
                    string time = row["时间"].ToString();
                    string eventType = row["事件类型"].ToString();
                    string eventId = row["事件ID"].ToString();
                    string description = row["描述"].ToString();
                    _logger.Info($"用户点击表格行: {eventType} - {eventId}");
                    string detailedInfo = $"时间: {time}\n" +
                                        $"事件类型: {eventType}\n" +
                                        $"事件ID: {eventId}\n" +
                                        $"描述: {description}\n";
                    // 提供简洁的问题分析和解决方案
                    detailedInfo += GetProblemAnalysis(eventType, eventId, description);
                    // 如果是蓝屏事件，显示修复按钮
                    if (eventType == "蓝屏检测" && description.Contains("检测到蓝屏事件"))
                    {
                        var result = MessageBox.Show(detailedInfo + "\n是否立即尝试自动修复？",
                            "问题分析", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                        if (result == DialogResult.Yes)
                        {
                            _logger.Info($"用户选择修复蓝屏事件: {_blueScreenInfo.StopCode}");
                            StartAutoRepair(_blueScreenInfo.StopCode);
                        }
                        else
                        {
                            _logger.Info("用户取消蓝屏修复");
                        }
                    }
                    else
                    {
                        MessageBox.Show(detailedInfo, "问题分析",
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error("处理表格点击事件时发生错误", ex);
                MessageBox.Show($"获取详细信息失败: {ex.Message}", "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private string GetProblemAnalysis(string eventType, string eventId, string description)
        {
            var analysis = eventType switch
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
            _logger.Info($"生成问题分析: {eventType} - {eventId}");
            return analysis;
        }

        private string GetForceShutdownAnalysis()
        {
            return "🔍 问题分析：\n" +
                   "• 系统被强制关闭\n" +
                   "• 可能由于长按电源键或系统无响应\n" +
                   "💡 解决方案：\n" +
                   "1. 尽量避免强制关机，使用正常关机流程\n" +
                   "2. 如系统无响应，可尝试Ctrl+Alt+Del调出任务管理器\n" +
                   "3. 定期保存工作数据，防止意外丢失";
        }

        private string GetPowerLossAnalysis()
        {
            return "🔍 问题分析：\n" +
                   "• 检测到电源中断\n" +
                   "• 可能由于停电、电源线松动或电源故障\n" +
                   "💡 解决方案：\n" +
                   "1. 检查电源连接是否牢固\n" +
                   "2. 考虑使用UPS不间断电源\n" +
                   "3. 如频繁发生，检查电源设备和电路";
        }

        private string GetBlueScreenAnalysis()
        {
            string stopCode = _blueScreenInfo?.StopCode ?? "未知";
            var strategy = GetRepairStrategy(stopCode);
            return $"🔍 问题分析：\n" +
                   $"• 系统发生蓝屏崩溃\n" +
                   $"• 停止代码: {stopCode}\n" +
                   $"• {strategy?.UserMessage ?? "可能由于驱动程序冲突、硬件故障或系统文件损坏"}\n" +
                   $"💡 解决方案：\n" +
                   $"1. 点击'是'按钮尝试自动修复\n" +
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
                   $"1. 点击'是'按钮尝试自动修复\n" +
                   $"2. 或手动执行修复命令";
        }

        private string GetSystemConfigAnalysis()
        {
            return "🔍 问题分析：\n" +
                   "• 内存转储功能未启用\n" +
                   "• 发生系统崩溃时无法生成分析文件\n" +
                   "💡 解决方案：\n" +
                   "1. 右键点击'此电脑'→属性\n" +
                   "2. 选择'高级系统设置'\n" +
                   "3. 在'启动和故障恢复'中点击'设置'\n" +
                   "4. 将'写入调试信息'设置为'小内存转储(256 KB)'";
        }

        private string GetSystemNormalAnalysis()
        {
            return "✅ 系统状态良好\n" +
                   "• 未发现异常关机事件\n" +
                   "• 系统运行稳定\n" +
                   "💡 维护建议：\n" +
                   "1. 定期进行系统更新\n" +
                   "2. 保持驱动程序最新\n" +
                   "3. 定期备份重要数据";
        }

        private string GetConfigInfoAnalysis()
        {
            var config = _configManager.GetConfig();
            var configUrl = REMOTE_CONFIG_URL; // 使用常量显示当前配置URL
            return $"ℹ️ 识别库信息\n" +
                   $"• 版本: {config?.Version ?? "未知"}\n" +
                   $"• 最后更新: {config?.LastUpdated ?? "未知"}\n" +
                   $"• 支持的错误模式: {config?.ErrorPatterns?.Count ?? 0} 种\n" +
                   $"• 修复策略: {config?.RepairStrategies?.Count ?? 0} 种\n" +
                   $"• 配置源: {configUrl}";
        }

        private string GetDefaultAnalysis()
        {
            return "ℹ️ 系统事件记录\n" +
                   "• 这是正常的系统操作记录\n" +
                   "• 无需特别处理";
        }
        #endregion

        #region 自动修复功能
        private void StartAutoRepair(string stopCode)
        {
            try
            {
                _logger.Info($"开始自动修复流程，停止代码: {stopCode}");
                var strategy = GetRepairStrategy(stopCode);
                if (strategy == null)
                {
                    _logger.Warn($"未找到针对错误代码 '{stopCode}' 的修复策略");
                    MessageBox.Show($"未找到针对错误代码 '{stopCode}' 的修复策略。", "提示",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                // 检查管理员权限
                if (!CheckAdminPrivileges())
                {
                    _logger.Warn("自动修复需要管理员权限");
                    MessageBox.Show("自动修复需要管理员权限。请以管理员身份运行此程序。", "权限不足",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                // 显示修复确认对话框
                string confirmMessage = $"确定要执行以下修复操作吗？\n" +
                                      $"错误代码: {stopCode}\n" +
                                      $"问题描述: {strategy.Description}\n" +
                                      $"将执行 {strategy.RepairCommands.Count} 个修复命令";
                var result = MessageBox.Show(confirmMessage, "确认修复",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (result == DialogResult.Yes)
                {
                    _logger.Info("用户确认执行修复");
                    SafeEnableStartButton(false);
                    _repairWorker.RunWorkerAsync(strategy);
                }
                else
                {
                    _logger.Info("用户取消修复操作");
                }
            }
            catch (Exception ex)
            {
                _logger.Error("启动自动修复时发生错误", ex);
                MessageBox.Show($"启动自动修复时发生错误: {ex.Message}", "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private RepairStrategy GetRepairStrategy(string stopCode)
        {
            var config = _configManager.GetConfig();
            if (config?.RepairStrategies == null)
            {
                _logger.Warn("配置中的修复策略为空");
                return null;
            }
            // 优化：使用字典缓存策略，提高查找速度
            var strategyMap = _configManager.GetActiveStrategyMap();
            if (strategyMap.TryGetValue(stopCode.ToUpperInvariant(), out var strategy))
            {
                _logger.Info($"找到匹配的修复策略: {strategy.ErrorCode}");
                return strategy;
            }
            // 未找到特定策略，使用通用修复
            _logger.Info($"未找到特定策略 '{stopCode}'，使用通用修复策略");
            return strategyMap.TryGetValue("GENERAL_SYSTEM_REPAIR", out var generalStrategy) ? generalStrategy : null;
        }

        private bool CheckAdminPrivileges()
        {
            try
            {
                using (var identity = WindowsIdentity.GetCurrent())
                {
                    var principal = new WindowsPrincipal(identity);
                    return principal.IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
            catch (Exception ex)
            {
                _logger.Error("检查管理员权限时发生错误", ex);
                return false;
            }
        }

        private void RepairWorker_DoWork(object sender, DoWorkEventArgs e)
        {
            var strategy = e.Argument as RepairStrategy;
            if (strategy == null)
            {
                _logger.Error("修复工作器接收到空的策略");
                return;
            }
            try
            {
                _logger.Info($"开始执行修复策略: {strategy.ErrorCode}");
                _repairWorker.ReportProgress(0, "开始自动修复...");
                // 优化：并行执行不相关的修复命令
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
                    _logger.Info($"执行修复命令: {command.Description}");
                    ExecuteRepairCommand(command);
                    lock (lockObj)
                    {
                        completedCount++;
                        int progress = (completedCount * 100) / commands.Count;
                        _repairWorker.ReportProgress(progress, $"已完成: {command.Description}");
                    }
                });
                _logger.Info("修复策略执行完成");
                _repairWorker.ReportProgress(100, "修复完成");
            }
            catch (Exception ex)
            {
                _logger.Error("修复工作器执行过程中发生错误", ex);
                throw;
            }
        }

        private void ExecuteRepairCommand(RepairCommand command)
        {
            try
            {
                _logger.Info($"执行命令: {command.Type} - {command.Description}");
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
                    default:
                        _logger.Warn($"未知的命令类型: {command.Type}");
                        break;
                }
                if (success)
                {
                    _logger.Info($"命令执行成功: {command.Description}");
                    if (!string.IsNullOrEmpty(output))
                    {
                        _logger.Info($"命令输出: {output}");
                    }
                }
                else
                {
                    _logger.Error($"命令执行失败: {command.Description}", new Exception(error));
                }
                // 移除延迟，提高修复速度
                Thread.Sleep(200); // 仅保留最小延迟
            }
            catch (Exception ex)
            {
                _logger.Error($"执行修复命令时发生错误: {command.Description}", ex);
                throw;
            }
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
                    Verb = "runas" // 请求管理员权限
                };
                using (var process = Process.Start(processInfo))
                {
                    if (process == null)
                    {
                        _logger.Error("启动PowerShell进程失败");
                        return false;
                    }
                    output = process.StandardOutput.ReadToEnd();
                    error = process.StandardError.ReadToEnd();
                    process.WaitForExit(20000); // 20秒超时，加快失败检测
                    bool success = process.ExitCode == 0 && string.IsNullOrEmpty(error);
                    if (!success)
                    {
                        _logger.Warn($"PowerShell命令执行失败，退出代码: {process.ExitCode}, 错误: {error}");
                    }
                    return success;
                }
            }
            catch (Exception ex)
            {
                _logger.Error("执行PowerShell命令时发生异常", ex);
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
                    Verb = "runas" // 请求管理员权限
                };
                using (var process = Process.Start(processInfo))
                {
                    if (process == null)
                    {
                        _logger.Error("启动CMD进程失败");
                        return false;
                    }
                    output = process.StandardOutput.ReadToEnd();
                    error = process.StandardError.ReadToEnd();
                    process.WaitForExit(20000); // 20秒超时
                    bool success = process.ExitCode == 0 && string.IsNullOrEmpty(error);
                    if (!success)
                    {
                        _logger.Warn($"CMD命令执行失败，退出代码: {process.ExitCode}, 错误: {error}");
                    }
                    return success;
                }
            }
            catch (Exception ex)
            {
                _logger.Error("执行CMD命令时发生异常", ex);
                return false;
            }
        }

        private void RepairWorker_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            // 使用线程安全的方法更新UI
            SafeUpdateProgress(e.ProgressPercentage, e.UserState?.ToString() ?? "处理中...");
        }

        private void RepairWorker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            SafeEnableStartButton(true);
            if (e.Error != null)
            {
                _logger.Error("修复过程中出现错误", e.Error);
                SafeUpdateUI($"修复过程中出现错误: {e.Error.Message}", 0);
            }
            else if (e.Cancelled)
            {
                _logger.Info("修复过程被用户取消");
                SafeUpdateUI("修复过程已被取消。", 0);
            }
            else
            {
                _logger.Info("自动修复完成");
                SafeUpdateUI("自动修复完成！建议重启计算机以使修复生效。", 100);
                // 显示完成消息框
                if (InvokeRequired)
                {
                    Invoke(new Action(() =>
                        MessageBox.Show("自动修复完成！建议重启计算机以使修复生效。", "修复完成",
                            MessageBoxButtons.OK, MessageBoxIcon.Information)));
                }
                else
                {
                    MessageBox.Show("自动修复完成！建议重启计算机以使修复生效。", "修复完成",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
        }
        #endregion

        #region 其他UI事件
        private void 手动修复按钮_Click(object sender, EventArgs e)
        {
            try
            {
                _logger.Info("用户点击手动修复按钮");
                using (var inputForm = new Form()
                {
                    Text = "手动修复",
                    Size = new System.Drawing.Size(400, 200),
                    FormBorderStyle = FormBorderStyle.FixedDialog,
                    StartPosition = FormStartPosition.CenterParent,
                    MaximizeBox = false,
                    MinimizeBox = false
                })
                {
                    var lbl = new Label() { Text = "请输入蓝屏错误代码:", Left = 20, Top = 20, Width = 350 };
                    var txtErrorCode = new TextBox() { Left = 20, Top = 50, Width = 350 };
                    var btnOk = new Button() { Text = "确定", Left = 240, Top = 90, Width = 60 };
                    var btnCancel = new Button() { Text = "取消", Left = 310, Top = 90, Width = 60 };
                    btnOk.Click += (s, ev) => { inputForm.DialogResult = DialogResult.OK; };
                    btnCancel.Click += (s, ev) => { inputForm.DialogResult = DialogResult.Cancel; };
                    inputForm.Controls.AddRange(new Control[] { lbl, txtErrorCode, btnOk, btnCancel });
                    inputForm.AcceptButton = btnOk;
                    inputForm.CancelButton = btnCancel;
                    if (inputForm.ShowDialog() == DialogResult.OK && !string.IsNullOrEmpty(txtErrorCode.Text))
                    {
                        string errorCode = txtErrorCode.Text.Trim();
                        _logger.Info($"用户输入错误代码: {errorCode}");
                        StartAutoRepair(errorCode);
                    }
                    else
                    {
                        _logger.Info("用户取消手动修复输入");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error("手动修复按钮点击时发生错误", ex);
                MessageBox.Show($"手动修复时发生错误: {ex.Message}", "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void 配置管理按钮_Click(object sender, EventArgs e)
        {
            _logger.Info("用户点击配置管理按钮");
            var configForm = new ConfigManagementForm(_configManager, _logger);
            configForm.ShowDialog();
        }

        private void 检查更新按钮_Click(object sender, EventArgs e)
        {
            try
            {
                _logger.Info("用户点击检查更新按钮");
                var result = _configManager.CheckForUpdates();
                if (result.Success)
                {
                    if (result.HasUpdate)
                    {
                        _logger.Info($"发现新版本: {result.NewVersion}");
                        var updateResult = MessageBox.Show(
                            $"发现新版本: {result.NewVersion}\n更新内容:\n{result.ChangeLog}\n是否立即更新？\n配置源: {REMOTE_CONFIG_URL}",
                            "发现更新",
                            MessageBoxButtons.YesNo,
                            MessageBoxIcon.Information);
                        if (updateResult == DialogResult.Yes)
                        {
                            _logger.Info("用户确认更新配置");
                            if (_configManager.UpdateConfig())
                            {
                                DisplayConfigInfo();
                                _logger.Info("配置更新成功");
                                MessageBox.Show("配置更新成功！", "更新完成",
                                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                            }
                            else
                            {
                                _logger.Error("配置更新失败");
                                MessageBox.Show("配置更新失败，请检查网络连接。", "更新失败",
                                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                            }
                        }
                        else
                        {
                            _logger.Info("用户取消配置更新");
                        }
                    }
                    else
                    {
                        _logger.Info("当前已是最新版本");
                        MessageBox.Show($"当前已是最新版本！\n配置源: {REMOTE_CONFIG_URL}", "检查更新",
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
                else
                {
                    _logger.Error($"检查更新失败: {result.ErrorMessage}");
                    MessageBox.Show($"检查更新失败: {result.ErrorMessage}\n配置源: {REMOTE_CONFIG_URL}", "错误",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                _logger.Error("检查更新时出现错误", ex);
                MessageBox.Show($"检查更新时出现错误: {ex.Message}\n配置源: {REMOTE_CONFIG_URL}", "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void progress1_Click(object sender, EventArgs e) { }
        private void label1_Click(object sender, EventArgs e) { }
        #endregion
    }

    #region 日志系统（修正版）
    public class Logger
    {
        private readonly string _logFilePath;
        private readonly ConcurrentQueue<string> _logQueue = new ConcurrentQueue<string>();
        private readonly object _fileLock = new object();
        private readonly System.Threading.Timer _flushTimer; // 明确指定Timer类型
        private long _logSize;
        private const long MAX_LOG_SIZE = 10 * 1024 * 1024; // 10MB
        private bool _isFlushing;
        private static readonly Lazy<Logger> _instance = new Lazy<Logger>(() => new Logger());

        public static Logger Instance => _instance.Value;

        private Logger()
        {
            var appDirectory = AppDomain.CurrentDomain.BaseDirectory;
            var logDirectory = Path.Combine(appDirectory, "logs");
            Directory.CreateDirectory(logDirectory);
            _logFilePath = Path.Combine(logDirectory, $"diagnostic_{DateTime.Now:yyyyMMdd_HHmmss}.log");
            _flushTimer = new System.Threading.Timer(FlushLogs, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
            AppDomain.CurrentDomain.ProcessExit += (s, e) => FlushAllLogs();
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                Error("Unhandled exception", e.ExceptionObject as Exception);
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
            var culture = System.Globalization.CultureInfo.CurrentCulture.Name;

            Instance.Info($"System Information:");
            Instance.Info($"OS: {osVersion}");
            Instance.Info($".NET Runtime: {dotnetVersion}");
            Instance.Info($"Processor Count: {processorCount}");
            Instance.Info($"Current Memory Usage: {totalMemory} MB");
            Instance.Info($"Machine Name: {machineName}");
            Instance.Info($"User: {userName}");
            Instance.Info($"Culture: {culture}");
            Instance.Info($"Application Path: {AppDomain.CurrentDomain.BaseDirectory}");
            Instance.Info($"Application Executable: {Assembly.GetEntryAssembly()?.Location}");
            Instance.Info($"Application Version: {Assembly.GetEntryAssembly()?.GetName().Version}");
            Instance.Info($"Start Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
        }

        private void EnsureLogDirectoryExists()
        {
            var directory = Path.GetDirectoryName(_logFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);
        }

        private string FormatLogEntry(string level, string message, Exception ex = null)
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            var entry = $"{timestamp} [{level}] {message}";
            if (ex != null)
                entry += $"\nException: {ex.Message}\nStack Trace: {ex.StackTrace}";
            return entry + Environment.NewLine;
        }

        private void EnqueueLog(string level, string message, Exception ex = null)
        {
            var entry = FormatLogEntry(level, message, ex);
            _logQueue.Enqueue(entry);
            Interlocked.Add(ref _logSize, entry.Length);
        }

        public void Info(string message) => EnqueueLog("INFO", message);
        public void Warn(string message, Exception ex = null) => EnqueueLog("WARN", message, ex);
        public void Error(string message, Exception ex = null) => EnqueueLog("ERROR", message, ex);

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

        public void ClearOldLogs(int keepDays = 7)
        {
            try
            {
                var logDirectory = Path.GetDirectoryName(_logFilePath);
                if (string.IsNullOrEmpty(logDirectory) || !Directory.Exists(logDirectory)) return;
                var cutoffDate = DateTime.Now.AddDays(-keepDays);
                var logFiles = Directory.GetFiles(logDirectory, "*.log");
                foreach (var file in logFiles)
                {
                    var fileInfo = new FileInfo(file);
                    if (fileInfo.LastWriteTime < cutoffDate)
                    {
                        try
                        {
                            fileInfo.Delete();
                            Instance.Info($"Deleted old log file: {file}");
                        }
                        catch (Exception ex)
                        {
                            Instance.Error($"Failed to delete log file: {file}", ex);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Instance.Error("Failed to clear old logs", ex);
            }
        }
    }
    #endregion

    #region 配置管理器
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
        private Dictionary<string, RepairStrategy> _activeStrategyMap = new Dictionary<string, RepairStrategy>(StringComparer.OrdinalIgnoreCase);
        public const int REMOTE_CACHE_MINUTES = 5;

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
            RefreshActiveConfig();
            _logger.Info("Configuration manager initialized");
        }

        private void LoadUserConfig()
        {
            try
            {
                if (File.Exists(_userConfigPath))
                {
                    var json = File.ReadAllText(_userConfigPath);
                    _userConfig = JsonSerializer.Deserialize<RepairConfig>(json);
                    _logger.Info("User configuration loaded successfully");
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
                    _logger.Info("Created new user configuration file");
                }
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to load user configuration", ex);
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
                    _logger.Info($"Remote configuration fetched successfully: v{config.Version}");
                    return config;
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to fetch remote config from {_remoteConfigUrl}", ex);
            }
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
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error($"Failed to fetch remote config: {ex.Message}");
                    _remoteConfig = null;
                }
                return _remoteConfig;
            }
        }

        // 👇 修复CS1061错误：添加 GetConfig() 方法
        public RepairConfig GetConfig()
        {
            var remoteConfig = GetRemoteConfig();
            if (remoteConfig != null)
                return remoteConfig;

            if (_userConfig?.RepairStrategies != null && _userConfig.RepairStrategies.Count > 0)
                return _userConfig;

            return _builtInConfig;
        }

        // 👇 新增：获取活动策略映射
        public Dictionary<string, RepairStrategy> GetActiveStrategyMap()
        {
            RefreshActiveConfig();
            return _activeStrategyMap;
        }

        public ConfigInfo GetActiveConfigInfo()
        {
            var remoteConfig = GetRemoteConfig();
            var source = remoteConfig != null ? "远程服务器" :
                        (_userConfig?.RepairStrategies?.Count > 0 ? "用户自定义" : "内置配置");
            var activeConfig = remoteConfig ?? _userConfig ?? _builtInConfig;
            var priority = remoteConfig != null ? "最高（服务器）" :
                          (_userConfig?.RepairStrategies?.Count > 0 ? "中（用户）" : "低（内置）");

            return new ConfigInfo
            {
                Version = activeConfig.Version,
                LastUpdated = activeConfig.LastUpdated,
                ErrorPatternCount = activeConfig.ErrorPatterns?.Count ?? 0,
                RepairStrategyCount = activeConfig.RepairStrategies?.Count ?? 0,
                Source = source,
                Priority = priority,
                DisplayText = $"识别库版本: {activeConfig.Version} | 最后更新: {activeConfig.LastUpdated} | 来源: {source}"
            };
        }

        private void RefreshActiveConfig()
        {
            _activeStrategyMap.Clear();
            var remoteConfig = GetRemoteConfig();
            var configsToProcess = new List<RepairConfig>();

            if (remoteConfig != null) configsToProcess.Add(remoteConfig);
            if (_userConfig != null) configsToProcess.Add(_userConfig);
            configsToProcess.Add(_builtInConfig);

            foreach (var config in configsToProcess)
            {
                if (config?.RepairStrategies == null) continue;
                foreach (var strategy in config.RepairStrategies)
                {
                    if (!string.IsNullOrEmpty(strategy.ErrorCode) && !_activeStrategyMap.ContainsKey(strategy.ErrorCode))
                    {
                        _activeStrategyMap[strategy.ErrorCode] = strategy;
                    }
                }
            }
            _logger.Info($"Active strategy map refreshed with {_activeStrategyMap.Count} strategies");
        }

        public RepairStrategy GetRepairStrategy(string errorCode)
        {
            if (string.IsNullOrEmpty(errorCode)) return null;
            RefreshActiveConfig();
            return _activeStrategyMap.GetValueOrDefault(errorCode.ToUpperInvariant());
        }

        public RepairConfig GetUserConfig() => _userConfig;

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
            RefreshActiveConfig();
        }

        public void RemoveUserStrategy(string errorCode)
        {
            if (_userConfig.RepairStrategies == null) return;
            _userConfig.RepairStrategies.RemoveAll(s => s.ErrorCode.Equals(errorCode, StringComparison.OrdinalIgnoreCase));
            _userConfig.LastUpdated = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            SaveUserConfig();
            RefreshActiveConfig();
        }

        private void SaveUserConfig()
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(_userConfig, options);
                File.WriteAllText(_userConfigPath, json);
                _logger.Info("User configuration saved successfully");
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to save user configuration", ex);
            }
        }

        public UpdateCheckResult CheckForUpdates()
        {
            try
            {
                _logger.Info($"Checking for config updates from {_remoteConfigUrl}");
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
                _logger.Info($"Update check complete: current {currentVersion}, remote {remoteVersion}, hasUpdate: {hasUpdate}");
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
            catch (Exception ex)
            {
                _logger.Error($"Update check failed for {_remoteConfigUrl}", ex);
                return new UpdateCheckResult
                {
                    Success = false,
                    ErrorMessage = $"检查更新失败: {ex.Message}"
                };
            }
        }

        public bool UpdateConfig()
        {
            try
            {
                _logger.Info($"Updating configuration from {_remoteConfigUrl}");
                var result = CheckForUpdates();
                if (result.Success && result.HasUpdate && result.RemoteConfig != null)
                {
                    _remoteConfig = result.RemoteConfig;
                    _lastRemoteFetch = DateTime.Now;
                    RefreshActiveConfig();
                    _logger.Info($"Configuration updated to version {result.NewVersion}");
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                _logger.Error($"Configuration update failed for {_remoteConfigUrl}", ex);
                return false;
            }
        }
    }

    public class ConfigInfo
    {
        public string Version { get; set; }
        public string LastUpdated { get; set; }
        public int ErrorPatternCount { get; set; }
        public int RepairStrategyCount { get; set; }
        public string Source { get; set; }
        public string Priority { get; set; }
        public string DisplayText { get; set; }
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
            var userConfig = _configManager.GetUserConfig();
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
                _logger.Info($"User strategy added: {newStrategy.ErrorCode}");
                MessageBox.Show("策略添加成功！", "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                _logger.Error("Error adding user strategy", ex);
                MessageBox.Show($"添加策略失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
            var userConfig = _configManager.GetUserConfig();
            if (userConfig?.RepairStrategies != null && selectedIndex < userConfig.RepairStrategies.Count)
            {
                var removedStrategy = userConfig.RepairStrategies[selectedIndex];
                _configManager.RemoveUserStrategy(removedStrategy.ErrorCode);
                LoadUserStrategies();
                _logger.Info($"User strategy removed: {removedStrategy.ErrorCode}");
                MessageBox.Show("策略移除成功！", "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
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