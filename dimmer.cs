using System;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;

using System.Windows.Forms;
using Microsoft.Win32;

namespace Dimmer;

static class Program
{
    static readonly System.Threading.Mutex _mutex = new System.Threading.Mutex(true, "Dimmer_" + Assembly.GetExecutingAssembly().GetName().Name);

    [STAThread]
    static void Main()
    {
        if (!_mutex.WaitOne(TimeSpan.Zero, true))
        {
            Application.Run(new AlreadyRunningForm());
            return;
        }
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        using var app = new DimmerApp();
        Application.Run();
    }
}

class DimmerApp : IDisposable
{
    readonly NotifyIcon _tray;
    readonly LowLevelMouseHook _hook;
    readonly ToolStripMenuItem _autostartItem;
    readonly NvDdc _nv = new NvDdc();
    BrightnessForm _osd;
    int _current = 100;
    int _maxBrightness = 100;

    public DimmerApp()
    {
        InitNv();
        SystemEvents.PowerModeChanged += OnPowerModeChanged;

        _tray = new NotifyIcon
        {
            Icon = CreateIcon(),
            Text = $"Dimmer — {_current}%",
            Visible = true,
        };

        var menu = new ContextMenuStrip();
        menu.Renderer = new DarkMenuRenderer();
        menu.ForeColor = Color.White;
        _autostartItem = new ToolStripMenuItem("Автозапуск", null, OnToggleAutostart);
        _autostartItem.Checked = IsAutostart();
        _autostartItem.ForeColor = Color.White;
        menu.Items.Add(_autostartItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Выход", null, (_, _) => Exit()).ForeColor = Color.White;

        _tray.ContextMenuStrip = menu;

        _hook = new LowLevelMouseHook();
        _hook.MouseWheel += OnMouseWheel;
        _hook.Start();
    }

    void InitNv()
    {
        _nv.Init();
        if (_nv.IsAvailable)
        {
            _current = _nv.GetBrightness();
            if (_current < 0 || _current > _maxBrightness) _current = 70;
            _maxBrightness = Math.Min(_nv.MaxBrightness, 100);
        }
    }

    void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
        {
            _nv.Reinit();
            InitNv();
        }
    }

    void Exit()
    {
        _hook.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        Application.Exit();
    }

    void OnMouseWheel(int delta)
    {
        if (Control.ModifierKeys != Keys.Alt) return;

        var step = 10;
        var next = delta > 0
            ? Math.Min(_maxBrightness, _current + step)
            : Math.Max(0, _current - step);

        if (next == _current) return;

        _current = next;
        if (_nv.IsAvailable) _nv.SetBrightness(_current);

        ShowBrightness(_current);
        _tray.Text = $"Dimmer — {_current}%";
    }

    void ShowBrightness(int level)
    {
        if (_osd == null || _osd.IsDisposed)
        {
            _osd = new BrightnessForm();
            _osd.Show();
        }
        _osd.ShowBrightness(level);
    }

    static bool IsAutostart()
    {
        var val = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")?.GetValue("Dimmer");
        return val is string s && s.Equals(Application.ExecutablePath, StringComparison.OrdinalIgnoreCase);
    }

    void OnToggleAutostart(object sender, EventArgs e)
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
        if (IsAutostart())
        {
            key?.DeleteValue("Dimmer", false);
            _autostartItem.Checked = false;
        }
        else
        {
            key?.SetValue("Dimmer", Application.ExecutablePath);
            _autostartItem.Checked = true;
        }
    }

    static Icon CreateIcon()
    {
        try { return Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
        catch { return SystemIcons.Application; }
    }

    public void Dispose()
    {
        _hook?.Dispose();
        _tray?.Dispose();
        _osd?.Dispose();
    }
}

class NvDdc
{
    IntPtr _lib;
    IntPtr _gpu;
    uint _outputId;
    uint _structVer;
    int _structSize;
    I2CWriteFn _i2cWrite;
    I2CReadFn _i2cRead;
    DateTime _lastI2c = DateTime.MinValue;
    int _pending = -1;
    System.Threading.Timer _flushTimer;

