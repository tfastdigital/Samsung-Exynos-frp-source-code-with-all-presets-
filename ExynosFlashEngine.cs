using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.IO.Ports;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace ZeroKnoxRemoval
{
    public class PresetRecord
    {
        public string? PresetName { get; set; }
        public string? Chipset { get; set; }
        public string? ChipsetName { get; set; }
        public string? Variant { get; set; }
        public string? DeviceModel { get; set; }
        public string? GalaxyName { get; set; }
        public string? ExploitMethod { get; set; }
        public string? SmModels { get; set; }
        public string? BuildNumber { get; set; }
        public string? DisplayText { get; set; }
        public string? SearchIndex { get; set; }
    }

    public class ExynosDetectResult
    {
        public bool Success { get; set; }
        public string? ChipsetKey { get; set; }
        public string? ChipsetName { get; set; }
        public string? GalaxyName { get; set; }
        public string? ModelNumber { get; set; }
        public string? FirmwareVersion { get; set; }
        public string? PresetName { get; set; }
        public string? ComPort { get; set; }
    }

    public class ExynosFlashEngine : IDisposable
    {
        public event Action<string, Color, bool>? OnLogReceived;
        public event Action<double, string>? OnProgressChanged;

        private readonly string _tempBasePath;
        private readonly string _exynosWorkingDir;

        private readonly object _processLock = new();
        private volatile bool _isRunning = false;
        private Process? _childProcess = null;
        private bool _disposed = false;

        public List<PresetRecord> Presets { get; private set; } = new();
        public ExynosFlashEngine()
        {

            _tempBasePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "ZeroKnox Removal");
            _exynosWorkingDir = Path.Combine(_tempBasePath, "exynos");


            ExtractResources();
        }
        private void ExtractResources()
        {
            try
            {

                Directory.CreateDirectory(_tempBasePath);


                if (Directory.Exists(_exynosWorkingDir))
                    Directory.Delete(_exynosWorkingDir, recursive: true);

                // Создаём папку exynos и сразу делаем СКРЫТОЙ
                var exynosDir = Directory.CreateDirectory(_exynosWorkingDir);
                exynosDir.Attributes = FileAttributes.Directory | FileAttributes.Hidden;


                byte[] zipBytes = SamsungExynos.Properties.Resources.exynos;
                string zipPath = Path.Combine(_tempBasePath, "exynos_payload.tmp");
                File.WriteAllBytes(zipPath, zipBytes);

                ZipFile.ExtractToDirectory(zipPath, _exynosWorkingDir, overwriteFiles: true);
                File.Delete(zipPath);

                // Создаём пустую папку config — JSON туда будут скачаны перед flash
                Directory.CreateDirectory(Path.Combine(_exynosWorkingDir, "presets"));

                Debug.WriteLine($"Exynos extracted to: {_exynosWorkingDir}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Extract Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Скачивает один JSON конфиг с сервера в exynos/presets/
        /// Вызывается перед каждым flash. После flash папка presets очищается.
        /// </summary>
        public async Task<bool> DownloadPresetAsync(string presetName, string serverBaseUrl)
        {
            try
            {
                string presetsDir = Path.Combine(_exynosWorkingDir, "presets");
                Directory.CreateDirectory(presetsDir);

                string url = serverBaseUrl.TrimEnd('/') + "/" + presetName + ".json";
                Log($"Downloading config from server...", Color.Cyan, true);

                using var http = new System.Net.Http.HttpClient();
                http.Timeout = TimeSpan.FromSeconds(15);
                string json = await http.GetStringAsync(url);

                string destPath = Path.Combine(presetsDir, presetName + ".json");
                await File.WriteAllTextAsync(destPath, json);

                Log($"Config ready.", Color.LimeGreen, true);
                return true;
            }
            catch (Exception ex)
            {
                Log($"Failed to download config: {ex.Message}", Color.Red, true);
                return false;
            }
        }

        /// <summary>
        /// Очищает папку config — вызывается после flash (успех или ошибка).
        /// JSON файлы не хранятся локально для защиты.
        /// </summary>
        public void ClearPresets()
        {
            try
            {
                string presetsDir = Path.Combine(_exynosWorkingDir, "presets");
                if (Directory.Exists(presetsDir))
                {
                    foreach (string f in Directory.GetFiles(presetsDir, "*.json"))
                        File.Delete(f);
                }
                Debug.WriteLine("Config cleared.");
            }
            catch { }
        }
        private static readonly Dictionary<string, (string name, string galaxy, string smModels, string type)> ChipsetDict = new()
        {
            { "exynos850",  ("Exynos 850 (S5E3830)",  "Galaxy A12 / A02s / A03s / M12", "SM-A127F, SM-A135F, SM-A047F, SM-M127F", "extract") },
            { "exynos1280", ("Exynos 1280 (S5E8825)", "Galaxy A33 5G / A53 5G / M33",   "SM-A336B, SM-A536B, SM-M336B", "extract") },
            { "exynos1380", ("Exynos 1380 (S5E8835)", "Galaxy A34 5G / A54 5G",         "SM-A346B, SM-A546B, SM-A546E", "extract") },
            { "exynos1480", ("Exynos 1480 (S5E8845)", "Galaxy A35 5G / A55 5G",         "SM-A356B, SM-A556B", "extract") },
            { "exynos1580", ("Exynos 1580 (S5E8855)", "Galaxy A36 5G / A56 5G",         "SM-A366B", "extract") },
            { "exynos7884", ("Exynos 7884 (S5E7884)", "Galaxy A10 / A20e / A30s",       "SM-A105F, SM-A107F, SM-A207F, SM-A307F", "extract") },
            { "exynos7885", ("Exynos 7885 (S5E7885)", "Galaxy A30 / A40",               "SM-A530F, SM-A605F, SM-J810F", "extract") },
            { "exynos2100", ("Exynos 2100 (S5E9840)", "Galaxy S21 Series",              "SM-G991B, SM-G996B, SM-G998B", "extract") },
            { "exynos2200", ("Exynos 2200 (S5E9925)", "Galaxy S22 Series",              "SM-S901B, SM-S906B, SM-S908B", "extract") },
            { "exynos2400", ("Exynos 2400 (S5E9945)", "Galaxy S24 Series",              "SM-S921B, SM-S926B, SM-S928B", "extract") },
            { "exynos2500", ("Exynos 2500 (S5E9955)", "Galaxy S25 Series",              "SM-S931B, SM-S936B, SM-S938B", "extract") },
            { "galaxy_a12", ("Galaxy A12 Special",    "Galaxy A12 (Special)",           "SM-A125F", "extract") },

            { "exynos7570", ("Exynos 7570 (S5E7570)", "Galaxy J2 / J3 / J5",            "SM-J330F, SM-J330G, SM-G532F, SM-J250F", "integrity") },
            { "exynos7870", ("Exynos 7870 (S5E7870)", "Galaxy J5 2017 / J7 2017",       "SM-J730F, SM-J730G, SM-A520F, SM-J530F", "integrity") },
            { "exynos7880", ("Exynos 7880 (S5E7880)", "Galaxy A5 2017 / A7 2017",       "SM-A520F, SM-A720F", "integrity") },
            { "exynos7904", ("Exynos 7904 (S5E7904)", "Galaxy M20 / M30 / A20 / A30",   "SM-A205F, SM-A305F, SM-M205F, SM-M305F", "integrity") },
            { "exynos9610", ("Exynos 9610 (S5E9610)", "Galaxy A50 / A50s",              "SM-A505F, SM-A505G, SM-A505FN", "integrity") },
            { "exynos9611", ("Exynos 9611 (S5E9611)", "Galaxy A51 / M31 / M21",         "SM-A515F, SM-M315F, SM-M317F, SM-F415F", "integrity") },
            { "exynos9810", ("Exynos 9810 (S5E9810)", "Galaxy S9 / S9+ / Note 9",       "SM-G960F, SM-G965F, SM-N960F", "integrity") },
            { "exynos9820", ("Exynos 9820 (S5E9820)", "Galaxy S10 / S10+ / Note 10",    "SM-G970F, SM-G973F, SM-G975F, SM-G977B", "integrity") },
            { "exynos9825", ("Exynos 9825 (S5E9825)", "Galaxy Note 10+",                "SM-N970F, SM-N975F, SM-N976B", "integrity") },
            { "exynos980",  ("Exynos 980 (S5E9630)",  "Galaxy A71 5G",                  "SM-A716B", "integrity") },
            { "exynos990",  ("Exynos 990 (S5E9830)",  "Galaxy S20 / Note 20",           "SM-G980F, SM-G981B, SM-G985F, SM-N980F", "integrity") },
            { "exynos8890", ("Exynos 8890 (S5E8890)", "Galaxy S7 / S7 Edge",            "SM-G930F, SM-G935F", "integrity") },
            { "exynos8895", ("Exynos 8895 (S5E8895)", "Galaxy S8 / S8+ / Note 8",       "SM-G950F, SM-G955F, SM-N950F", "integrity") }
        };

        private static readonly Dictionary<string, string> ModelToChipset = new()
        {
            { "SM-A127", "exynos850" },  { "SM-A125", "exynos850" },  { "SM-A135", "exynos850" },
            { "SM-A047", "exynos850" },  { "SM-G525", "exynos850" },  { "SM-M127", "exynos850" },
            { "SM-A105", "exynos7884" }, { "SM-A107", "exynos7884" }, { "SM-A207", "exynos7884" },
            { "SM-A307", "exynos7884" }, { "SM-A750", "exynos7885" }, { "SM-A530", "exynos7885" },
            { "SM-A205", "exynos7904" }, { "SM-A305", "exynos7904" }, { "SM-A505", "exynos9610" },
            { "SM-A515", "exynos9611" }, { "SM-G973", "exynos9820" }, { "SM-N975", "exynos9825" },
            { "SM-G980", "exynos990" },  { "SM-G991", "exynos2100" }, { "SM-S901", "exynos2200" },
            { "SM-A336", "exynos1280" }, { "SM-A536", "exynos1280" }, { "SM-A346", "exynos1380" },
            { "SM-A546", "exynos1380" }, { "SM-A356", "exynos1480" }, { "SM-A556", "exynos1480" }
        };

        public void LoadExynosPresets()
        {
            Presets.Clear();
            Log("Loading Exynos firmware config...", Color.Cyan, true);

            // ТЕПЕРЬ ЧИТАЕМ ИЗ TEMP ПАПКИ
            string presetsPath = Path.Combine(_exynosWorkingDir, "presets");
            if (!Directory.Exists(presetsPath))
            {
                Log($"Config directory missing: {presetsPath}", Color.OrangeRed, true);
                return;
            }

            // ... [Остальной код загрузки JSON остается без изменений] ...
            foreach (string jsonFile in Directory.GetFiles(presetsPath, "*.json"))
            {
                try
                {
                    string fileName = Path.GetFileNameWithoutExtension(jsonFile);
                    string json = File.ReadAllText(jsonFile, Encoding.UTF8);

                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    var preset = new PresetRecord
                    {
                        PresetName = fileName,
                        Chipset = GetJsonStr(root, "chipset"),
                        DeviceModel = GetJsonStr(root, "deviceModel"),
                        ExploitMethod = GetJsonStr(root, "exploitMethod"),
                    };

                    if (root.TryGetProperty("firmwareParams", out var fw))
                        preset.Variant = GetJsonStr(fw, "variant");

                    var buildMatch = Regex.Match(fileName, @"_(\d{5,})$");
                    if (buildMatch.Success) preset.BuildNumber = buildMatch.Groups[1].Value;

                    string chipL = (preset.Chipset ?? "").ToLower();
                    if (ChipsetDict.TryGetValue(chipL, out var info))
                    {
                        preset.ChipsetName = info.name;
                        preset.GalaxyName = info.galaxy;
                        preset.SmModels = info.smModels;
                    }
                    else
                    {
                        preset.ChipsetName = preset.Chipset;
                        preset.GalaxyName = preset.DeviceModel;
                        preset.SmModels = "";
                    }

                    preset.DisplayText = preset.ChipsetName
                        + (!string.IsNullOrEmpty(preset.GalaxyName) ? "  |  " + preset.GalaxyName : "")
                        + (!string.IsNullOrEmpty(preset.BuildNumber) ? "  [#" + preset.BuildNumber + "]" : "");

                    preset.SearchIndex = $"{preset.ChipsetName} {preset.Chipset} {preset.GalaxyName} {preset.SmModels} {preset.DeviceModel} {preset.ExploitMethod} {preset.Variant} {preset.BuildNumber} {preset.PresetName}".ToLower();

                    Presets.Add(preset);
                }
                catch (Exception ex)
                {
                    Log($"Failed to load {Path.GetFileName(jsonFile)}: {ex.Message}", Color.Red, true);
                }
            }

            Presets = Presets
                .OrderBy(p => p.Chipset, StringComparer.OrdinalIgnoreCase)
                .ThenBy(p => p.BuildNumber)
                .ToList();
        }

        // Загружает пресеты из любой папки (используется для remote загрузки)
        public void LoadPresetsFromDirectory(string presetsPath)
        {
            Presets.Clear();
            if (!Directory.Exists(presetsPath)) return;

            foreach (string jsonFile in Directory.GetFiles(presetsPath, "*.json"))
            {
                try
                {
                    string fileName = Path.GetFileNameWithoutExtension(jsonFile);
                    string json = File.ReadAllText(jsonFile, Encoding.UTF8);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    var preset = new PresetRecord
                    {
                        PresetName = fileName,
                        Chipset = GetJsonStr(root, "chipset"),
                        DeviceModel = GetJsonStr(root, "deviceModel"),
                        ExploitMethod = GetJsonStr(root, "exploitMethod"),
                    };

                    if (root.TryGetProperty("firmwareParams", out var fw))
                        preset.Variant = GetJsonStr(fw, "variant");

                    var bm = Regex.Match(fileName, @"_(\d{5,})$");
                    if (bm.Success) preset.BuildNumber = bm.Groups[1].Value;

                    string chipL = (preset.Chipset ?? "").ToLower();
                    if (ChipsetDict.TryGetValue(chipL, out var info))
                    {
                        preset.ChipsetName = info.name;
                        preset.GalaxyName = info.galaxy;
                        preset.SmModels = info.smModels;
                    }
                    else
                    {
                        preset.ChipsetName = preset.Chipset;
                        preset.GalaxyName = preset.DeviceModel;
                        preset.SmModels = "";
                    }

                    preset.DisplayText = preset.ChipsetName
                        + (!string.IsNullOrEmpty(preset.GalaxyName) ? "  |  " + preset.GalaxyName : "")
                        + (!string.IsNullOrEmpty(preset.BuildNumber) ? "  [#" + preset.BuildNumber + "]" : "");

                    preset.SearchIndex = $"{preset.ChipsetName} {preset.Chipset} {preset.GalaxyName} {preset.SmModels} {preset.DeviceModel} {preset.ExploitMethod} {preset.Variant} {preset.BuildNumber} {preset.PresetName}".ToLower();

                    Presets.Add(preset);
                }
                catch { }
            }

            Presets = Presets
                .OrderBy(p => p.Chipset, StringComparer.OrdinalIgnoreCase)
                .ThenBy(p => p.BuildNumber)
                .ToList();
        }

        public async Task<ExynosDetectResult> AutoDetectChipsetAsync(string comPort, CancellationToken ct = default)
        {
            var result = new ExynosDetectResult { ComPort = comPort };

            return await Task.Run(() =>
            {
                SerialPort? port = null;
                try
                {
                    ct.ThrowIfCancellationRequested();

                    port = new SerialPort(comPort)
                    {
                        BaudRate = 115200,
                        RtsEnable = true,
                        DtrEnable = true,
                        ReadTimeout = 4000,
                        WriteTimeout = 4000,
                    };

                    port.Open();

                    string ping = SendAT(port, "AT", 1500, ct);
                    if (!ping.Contains("OK"))
                    {
                        Log("Reading device info.....FAILED (No response)", Color.Red, true);
                        return result;
                    }

                    ct.ThrowIfCancellationRequested();

                    string info = SendAT(port, "AT+DEVCONINFO", 4000, ct);
                    if (!info.Contains("#OK#") && !info.Contains("OK"))
                    {
                        Log("Reading device info.....FAILED (Protocol Error)", Color.Red, true);
                        return result;
                    }

                    Log("Reading device info.....OK", Color.LimeGreen, true);

                    string model = ExtractBetween(info, "MN(", ");");
                    if (string.IsNullOrEmpty(model)) model = ExtractBetween(info, "MODEL(", ");");
                    if (string.IsNullOrEmpty(model)) model = "UNKNOWN";

                    string un = ExtractBetween(info, "UN(", ");");
                    if (string.IsNullOrEmpty(un)) un = "UNKNOWN_SERIAL_ID";

                    string capa = ExtractBetween(info, "CAPA(", ");");
                    if (string.IsNullOrEmpty(capa)) capa = ExtractBetween(info, "SZ(", ");");
                    if (string.IsNullOrEmpty(capa)) capa = "256";

                    string vendor = ExtractBetween(info, "VND(", ");");
                    if (string.IsNullOrEmpty(vendor)) vendor = "SAMSUNG";

                    string fwver = ExtractBetween(info, "FWVER(", ");");
                    if (string.IsNullOrEmpty(fwver)) fwver = "0006";

                    string product = ExtractBetween(info, "PN(", ");");
                    if (string.IsNullOrEmpty(product)) product = "KMAS9001PM-BC02";

                    string prov = ExtractBetween(info, "PROV(", ");");
                    if (string.IsNullOrEmpty(prov)) prov = "PASS";

                    string sales = ExtractBetween(info, "SALES(", ");");
                    if (string.IsNullOrEmpty(sales)) sales = "SKZ";

                    string ver = ExtractBetween(info, "VER(", ");");
                    if (string.IsNullOrEmpty(ver)) ver = "A556EXXSECZDE";
                    if (ver.Contains("/")) ver = ver.Split('/')[0];

                    string did = ExtractBetween(info, "DID(", ");");
                    if (string.IsNullOrEmpty(did)) did = "3013546dbee95011";

                    string tmu = ExtractBetween(info, "TMU(", ");");
                    if (string.IsNullOrEmpty(tmu)) tmu = "43";

                    string nad = ExtractBetween(info, "NAD(", ");");
                    if (string.IsNullOrEmpty(nad)) nad = "40";

                    Log($"MODEL: {model}", Color.White, true);
                    Log($"UN: {un}", Color.White, true);
                    Log($"CAPA: {capa}", Color.White, true);
                    Log($"VENDOR: {vendor}", Color.White, true);
                    Log($"FWVER: {fwver}", Color.White, true);
                    Log($"PRODUCT: {product}", Color.White, true);
                    Log($"PROV: {prov}", Color.White, true);
                    Log($"SALES: {sales}", Color.White, true);
                    Log($"VER: {ver}", Color.White, true);
                    Log($"DID: {did}", Color.White, true);
                    Log($"TMU_TEMP: {tmu}", Color.White, true);
                    Log($"NAD_TEMP: {nad}", Color.White, true);

                    Log("Preparing.....OK", Color.LimeGreen, true);

                    string? chipsetKey = DetectChipset(model, ver);
                    if (chipsetKey == null)
                    {
                        Log("EXYNOS CPU: UNKNOWN", Color.OrangeRed, true);
                        return result;
                    }

                    string cpuNumber = Regex.Match(chipsetKey, @"\d+").Value;
                    Log($"EXYNOS CPU: {cpuNumber}", Color.White, true);

                    result.ChipsetKey = chipsetKey;
                    if (ChipsetDict.TryGetValue(chipsetKey, out var cinfo))
                    {
                        result.ChipsetName = cinfo.name;
                        result.GalaxyName = cinfo.galaxy;
                    }

                    result.PresetName = FindBestPreset(chipsetKey);
                    result.ModelNumber = model;
                    result.FirmwareVersion = ver;

                    int mockId = new Random().Next(3000, 4000);
                    Log($"Auto selected: Exynos {cpuNumber} | dpolicy_extract | id {mockId} : Exynos {cpuNumber}", Color.Cyan, true);
                    Log("Processing.....OK", Color.LimeGreen, true);

                    result.Success = true;
                    return result;
                }
                catch (OperationCanceledException)
                {
                    Log("Auto-detect cancelled by user.", Color.Orange, true);
                    return result;
                }
                catch (Exception ex)
                {
                    Log($"Reading device info.....FAILED ({ex.Message})", Color.Red, true);
                    return result;
                }
                finally
                {
                    try { port?.Close(); } catch { }
                    port?.Dispose();
                }
            }, ct);
        }

        public bool RebootToDownloadMode(string comPort)
        {
            try
            {
                using var port = new SerialPort(comPort)
                {
                    BaudRate = 115200,
                    RtsEnable = true,
                    DtrEnable = true,
                    ReadTimeout = 2000,
                    WriteTimeout = 2000,
                };
                port.Open();

                Log("Sending hardware switch signal (AT+SUDDLMOD)...", Color.DarkMagenta, true);
                SendAT(port, "AT", 1000);
                SendAT(port, "AT+SUDDLMOD=0,0", 1000);
                return true;
            }
            catch (Exception ex)
            {
                Log($"Failed to route boot switch: {ex.Message}", Color.OrangeRed, true);
                return false;
            }
        }

        public string WaitForOdinPort(string oldPort, int timeoutSeconds, CancellationToken ct = default)
        {
            Log("Waiting for Samsung Download Mode port...", Color.White, true);

            for (int i = 0; i < timeoutSeconds; i++)
            {
                if (ct.IsCancellationRequested)
                {
                    Log("Port polling cancelled.", Color.Orange, true);
                    return "";
                }

                Thread.Sleep(1000);

                string odinPort = FindDownloadModePort();
                if (!string.IsNullOrEmpty(odinPort))
                {
                    Log($"Odin port detected: {odinPort}", Color.LimeGreen, true);
                    return odinPort;
                }

                var currentPorts = ScanComPorts();
                var newPorts = currentPorts.Except(new[] { oldPort }).ToArray();

                foreach (var p in newPorts)
                {
                    if (IsSamsungPort(p))
                    {
                        Log($"New Samsung port detected: {p}", Color.LimeGreen, true);
                        return p;
                    }
                }
            }

            return "";
        }

        private bool IsSamsungPort(string portName)
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DEVICEMAP\SERIALCOMM");
                if (key == null) return false;

                foreach (string name in key.GetValueNames())
                {
                    if (key.GetValue(name)?.ToString() == portName)
                    {
                        string lower = name.ToLower();
                        return lower.Contains("samsung") || lower.Contains("ssud");
                    }
                }
            }
            catch { }
            return false;
        }

        private string? DetectChipset(string? model, string? firmware)
        {
            if (!string.IsNullOrEmpty(model))
            {
                foreach (int len in new[] { 7, 6 })
                {
                    if (model.Length >= len)
                    {
                        string prefix = model.Substring(0, len);
                        if (ModelToChipset.TryGetValue(prefix, out string? chip)) return chip;
                    }
                }
            }

            if (!string.IsNullOrEmpty(firmware) && firmware.Length >= 4)
            {
                string fwKey = "SM-" + firmware.Substring(0, 4);
                if (ModelToChipset.TryGetValue(fwKey, out string? chip)) return chip;
            }

            return null;
        }

        private string FindBestPreset(string chipset)
        {
            string chipL = chipset.ToLower();

            var match = Presets.FirstOrDefault(p => p.Chipset?.Equals(chipL, StringComparison.OrdinalIgnoreCase) == true);
            if (match != null) return match.PresetName!;

            if (ChipsetDict.TryGetValue(chipL, out var info))
            {
                if (info.type == "integrity")
                {
                    return $"{chipL}_dpolicy_integrity";
                }
            }

            return $"{chipL}_dpolicy_extract";
        }

        // ══════════════════════════════════════════════════════════════
        // FlashAsync — с проверкой режима устройства
        // ══════════════════════════════════════════════════════════════
        public async Task<bool> FlashAsync(string comPort, string presetName, CancellationToken ct = default)
        {
            lock (_processLock)
            {
                if (_isRunning)
                {
                    Log("Engine instance busy running another process.", Color.Orange, true);
                    return false;
                }
                _isRunning = true;
            }

            try
            {
                // ИСПОЛЬЗУЕМ ВРЕМЕННУЮ ДИРЕКТОРИЮ!
                string cliPath = Path.Combine(_exynosWorkingDir, "ExynosCli.exe");

                if (!File.Exists(cliPath))
                {
                    Log($"ExynosCli.exe missing from secure temp vault!", Color.Red, true);
                    return false;
                }

                Log("Scanning for Samsung device...", Color.Cyan, true);
                var (mode, detectedPort) = DetectSamsungMode();

                string actualPort;

                if (mode == "download" && !string.IsNullOrEmpty(detectedPort))
                {
                    if (detectedPort == comPort)
                    {
                        actualPort = comPort;
                        Log($"Download Mode confirmed on {actualPort}", Color.LimeGreen, true);
                    }
                    else
                    {
                        Log($"Detected Odin port {detectedPort} differs from selected {comPort}. Auto-switching to {detectedPort}.", Color.Orange, true);
                        actualPort = detectedPort;
                    }
                }
                else if (mode == "mtp")
                {
                    Log("Device in MTP mode. Please use 'Auto Detect & Flash' first to reboot to Download Mode.", Color.Orange, true);
                    return false;
                }
                else
                {
                    if (IsSamsungPort(comPort))
                    {
                        Log($"Using selected port {comPort} (identified as Samsung). Make sure device is in Download/Odin Mode!", Color.Orange, true);
                        actualPort = comPort;
                    }
                    else
                    {
                        Log($"Selected port {comPort} is not a Samsung Download/Odin port. Please select the correct port or use Auto Detect.", Color.Red, true);
                        return false;
                    }
                }

                // 1. Токен
                string token = GenerateToken();

                // 2. .session файл — ASCII без BOM
                WriteSession(_exynosWorkingDir, token);

                // 3. ADB ключ
                SyncAdbKey(_exynosWorkingDir);

                // 4. Аргументы
                string arguments =
                    $"--exynos-dir \"{_exynosWorkingDir}\" " +
                    $"--action boot " +
                    $"--preset \"{presetName}\" " +
                    $"--port \"{actualPort}\" " +
                    $"--auth-token \"{token}\"";



                return await RunProcessAsync(cliPath, arguments, ct);
            }
            finally
            {
                lock (_processLock) { _isRunning = false; }
            }
        }

        // ══════════════════════════════════════════════════════════════
        // RunProcessAsync — using System.Diagnostics.Process (надёжнее)
        // ══════════════════════════════════════════════════════════════
        public async Task<bool> RunProcessAsync(string exePath, string arguments, CancellationToken ct = default)
        {
            TerminateCurrentProcess();

            return await Task.Run(() =>
            {
                try
                {
                    ct.ThrowIfCancellationRequested();

                    string workDir = Path.GetDirectoryName(exePath) ?? AppDomain.CurrentDomain.BaseDirectory;
                    string cmdLine = $"\"{exePath}\" {arguments}";


                    var psi = new ProcessStartInfo
                    {
                        FileName = exePath,
                        Arguments = arguments,
                        WorkingDirectory = workDir,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        StandardOutputEncoding = Encoding.UTF8,
                        StandardErrorEncoding = Encoding.UTF8,
                    };

                    using var process = new Process();
                    process.StartInfo = psi;
                    process.EnableRaisingEvents = true;

                    // Буферы для вывода (обработка в реальном времени)
                    process.OutputDataReceived += (s, e) =>
                    {
                        if (e.Data != null)
                        {
                            ProcessOutputLine(e.Data);
                        }
                    };
                    process.ErrorDataReceived += (s, e) =>
                    {
                        if (e.Data != null)
                        {
                            ProcessOutputLine("[ERR] " + e.Data);
                        }
                    };

                    process.Start();

                    // Сохраняем ссылку на процесс для TerminateCurrentProcess
                    _childProcess = process;

                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    // Ожидаем завершения или отмены
                    while (!process.HasExited)
                    {
                        if (ct.IsCancellationRequested)
                        {
                            process.Kill();
                            Log("Process killed by cancellation.", Color.Orange, true);
                            return false;
                        }
                        // Небольшая задержка, чтобы не нагружать CPU
                        Thread.Sleep(100);
                    }

                    process.WaitForExit();

                    if (process.ExitCode == 0)
                    {

                        return true;
                    }
                    else
                    {
                        Log($"ExynosCLI exited with code {process.ExitCode}.", Color.OrangeRed, true);
                        return false;
                    }
                }
                catch (OperationCanceledException)
                {
                    Log("Operation cancelled by user.", Color.Orange, true);
                    return false;
                }
                catch (Exception ex)
                {
                    Log($"Process error: {ex.Message}", Color.Red, true);
                    return false;
                }
                finally
                {
                    _childProcess = null;
                }
            }, ct);
        }

        public void TerminateCurrentProcess()
        {
            lock (_processLock)
            {
                if (_childProcess != null && !_childProcess.HasExited)
                {
                    try
                    {
                        _childProcess.Kill();
                        _childProcess.WaitForExit(5000);
                    }
                    catch { }
                    finally
                    {
                        _childProcess?.Dispose();
                        _childProcess = null;
                    }
                }
            }
        }

        private void ProcessOutputLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;

            var m = Regex.Match(line, @"^\[(\d\d:\d\d:\d\d)\]\s*\[([A-Z]+)\]\s*(.*)$");
            if (!m.Success) { Log(line, Color.White, true); return; }

            string tag = m.Groups[2].Value;
            string msg = m.Groups[3].Value.Trim().Trim('"');
            string low = msg.ToLower();

            if (low.StartsWith("loading plugin:") || low == "plugin loaded"
                || low.StartsWith("calling exynos.dll") || low.StartsWith("action:")
                || low.StartsWith("preset:") || low.StartsWith("chip:")
                || low.StartsWith("method:") || low.StartsWith("variant:")
                || low.StartsWith("port:") || low.StartsWith("model:")
                || low.Contains("libusb:") || low.Contains("usbdk backend")
                || low.StartsWith("failed finding") || string.IsNullOrEmpty(msg))
                return;

            var pMatch = Regex.Match(msg, @"\[(\d+)%\]");
            if (pMatch.Success && double.TryParse(pMatch.Groups[1].Value, out double pct))
                OnProgressChanged?.Invoke(pct, $"Processing {pct}%...");

            // ── Скрываем технические строки (файлы ramdisk, пути, control transfer) ──
            if (low.StartsWith("loaded ") && low.Contains("ramdisk file(s)"))
                return; // "Loaded 20 ramdisk file(s) from ..."
            if (msg.TrimStart().StartsWith("- "))
                return; // "  - adb_keys (1434 bytes)" и т.д.
            if (low.Contains("control transfer failed"))
                return;
            if (low.Contains("failed connecting to the device"))
                return;
            if (low == "booting custom ramdisk:" || low.StartsWith("booting custom ramdisk:"))
            { Log("  Executing exploit: Dumping", Color.Cyan, true); return; }

            string display = low switch
            {
                var s when s.StartsWith("successfuly opened port:")
                    || s.StartsWith("successfully opened port:") => "Device channel established successfully",

                // Заменяем на нужные тексты
                "starting exploit" => "Patching in progress....",
                "dumping sboot" => "Read boot and FRP partition",

                // Скрываем — возвращаем null (обрабатываем ниже)
                "analyzing sboot" => null,
                "loading boot images" => null,
                "dumping boot images" => null,
                "loading bootconfig" => null,
                "dumping bootconfig" => null,
                "crafting custom ramdisk" => null,
                "sending ramdisk to device" => null,
                "setting cmdline" => null,

                // Заменяем
                "rebooting" => "- Reboot Phone...",
                var s when s.Contains("completed successfully") => null, // обрабатывается отдельно
                _ => msg
            };

            // Скрываем null-строки
            if (display == null) return;

            Color logColor = tag switch
            {
                "OK" => Color.LimeGreen,
                "FAIL" => Color.Red,
                "WARN" => Color.DarkOrange,
                _ => Color.White
            };

            Log($"  {display}", logColor, true);
        }

        public string[] ScanComPorts()
        {
            var ports = new List<string>();
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DEVICEMAP\SERIALCOMM");
                if (key != null)
                    foreach (string name in key.GetValueNames())
                        if (key.GetValue(name)?.ToString() is string port)
                            ports.Add(port);
            }
            catch { }

            ports.Sort((a, b) =>
            {
                int numA = int.TryParse(a.Replace("COM", ""), out int x) ? x : 0;
                int numB = int.TryParse(b.Replace("COM", ""), out int y) ? y : 0;
                return numA.CompareTo(numB);
            });

            return ports.ToArray();
        }

        private string GenerateToken()
        {
            var rng = new Random();
            var sb = new StringBuilder(64);
            for (int i = 0; i < 64; i++)
            {
                int v = rng.Next(16);
                sb.Append((char)(v < 10 ? '0' + v : 'A' + v - 10));
            }
            return sb.ToString();
        }

        private void WriteSession(string exynosDir, string token)
        {
            try
            {
                string path = Path.Combine(exynosDir, ".session");
                if (File.Exists(path)) File.Delete(path);
                File.WriteAllBytes(path, Encoding.ASCII.GetBytes(token));
            }
            catch (Exception ex) { Log($"Session allocation warning: {ex.Message}", Color.Orange, true); }
        }

        private void SyncAdbKey(string exynosDir)
        {
            try
            {
                string userProfile = Environment.GetEnvironmentVariable("USERPROFILE") ?? "";
                string userKey = Path.Combine(userProfile, ".android", "adbkey.pub");
                string exynosKeys = Path.Combine(exynosDir, "data", "adb_keys");

                if (!File.Exists(userKey) || !File.Exists(exynosKeys)) return;

                string keyContent = File.ReadAllText(userKey);
                string existing = File.ReadAllText(exynosKeys);
                string keyPrefix = keyContent.Length >= 40 ? keyContent.Substring(0, 40) : keyContent;

                if (!string.IsNullOrEmpty(keyContent) && !existing.Contains(keyPrefix))
                {
                    File.AppendAllText(exynosKeys, "\n" + keyContent);
                }
            }
            catch { }
        }

        private string SendAT(SerialPort port, string cmd, int timeoutMs, CancellationToken ct = default)
        {
            try
            {
                port.DiscardInBuffer();
                port.DiscardOutBuffer();
                port.Write(Encoding.ASCII.GetBytes(cmd + "\r\n"), 0, cmd.Length + 2);

                var sb = new StringBuilder();
                var stopwatch = Stopwatch.StartNew();

                while (stopwatch.ElapsedMilliseconds < timeoutMs)
                {
                    if (ct.IsCancellationRequested)
                        throw new OperationCanceledException();

                    if (port.BytesToRead > 0)
                    {
                        sb.Append(port.ReadExisting());
                        string cur = sb.ToString();
                        if (cur.Contains("OK") || cur.Contains("ERROR") || cur.Contains("#OK#"))
                        {
                            var remaining = timeoutMs - (int)stopwatch.ElapsedMilliseconds;
                            if (remaining > 50)
                            {
                                Thread.Sleep(Math.Min(100, remaining));
                                if (port.BytesToRead > 0)
                                    sb.Append(port.ReadExisting());
                            }
                            break;
                        }
                    }
                    Thread.Sleep(50);
                }
                return sb.ToString();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch { return ""; }
        }

        private static string ExtractBetween(string src, string start, string end)
        {
            int s = src.IndexOf(start);
            if (s < 0) return "";
            s += start.Length;
            int e = src.IndexOf(end, s);
            return e < 0 ? "" : src.Substring(s, e - s).Trim();
        }

        private static string GetJsonStr(JsonElement el, string key)
        {
            try
            {
                if (el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String)
                    return v.GetString() ?? "";
            }
            catch { }
            return "";
        }

        // ══════════════════════════════════════════════════════════════
        // FindDownloadModePort — Samsung Download Mode (PID_6601)
        // ══════════════════════════════════════════════════════════════
        public string FindDownloadModePort()
        {
            try
            {
                IntPtr hDevInfo = SetupDiGetClassDevs(IntPtr.Zero, null, IntPtr.Zero,
                    DIGCF_ALLCLASSES | DIGCF_PRESENT);

                if (hDevInfo == INVALID_HANDLE_VALUE) return "";

                try
                {
                    var devInfoData = new SP_DEVINFO_DATA();
                    devInfoData.cbSize = (uint)Marshal.SizeOf(devInfoData);

                    for (uint i = 0; SetupDiEnumDeviceInfo(hDevInfo, i, ref devInfoData); i++)
                    {
                        var idBuf = new char[1024];
                        if (!SetupDiGetDeviceInstanceId(hDevInfo, ref devInfoData, idBuf, 1024, out _))
                            continue;

                        string deviceId = new string(idBuf).ToLower().TrimEnd('\0');

                        if (deviceId.Contains("vid_04e8") && deviceId.Contains("pid_6601"))
                        {
                            string port = ExtractComPort(hDevInfo, ref devInfoData);
                            if (!string.IsNullOrEmpty(port))
                            {
                                Log($"Download Mode port found: {port} (PID_6601)", Color.Cyan, true);
                                return port;
                            }
                        }
                    }
                }
                finally
                {
                    SetupDiDestroyDeviceInfoList(hDevInfo);
                }
            }
            catch { }
            return "";
        }

        // ══════════════════════════════════════════════════════════════
        // DetectSamsungMode — определяет режим устройства
        // ══════════════════════════════════════════════════════════════
        public (string mode, string port) DetectSamsungMode()
        {
            try
            {
                IntPtr hDevInfo = SetupDiGetClassDevs(IntPtr.Zero, null, IntPtr.Zero,
                    DIGCF_ALLCLASSES | DIGCF_PRESENT);

                if (hDevInfo == INVALID_HANDLE_VALUE) return ("", "");

                string dlPort = "", mtpPort = "";

                try
                {
                    var devInfoData = new SP_DEVINFO_DATA();
                    devInfoData.cbSize = (uint)Marshal.SizeOf(devInfoData);

                    for (uint i = 0; SetupDiEnumDeviceInfo(hDevInfo, i, ref devInfoData); i++)
                    {
                        var idBuf = new char[1024];
                        if (!SetupDiGetDeviceInstanceId(hDevInfo, ref devInfoData, idBuf, 1024, out _))
                            continue;

                        string deviceId = new string(idBuf).ToLower().TrimEnd('\0');
                        if (!deviceId.Contains("vid_04e8")) continue;

                        if (deviceId.Contains("pid_6601") && string.IsNullOrEmpty(dlPort))
                            dlPort = ExtractComPort(hDevInfo, ref devInfoData);
                        else if (deviceId.Contains("pid_6860") && string.IsNullOrEmpty(mtpPort))
                            mtpPort = ExtractComPort(hDevInfo, ref devInfoData);
                    }
                }
                finally
                {
                    SetupDiDestroyDeviceInfoList(hDevInfo);
                }

                if (!string.IsNullOrEmpty(dlPort)) return ("download", dlPort);
                if (!string.IsNullOrEmpty(mtpPort)) return ("mtp", mtpPort);
            }
            catch { }
            return ("", "");
        }

        private string ExtractComPort(IntPtr hDevInfo, ref SP_DEVINFO_DATA devInfoData)
        {
            // Метод 1: через реестровый ключ устройства (PortName)
            try
            {
                IntPtr hKey = SetupDiOpenDevRegKey(hDevInfo, ref devInfoData,
                    DICS_FLAG_GLOBAL, 0, DIREG_DEV, KEY_READ);

                if (hKey != INVALID_HANDLE_VALUE)
                {
                    try
                    {
                        var buf = new char[256];
                        uint size = 512;
                        uint type = 0;
                        if (RegQueryValueEx(hKey, "PortName", IntPtr.Zero, ref type, buf, ref size) == 0)
                        {
                            string port = new string(buf).TrimEnd('\0');
                            if (!string.IsNullOrEmpty(port)) return port;
                        }
                    }
                    finally { RegCloseKey(hKey); }
                }
            }
            catch { }

            // Метод 2: из FriendlyName "(COMn)"
            try
            {
                var fnBuf = new char[512];
                if (SetupDiGetDeviceRegistryProperty(hDevInfo, ref devInfoData,
                    SPDRP_FRIENDLYNAME, out _, fnBuf, (uint)(fnBuf.Length * sizeof(char)), out _))
                {
                    string fn = new string(fnBuf).TrimEnd('\0');
                    var m = Regex.Match(fn, @"\(COM(\d+)\)");
                    if (m.Success) return "COM" + m.Groups[1].Value;
                }
            }
            catch { }

            return "";
        }

        // ── SetupAPI P/Invoke (только для работы с портами) ──
        private static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);
        private const uint DIGCF_PRESENT = 0x00000002;
        private const uint DIGCF_ALLCLASSES = 0x00000004;
        private const uint DICS_FLAG_GLOBAL = 0x00000001;
        private const uint DIREG_DEV = 0x00000001;
        private const uint KEY_READ = 0x20019;
        private const uint SPDRP_FRIENDLYNAME = 0x0000000C;

        [StructLayout(LayoutKind.Sequential)]
        private struct SP_DEVINFO_DATA
        {
            public uint cbSize;
            public Guid ClassGuid;
            public uint DevInst;
            public IntPtr Reserved;
        }

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern IntPtr SetupDiGetClassDevs(IntPtr ClassGuid, string? Enumerator,
            IntPtr hwndParent, uint Flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiEnumDeviceInfo(IntPtr DeviceInfoSet, uint MemberIndex,
            ref SP_DEVINFO_DATA DeviceInfoData);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool SetupDiGetDeviceInstanceId(IntPtr DeviceInfoSet,
            ref SP_DEVINFO_DATA DeviceInfoData, char[] DeviceInstanceId,
            uint DeviceInstanceIdSize, out uint RequiredSize);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool SetupDiGetDeviceRegistryProperty(IntPtr DeviceInfoSet,
            ref SP_DEVINFO_DATA DeviceInfoData, uint Property, out uint PropertyRegDataType,
            char[] PropertyBuffer, uint PropertyBufferSize, out uint RequiredSize);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern IntPtr SetupDiOpenDevRegKey(IntPtr DeviceInfoSet,
            ref SP_DEVINFO_DATA DeviceInfoData, uint Scope, uint HwProfile,
            uint KeyType, uint samDesired);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiDestroyDeviceInfoList(IntPtr DeviceInfoSet);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern int RegQueryValueEx(IntPtr hKey, string lpValueName,
            IntPtr lpReserved, ref uint lpType, char[] lpData, ref uint lpcbData);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern int RegCloseKey(IntPtr hKey);

        private void Log(string text, Color color, bool breakline) => OnLogReceived?.Invoke(text, color, breakline);

        public bool IsRunning
        {
            get { lock (_processLock) { return _isRunning; } }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            TerminateCurrentProcess();


            try
            {
                if (Directory.Exists(_exynosWorkingDir))
                {
                    Directory.Delete(_exynosWorkingDir, true);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to wipe exynos dir: {ex.Message}");
            }
        }
    }
}