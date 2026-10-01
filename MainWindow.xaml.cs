using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace VisionWorkbenchLicenseIssuer;

public partial class MainWindow : Window
{
    private const string DefaultOutputDirectory = @"D:\";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
    private readonly DispatcherTimer _toastTimer;
    private bool _syncingModuleChecks;
    private readonly string _issuerDataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VisionWorkbenchLicenseIssuer");

    public MainWindow()
    {
        InitializeComponent();
        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            ToastBorder.Visibility = Visibility.Collapsed;
        };
        foreach (var checkBox in ModuleChecks())
        {
            checkBox.Checked += ModuleCheck_Changed;
            checkBox.Unchecked += ModuleCheck_Changed;
        }
        StatusText.Text = "粘贴客户申请码，选择有效期后点击“生成许可证并复制”。";
    }

    private CheckBox[] ModuleChecks() =>
    [
        DetectionModuleCheck, SemanticSegmentationModuleCheck, InstanceSegmentationModuleCheck,
        PoseModuleCheck, BehaviorModuleCheck, AiTextModuleCheck, BarcodeModuleCheck, QrCodeModuleCheck,
    ];

    private void AllModulesCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_syncingModuleChecks || AllModulesCheck is null || ModuleChecks().Any(checkBox => checkBox is null)) return;
        _syncingModuleChecks = true;
        try
        {
            foreach (var checkBox in ModuleChecks()) checkBox.IsChecked = AllModulesCheck.IsChecked == true;
        }
        finally
        {
            _syncingModuleChecks = false;
        }
    }

    private void ModuleCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_syncingModuleChecks || AllModulesCheck is null || ModuleChecks().Any(checkBox => checkBox is null)) return;
        _syncingModuleChecks = true;
        try
        {
            AllModulesCheck.IsChecked = ModuleChecks().All(checkBox => checkBox.IsChecked == true);
        }
        finally
        {
            _syncingModuleChecks = false;
        }
    }

    private string[] SelectedModuleIds()
    {
        if (AllModulesCheck.IsChecked == true) return ["module.all"];
        return ModuleChecks()
            .Where(checkBox => checkBox.IsChecked == true)
            .Select(checkBox => checkBox.Tag?.ToString())
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Cast<string>()
            .ToArray();
    }

    private void Generate_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var request = ParseRequest(ActivationRequestText.Text);
            var validity = (ValidityCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "year";
            var now = DateTime.UtcNow;
            var modules = SelectedModuleIds();
            var payload = new LicensePayload(
                $"LIC-{now:yyyyMMdd-HHmmss}-{RandomNumberGenerator.GetInt32(100000, 999999)}",
                "客户",
                "VisionWorkbench",
                "Professional",
                now,
                CalculateExpiry(now, validity),
                request.MachineFingerprint,
                modules,
                Modules: modules);

            Directory.CreateDirectory(_issuerDataDirectory);
            var privateKeyPath = Path.Combine(_issuerDataDirectory, "license-signing-private.pem");
            using var ecdsa = LoadOrCreateSigningKey(privateKeyPath);
            var payloadJson = JsonSerializer.Serialize(payload, JsonOptions);
            var document = new LicenseDocument(
                payload,
                Convert.ToBase64String(ecdsa.SignData(Encoding.UTF8.GetBytes(payloadJson), HashAlgorithmName.SHA256)));
            var output = JsonSerializer.Serialize(document, new JsonSerializerOptions(JsonOptions) { WriteIndented = true });
            var outputDirectory = Directory.Exists(@"D:\")
                ? DefaultOutputDirectory
                : Path.Combine(_issuerDataDirectory, "issued-licenses");
            Directory.CreateDirectory(outputDirectory);
            var outputPath = Path.Combine(outputDirectory, payload.LicenseId + ".vwlicense");
            File.WriteAllText(outputPath, output, new UTF8Encoding(false));
            StatusText.Text = $"许可证已生成：{outputPath}";
            ShowToast("许可证生成成功");
        }
        catch (Exception ex)
        {
            StatusText.Text = $"生成失败：{ex.Message}";
            ShowToast($"生成失败：{ex.Message}", isError: true);
        }
    }

    private static ActivationRequest ParseRequest(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidDataException("请先填入客户申请码");
        var json = Convert.FromBase64String(value.Trim());
        var request = JsonSerializer.Deserialize<ActivationRequest>(json, JsonOptions)
                      ?? throw new InvalidDataException("客户申请码为空");
        if (!string.Equals(request.Product, "VisionWorkbench", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("申请码产品不匹配");
        if (string.IsNullOrWhiteSpace(request.MachineFingerprint))
            throw new InvalidDataException("申请码缺少设备绑定信息");
        return request;
    }

    private static DateTime CalculateExpiry(DateTime start, string validity) => validity switch
    {
        "day" => start.AddDays(1),
        "week" => start.AddDays(7),
        "month" => start.AddMonths(1),
        "three-years" => start.AddYears(3),
        "ten-years" => start.AddYears(10),
        "permanent" => DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc),
        _ => start.AddYears(1),
    };

    private ECDsa LoadOrCreateSigningKey(string path)
    {
        var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        try
        {
            if (File.Exists(path))
            {
                ecdsa.ImportFromPem(File.ReadAllText(path));
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, ecdsa.ExportPkcs8PrivateKeyPem(), new UTF8Encoding(false));
            }
            File.WriteAllText(Path.Combine(_issuerDataDirectory, "license-public-key.txt"),
                Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo()), new UTF8Encoding(false));
            return ecdsa;
        }
        catch
        {
            ecdsa.Dispose();
            throw new InvalidDataException("签发私钥无法读取，请检查许可证工具数据目录");
        }
    }

    private void ShowToast(string message, bool isError = false)
    {
        ToastText.Text = message;
        ToastBorder.Background = (Brush)FindResource(isError ? "DangerBrush" : "AccentBrush");
        ToastBorder.Visibility = Visibility.Visible;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    private void GenerateKey_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "保存许可证签发私钥",
            Filter = "PEM 私钥 (*.pem)|*.pem|所有文件 (*.*)|*.*",
            FileName = "visionworkbench-license-signing-private.pem",
            OverwritePrompt = true,
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            File.WriteAllText(dialog.FileName, ecdsa.ExportPkcs8PrivateKeyPem(), new UTF8Encoding(false));
            PrivateKeyPathText.Text = dialog.FileName;
            PublicKeyText.Text = Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo());
            StatusText.Text = "密钥已生成。请妥善备份私钥，并将公钥配置到客户端。";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"生成密钥失败：{ex.Message}";
        }
    }

    private void ChoosePrivateKey_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择许可证签发私钥",
            Filter = "PEM 私钥 (*.pem;*.key)|*.pem;*.key|所有文件 (*.*)|*.*",
            CheckFileExists = true,
        };
        if (dialog.ShowDialog(this) != true) return;
        PrivateKeyPathText.Text = dialog.FileName;
        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportFromPem(File.ReadAllText(dialog.FileName));
            PublicKeyText.Text = Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo());
            StatusText.Text = "私钥已加载。";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"私钥加载失败：{ex.Message}";
        }
    }

    private void ParseRequest_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var raw = Convert.FromBase64String(ActivationRequestText.Text.Trim());
            var request = JsonSerializer.Deserialize<ActivationRequest>(raw, JsonOptions)
                          ?? throw new InvalidDataException("申请码为空");
            ProductText.Text = request.Product;
            MachineFingerprintText.Text = request.MachineFingerprint;
            StatusText.Text = $"申请码解析成功，申请编号：{request.RequestId}";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"申请码解析失败：{ex.Message}";
        }
    }

    private void ChooseOutput_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "保存许可证文件",
            Filter = "VisionWorkbench 许可证 (*.vwlicense;*.json)|*.vwlicense;*.json|所有文件 (*.*)|*.*",
            FileName = Path.GetFileName(OutputPathText.Text),
            OverwritePrompt = true,
        };
        if (dialog.ShowDialog(this) == true) OutputPathText.Text = dialog.FileName;
    }

    private void Issue_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var privateKeyPath = Require(PrivateKeyPathText.Text, "请选择签发私钥");
            var customer = Require(CustomerText.Text, "请输入客户名称");
            var licenseId = Require(LicenseIdText.Text, "请输入许可证编号");
            var fingerprint = Require(MachineFingerprintText.Text, "请粘贴客户申请码并解析，或填写设备码");
            var notBefore = ToUtc(NotBeforePicker.SelectedDate, "请选择生效日期");
            var expires = ToUtc(ExpiresPicker.SelectedDate, "请选择到期日期");
            if (expires <= notBefore) throw new InvalidDataException("到期日期必须晚于生效日期");
            var outputPath = Require(OutputPathText.Text, "请选择输出文件");
            var modules = SelectedModuleIds();
            var features = modules;
            var limits = ParseLimits(LimitsText.Text);

            using var ecdsa = ECDsa.Create();
            ecdsa.ImportFromPem(File.ReadAllText(privateKeyPath));
            var payload = new LicensePayload(
                licenseId,
                customer,
                string.IsNullOrWhiteSpace(ProductText.Text) ? "VisionWorkbench" : ProductText.Text.Trim(),
                string.IsNullOrWhiteSpace(EditionText.Text) ? "Professional" : EditionText.Text.Trim(),
                notBefore,
                expires,
                fingerprint.Trim(),
                features,
                limits.Count == 0 ? null : limits,
                modules);
            var payloadJson = JsonSerializer.Serialize(payload, JsonOptions);
            var document = new LicenseDocument(
                payload,
                Convert.ToBase64String(ecdsa.SignData(Encoding.UTF8.GetBytes(payloadJson), HashAlgorithmName.SHA256)));
            var fullOutputPath = Path.GetFullPath(outputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullOutputPath)!);
            File.WriteAllText(fullOutputPath, JsonSerializer.Serialize(document, new JsonSerializerOptions(JsonOptions) { WriteIndented = true }), new UTF8Encoding(false));
            PublicKeyText.Text = Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo());
            StatusText.Text = $"许可证签发成功：{fullOutputPath}";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"签发失败：{ex.Message}";
        }
    }

    private static string Require(string? value, string message) =>
        string.IsNullOrWhiteSpace(value) ? throw new InvalidDataException(message) : value.Trim();

    private static DateTime ToUtc(DateTime? value, string message) =>
        value is null ? throw new InvalidDataException(message) : DateTime.SpecifyKind(value.Value, DateTimeKind.Local).ToUniversalTime();

    private static Dictionary<string, int> ParseLimits(string value)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(value)) return result;
        foreach (var item in value.Split([';', '；', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = item.Split('=', 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || !int.TryParse(parts[1], out var limit) || limit < 0)
                throw new InvalidDataException($"数量限制格式无效：{item}");
            result[parts[0]] = limit;
        }
        return result;
    }

    private sealed record ActivationRequest(
        string Product,
        string MachineFingerprint,
        string RequestedAtUtc,
        string RequestId);

    private sealed record LicensePayload(
        string LicenseId,
        string Customer,
        string Product,
        string Edition,
        DateTime NotBeforeUtc,
        DateTime ExpiresAtUtc,
        string MachineFingerprint,
        IReadOnlyList<string> Features,
        IReadOnlyDictionary<string, int>? Limits = null,
        IReadOnlyList<string>? Modules = null);

    private sealed record LicenseDocument(
        LicensePayload Payload,
        string Signature,
        string SignatureAlgorithm = "ECDSA_P256_SHA256");
}