    public bool IsAvailable { get; private set; }
    public int MaxBrightness { get; private set; } = 100;

    public void Reinit()
    {
        IsAvailable = false;
        _pending = -1;
        if (_lib != IntPtr.Zero) { FreeLibrary(_lib); _lib = IntPtr.Zero; }
        Init();
    }

    public void Init()
    {
        try
        {
            _lib = LoadLibrary("nvapi64.dll");
            if (_lib == IntPtr.Zero) return;

            var queryAddr = GetProcAddress(_lib, "nvapi_QueryInterface");
            if (queryAddr == IntPtr.Zero) return;
            var q = (QueryInterfaceFn)Marshal.GetDelegateForFunctionPointer(queryAddr, typeof(QueryInterfaceFn));

            var init = GetFn<NvStatusFn>(q, 0x0150E828);
            if (init() != 0) return;

            var enumDisp = GetFn<EnumNvidiaDisplayHandleFn>(q, 0x9ABDD40D);
            var getGPU = GetFn<GetPhysicalGPUsFromDisplayFn>(q, 0x34EF9506);
            var getOutId = GetFn<GetAssociatedDisplayOutputIdFn>(q, 0xD995937E);
            _i2cWrite = GetFn<I2CWriteFn>(q, 0xE812EB07);
            _i2cRead = GetFn<I2CReadFn>(q, 0x2FDE12C5);

            if (_i2cWrite == null) return;

            uint dispHandle = 0;
            if (enumDisp(0, ref dispHandle) != 0) return;

            uint[] gpus = new uint[64];
            uint gpuCount = 0;
            if (getGPU(dispHandle, gpus, ref gpuCount) != 0 || gpuCount == 0) return;

            uint outId = 0;
            if (getOutId(dispHandle, ref outId) != 0) return;

            _gpu = (IntPtr)gpus[0];
            _outputId = outId;
            _structSize = 48;
            _structVer = (uint)_structSize | (1u << 16);

            IsAvailable = true;
        }
        catch { IsAvailable = false; }
    }

    public int GetBrightness()
    {
        if (!IsAvailable) return -1;

        IntPtr regPtr = IntPtr.Zero, dataPtr = IntPtr.Zero, infoPtr = IntPtr.Zero;
        IntPtr regRPtr = IntPtr.Zero, readPtr = IntPtr.Zero, infoRPtr = IntPtr.Zero;
        try
        {
            byte reg = 0;
            byte[] query = { 0x82, 0x01, 0x10, 0x00 };
            byte cs = (byte)(0x6E ^ reg);
            for (int i = 0; i < query.Length - 1; i++) cs ^= query[i];
            query[query.Length - 1] = cs;

            regPtr = Marshal.AllocHGlobal(1);
            Marshal.WriteByte(regPtr, reg);
            dataPtr = Marshal.AllocHGlobal(query.Length);
            Marshal.Copy(query, 0, dataPtr, query.Length);
            infoPtr = AllocI2cInfo(0x6E, regPtr, 1, dataPtr, (uint)query.Length);

            if (_i2cWrite((uint)_gpu, infoPtr) != 0) return -1;

            System.Threading.Thread.Sleep(50);

            byte[] readBuf = new byte[11];
            regRPtr = Marshal.AllocHGlobal(1);
            Marshal.WriteByte(regRPtr, reg);
            readPtr = Marshal.AllocHGlobal(readBuf.Length);
            infoRPtr = AllocI2cInfo(0x6F, regRPtr, 1, readPtr, (uint)readBuf.Length);

            if (_i2cRead((uint)_gpu, infoRPtr) != 0) return -1;

            Marshal.Copy(readPtr, readBuf, 0, readBuf.Length);
            if (readBuf.Length > 9 && readBuf[0] == 0x6E)
            {
                int cur = (readBuf[8] << 8) | readBuf[9];
                int max = (readBuf[6] << 8) | readBuf[7];
                if (max > 0 && max <= 255) MaxBrightness = max;
                if (cur >= 0 && cur <= MaxBrightness) return cur;
            }
            return -1;
        }
        finally
        {
            Free(ref regPtr); Free(ref dataPtr); Free(ref infoPtr);
            Free(ref regRPtr); Free(ref readPtr); Free(ref infoRPtr);
        }
    }

