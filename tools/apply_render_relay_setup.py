#!/usr/bin/env python3
from pathlib import Path
import sys


def replace_required(text: str, old: str, new: str, label: str) -> str:
    if old not in text:
        raise RuntimeError(f"Render setup UI patch failed: {label}")
    return text.replace(old, new, 1)


def main() -> None:
    root = Path(sys.argv[1] if len(sys.argv) > 1 else ".").resolve()
    path = root / "VoiceCraft.Server.Android" / "ModernMainActivity.cs"
    text = path.read_text(encoding="utf-8")

    fields_anchor = "    private TextView? _configPreview;\n"
    fields = fields_anchor + '''    private EditText? _renderApiKey;
    private Spinner? _renderWorkspace;
    private EditText? _renderServiceName;
    private Spinner? _renderRegion;
    private Spinner? _renderPlan;
    private TextView? _renderProvisionStatus;
    private Button? _renderConnect;
    private Button? _renderCreate;
    private IReadOnlyList<RenderWorkspaceOption> _renderWorkspaceOptions = Array.Empty<RenderWorkspaceOption>();
    private string _renderCreatedServiceId = string.Empty;
    private string _renderCreatedServiceUrl = string.Empty;
'''
    text = replace_required(text, fields_anchor, fields, "Render UI fields")

    bridge_anchor = '''        body.AddView(relay, CardLayout());

        var identity = Card();
'''
    render_card = '''        body.AddView(relay, CardLayout());

        var renderCreate = Card();
        renderCreate.AddView(SectionTitle(T("สร้าง Render Relay", "Create Render Relay"), Primary));
        renderCreate.AddView(Label(
            T("สร้าง Web Service สำหรับ VoiceCraft Relay จากในแอป โดย API Key ใช้เฉพาะ session นี้และจะไม่ถูกบันทึก", "Create the VoiceCraft Relay Web Service from the app. The API key is used only for this session and is never saved."),
            11,
            Muted));

        renderCreate.AddView(InputLabel("Render API Key"));
        _renderApiKey = Input(string.Empty, InputTypes.ClassText | InputTypes.TextVariationPassword);
        _renderApiKey.Hint = "rnd_...";
        renderCreate.AddView(_renderApiKey);

        _renderConnect = MakeButton(T("เชื่อมต่อ Render", "CONNECT TO RENDER"), primary: true);
        WireButton(_renderConnect, ConnectRenderAccount);
        var renderConnectRow = ButtonRow();
        renderConnectRow.AddView(_renderConnect, new LinearLayout.LayoutParams(0, Dp(48), 1f) { LeftMargin = Dp(3), RightMargin = Dp(3) });
        renderCreate.AddView(renderConnectRow);

        renderCreate.AddView(InputLabel(T("Workspace", "Workspace")));
        _renderWorkspace = new Spinner(this) { Background = Round(SurfaceSoft, 14, Border) };
        _renderWorkspace.SetPadding(Dp(10), 0, Dp(10), 0);
        SetRenderSpinnerItems(_renderWorkspace, new[] { T("เชื่อมต่อ Render ก่อน", "Connect to Render first") });
        renderCreate.AddView(_renderWorkspace, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(52)));

        renderCreate.AddView(InputLabel(T("ชื่อ Service", "Service Name")));
        _renderServiceName = Input("voicecraft-relay-" + Guid.NewGuid().ToString("N")[..6], InputTypes.ClassText);
        _renderServiceName.Hint = "voicecraft-relay-name";
        renderCreate.AddView(_renderServiceName);

        renderCreate.AddView(InputLabel(T("Region", "Region")));
        _renderRegion = new Spinner(this) { Background = Round(SurfaceSoft, 14, Border) };
        _renderRegion.SetPadding(Dp(10), 0, Dp(10), 0);
        SetRenderSpinnerItems(_renderRegion, new[] { "Singapore", "Oregon", "Frankfurt", "Ohio", "Virginia" });
        renderCreate.AddView(_renderRegion, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(52)));

        renderCreate.AddView(InputLabel(T("Plan", "Plan")));
        _renderPlan = new Spinner(this) { Background = Round(SurfaceSoft, 14, Border) };
        _renderPlan.SetPadding(Dp(10), 0, Dp(10), 0);
        SetRenderSpinnerItems(_renderPlan, new[] { "Free" });
        renderCreate.AddView(_renderPlan, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(52)));

        _renderCreate = MakeButton(T("สร้าง Web Service", "CREATE WEB SERVICE"), primary: true);
        WireButton(_renderCreate, CreateRenderRelayService);
        _renderCreate.Enabled = false;
        _renderCreate.Alpha = 0.45f;
        var renderCreateRow = ButtonRow();
        renderCreateRow.AddView(_renderCreate, new LinearLayout.LayoutParams(0, Dp(48), 1f) { LeftMargin = Dp(3), RightMargin = Dp(3) });
        renderCreate.AddView(renderCreateRow);

        _renderProvisionStatus = Label(
            T("ใส่ API Key แล้วกดเชื่อมต่อเพื่อเลือก Workspace", "Enter an API key and connect to load your workspaces"),
            10,
            Muted);
        renderCreate.AddView(_renderProvisionStatus, Top(Dp(8)));
        body.AddView(renderCreate, CardLayout());

        var identity = Card();
'''
    text = replace_required(text, bridge_anchor, render_card, "Render provisioning card")

    methods_anchor = "    private void ShowOpenSourceLegal()\n"
    if methods_anchor not in text:
        methods_anchor = "    private void ShowInformation()\n"

    methods = r'''    private void SetRenderSpinnerItems(Spinner spinner, IEnumerable<string> items)
    {
        var values = items.ToArray();
        var adapter = new ArrayAdapter<string>(this, global::Android.Resource.Layout.SimpleSpinnerItem, values);
        adapter.SetDropDownViewResource(global::Android.Resource.Layout.SimpleSpinnerDropDownItem);
        spinner.Adapter = adapter;
    }

    private void SetRenderProvisionStatus(string text, Color color)
    {
        if (_renderProvisionStatus == null)
            return;
        _renderProvisionStatus.Text = text;
        _renderProvisionStatus.SetTextColor(color);
    }

    private void SetRenderCreateEnabled(bool enabled)
    {
        if (_renderCreate == null)
            return;
        _renderCreate.Enabled = enabled;
        _renderCreate.Alpha = enabled ? 1f : 0.45f;
    }

    private async void ConnectRenderAccount()
    {
        var apiKey = _renderApiKey?.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            SetRenderProvisionStatus(T("กรอก Render API Key ก่อน", "Enter a Render API key first"), Red);
            return;
        }

        if (_renderConnect != null)
        {
            _renderConnect.Enabled = false;
            _renderConnect.Alpha = 0.55f;
        }
        SetRenderCreateEnabled(false);
        SetRenderProvisionStatus(T("กำลังตรวจสอบบัญชี Render…", "Checking your Render account…"), Amber);

        try
        {
            var workspaces = await RenderApiClient.ListWorkspacesAsync(apiKey);
            _renderWorkspaceOptions = workspaces;
            if (_renderWorkspace != null)
            {
                var labels = workspaces.Select(workspace => string.IsNullOrWhiteSpace(workspace.Email)
                    ? workspace.Name
                    : $"{workspace.Name} • {workspace.Email}").ToArray();
                SetRenderSpinnerItems(_renderWorkspace, labels);
            }
            SetRenderCreateEnabled(workspaces.Count > 0);
            SetRenderProvisionStatus(
                T($"เชื่อมต่อ Render สำเร็จ • พบ {workspaces.Count} Workspace", $"Connected to Render • {workspaces.Count} workspace(s) found"),
                Green);
            AndroidRuntimeLog.Append("RENDER", $"Render account connected; workspaces={workspaces.Count}; API key hidden");
        }
        catch (RenderApiException ex)
        {
            _renderWorkspaceOptions = Array.Empty<RenderWorkspaceOption>();
            if (_renderWorkspace != null)
                SetRenderSpinnerItems(_renderWorkspace, new[] { T("ยังไม่มี Workspace", "No workspace loaded") });
            SetRenderProvisionStatus(ex.Message, Red);
            AndroidRuntimeLog.Append("RENDER", $"Render account connection failed: {ex.Message}; API key hidden");
        }
        catch (Exception ex)
        {
            _renderWorkspaceOptions = Array.Empty<RenderWorkspaceOption>();
            SetRenderProvisionStatus(T("เชื่อมต่อ Render ไม่สำเร็จ", "Unable to connect to Render"), Red);
            AndroidRuntimeLog.Append("RENDER", $"Render account connection failed: {ex.GetType().Name}; API key hidden");
        }
        finally
        {
            if (_renderConnect != null)
            {
                _renderConnect.Enabled = true;
                _renderConnect.Alpha = 1f;
            }
        }
    }

    private async void CreateRenderRelayService()
    {
        var apiKey = _renderApiKey?.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            SetRenderProvisionStatus(T("Render API Key หมดจาก session แล้ว กรุณาเชื่อมต่อใหม่", "Render API key is no longer available. Connect again."), Red);
            return;
        }

        var workspaceIndex = _renderWorkspace?.SelectedItemPosition ?? -1;
        if (workspaceIndex < 0 || workspaceIndex >= _renderWorkspaceOptions.Count)
        {
            SetRenderProvisionStatus(T("เลือก Workspace ก่อน", "Select a workspace first"), Red);
            return;
        }

        var serviceName = _renderServiceName?.Text?.Trim() ?? string.Empty;
        var region = _renderRegion?.SelectedItem?.ToString() ?? "Singapore";
        var plan = _renderPlan?.SelectedItem?.ToString() ?? "Free";
        var secret = _bridgeSecret?.Text?.Trim() ?? string.Empty;
        if (secret.Length < 16)
        {
            secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
            if (_bridgeSecret != null)
                _bridgeSecret.Text = secret;
            RefreshBridgePreview();
        }

        SetRenderCreateEnabled(false);
        if (_renderConnect != null)
            _renderConnect.Enabled = false;
        SetRenderProvisionStatus(T("กำลังสร้าง Render Web Service…", "Creating Render Web Service…"), Amber);

        try
        {
            var workspace = _renderWorkspaceOptions[workspaceIndex];
            var created = await RenderApiClient.CreateRelayServiceAsync(
                apiKey,
                workspace.Id,
                serviceName,
                region,
                plan,
                secret);
            _renderCreatedServiceId = created.Id;
            _renderCreatedServiceUrl = created.Url;
            SetRenderProvisionStatus(
                T("สร้าง Web Service สำเร็จ • Render เริ่ม deploy ครั้งแรกแล้ว", "Web Service created • Render started the first deploy"),
                Green);
            AndroidRuntimeLog.Append("RENDER", $"Relay service created id={created.Id} name={created.Name}; Bridge Secret hidden");
        }
        catch (RenderApiException ex)
        {
            SetRenderCreateEnabled(true);
            SetRenderProvisionStatus(ex.Message, Red);
            AndroidRuntimeLog.Append("RENDER", $"Relay service creation failed: {ex.Message}; secrets hidden");
        }
        catch (Exception ex)
        {
            SetRenderCreateEnabled(true);
            SetRenderProvisionStatus(T("สร้าง Render Web Service ไม่สำเร็จ", "Unable to create Render Web Service"), Red);
            AndroidRuntimeLog.Append("RENDER", $"Relay service creation failed: {ex.GetType().Name}; secrets hidden");
        }
        finally
        {
            if (_renderConnect != null)
                _renderConnect.Enabled = true;
        }
    }

'''
    text = replace_required(text, methods_anchor, methods + methods_anchor, "Render provisioning methods")

    path.write_text(text, encoding="utf-8")
    final = path.read_text(encoding="utf-8")
    required = [
        "private EditText? _renderApiKey;",
        "CONNECT TO RENDER",
        "CREATE WEB SERVICE",
        "RenderApiClient.ListWorkspacesAsync(apiKey)",
        "RenderApiClient.CreateRelayServiceAsync(",
        "Bridge Secret hidden",
    ]
    missing = [value for value in required if value not in final]
    if missing:
        raise RuntimeError(f"Render setup UI validation failed: {missing}")

    print(f"Applied Render Relay provisioning UI to {path}")
    print("- session-only API key and workspace discovery")
    print("- service name, region and plan controls")
    print("- create Web Service action with automatic Bridge Secret generation")


if __name__ == "__main__":
    main()