    public bool SetBrightness(int value)
    {
        if (!IsAvailable) return false;
        if (value < 0) value = 0;
        if (value > 255) value = 255;

        var now = DateTime.UtcNow;
        if ((now - _lastI2c).TotalMilliseconds >= 500)
        {
            WriteI2c(value);
        }
        else
        {
            _pending = value;
            if (_flushTimer == null)
                _flushTimer = new System.Threading.Timer(_ => FlushPending(), null, 500, System.Threading.Timeout.Infinite);
            else
                _flushTimer.Change(500, System.Threading.Timeout.Infinite);
        }
        return true;
    }

    void FlushPending()
    {
        var val = _pending;
        if (val < 0) return;
        _pending = -1;
        WriteI2c(val);
    }

    bool WriteI2c(int value)
    {
        _lastI2c = DateTime.UtcNow;
        IntPtr regPtr = IntPtr.Zero, dataPtr = IntPtr.Zero, infoPtr = IntPtr.Zero;
        try
        {
            byte reg = 0;
            byte[] setData = { 0x84, 0x03, 0x10, 0x00, (byte)value, 0x00 };
            byte cs = (byte)(0x6E ^ reg);
            for (int i = 0; i < setData.Length - 1; i++) cs ^= setData[i];
            setData[setData.Length - 1] = cs;

            regPtr = Marshal.AllocHGlobal(1);
            Marshal.WriteByte(regPtr, reg);
            dataPtr = Marshal.AllocHGlobal(setData.Length);
            Marshal.Copy(setData, 0, dataPtr, setData.Length);
            infoPtr = AllocI2cInfo(0x6E, regPtr, 1, dataPtr, (uint)setData.Length);

            return _i2cWrite((uint)_gpu, infoPtr) == 0;
        }
        finally
        {
            Free(ref regPtr); Free(ref dataPtr); Free(ref infoPtr);
        }
    }

    IntPtr AllocI2cInfo(byte devAddr, IntPtr regAddr, uint regSize, IntPtr data, uint dataSize)
    {
        IntPtr ptr = Marshal.AllocHGlobal(_structSize);
        var b = new byte[_structSize];
        Marshal.Copy(b, 0, ptr, b.Length);
        Marshal.WriteInt32(ptr, 0, (int)_structVer);
        Marshal.WriteInt32(ptr, 4, (int)_outputId);
        Marshal.WriteByte(ptr, 8, 1);
        Marshal.WriteByte(ptr, 9, devAddr);
        Marshal.WriteIntPtr(ptr, 16, regAddr);
        Marshal.WriteInt32(ptr, 24, (int)regSize);
        Marshal.WriteIntPtr(ptr, 32, data);
        Marshal.WriteInt32(ptr, 40, (int)dataSize);
        Marshal.WriteInt32(ptr, 44, 27);
        return ptr;
    }

    static void Free(ref IntPtr p) { if (p != IntPtr.Zero) { Marshal.FreeHGlobal(p); p = IntPtr.Zero; } }

    static T GetFn<T>(QueryInterfaceFn q, uint id) where T : class
    {
        var ptr = q(id);
        return ptr != IntPtr.Zero ? Marshal.GetDelegateForFunctionPointer(ptr, typeof(T)) as T : null;
    }

    [DllImport("kernel32", SetLastError = true)]
    static extern IntPtr LoadLibrary(string lpFileName);

    [DllImport("kernel32", SetLastError = true)]
    static extern bool FreeLibrary(IntPtr hModule);

    [DllImport("kernel32", SetLastError = true)]
    static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate IntPtr QueryInterfaceFn(uint id);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate int NvStatusFn();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate int EnumNvidiaDisplayHandleFn(uint thisEnum, ref uint handle);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate int GetPhysicalGPUsFromDisplayFn(uint displayHandle, uint[] gpuHandles, ref uint count);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate int GetAssociatedDisplayOutputIdFn(uint displayHandle, ref uint outputId);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate int I2CWriteFn(uint gpuHandle, IntPtr info);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate int I2CReadFn(uint gpuHandle, IntPtr info);
}

class AlreadyRunningForm : Form
{
    public AlreadyRunningForm()
    {
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(0x1E, 0x1E, 0x1E);
        ForeColor = Color.White;
        ClientSize = new Size(300, 120);
        Text = "Dimmer";
        MaximizeBox = false;
        MinimizeBox = false;

        var label = new Label
        {
            Text = "Dimmer уже запущен.",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 12),
            BackColor = Color.Transparent,
        };
        Controls.Add(label);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        var val = 1;
        DwmSetWindowAttribute(Handle, 20, ref val, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int cb);
}

class BrightnessForm : Form
{
    readonly Label _label;
    Timer _timer;

    public BrightnessForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.Black;
        Opacity = 0.85;
        Size = new Size(200, 80);

        _label = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 20, FontStyle.Bold),
            BackColor = Color.Transparent,
        };
        Controls.Add(_label);
    }

    public void ShowBrightness(int level)
    {
        _label.Text = $"{level}%";
        Opacity = 0.85;
        _timer?.Dispose();
        _timer = new Timer { Interval = 80 };
        _timer.Tick += (_, _) =>
        {
            Opacity -= 0.08;
            if (Opacity <= 0) { _timer.Stop(); Close(); }
        };
        _timer.Start();
        Show();
        BringToFront();
    }

    protected override void Dispose(bool disposing)
    {
        _timer?.Dispose();
        base.Dispose(disposing);
    }
}

class DarkMenuRenderer : ToolStripProfessionalRenderer
{
    public DarkMenuRenderer() : base(new DarkColorTable()) { }
}

class DarkColorTable : ProfessionalColorTable
{
    public override Color MenuItemSelected => Color.FromArgb(0x3E, 0x3E, 0x42);
    public override Color MenuItemSelectedGradientBegin => Color.FromArgb(0x3E, 0x3E, 0x42);
    public override Color MenuItemSelectedGradientEnd => Color.FromArgb(0x3E, 0x3E, 0x42);
    public override Color MenuBorder => Color.FromArgb(0x3E, 0x3E, 0x42);
    public override Color MenuItemBorder => Color.FromArgb(0x3E, 0x3E, 0x42);
    public override Color ToolStripDropDownBackground => Color.FromArgb(0x1E, 0x1E, 0x1E);
    public override Color ImageMarginGradientBegin => Color.FromArgb(0x1E, 0x1E, 0x1E);
    public override Color ImageMarginGradientMiddle => Color.FromArgb(0x1E, 0x1E, 0x1E);
    public override Color ImageMarginGradientEnd => Color.FromArgb(0x1E, 0x1E, 0x1E);
    public override Color SeparatorDark => Color.FromArgb(0x3E, 0x3E, 0x42);
    public override Color SeparatorLight => Color.FromArgb(0x3E, 0x3E, 0x42);
    public override Color CheckBackground => Color.FromArgb(0x3E, 0x3E, 0x42);
    public override Color CheckSelectedBackground => Color.FromArgb(0x5A, 0x5A, 0x5E);
}

class LowLevelMouseHook : IDisposable
{
    const int WH_MOUSE_LL = 14;
    const int WM_MOUSEWHEEL = 0x020A;

    delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    IntPtr _hookId = IntPtr.Zero;
    LowLevelMouseProc _proc;

    public event Action<int> MouseWheel;

    public void Start()
    {
        _proc = HookCallback;
        using var cur = Process.GetCurrentProcess();
        using var mod = cur.MainModule;
        _hookId = SetWindowsHookEx(WH_MOUSE_LL, _proc,
            GetModuleHandle(mod?.ModuleName), 0);
    }

    IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam == (IntPtr)WM_MOUSEWHEEL)
        {
            var ms = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            var delta = (short)((ms.mouseData >> 16) & 0xFFFF);
            MouseWheel?.Invoke(delta);
        }
        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hookId != IntPtr.Zero)
            UnhookWindowsHookEx(_hookId);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct POINT { public int x, y; }

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    static extern IntPtr GetModuleHandle(string lpModuleName);
}
