using System;
using System.Runtime.InteropServices;

class SetBright
{
    [STAThread]
    static void Main(string[] args)
    {
        int target = 50;
        if (args.Length > 0) int.TryParse(args[0], out target);
        target = Math.Max(0, Math.Min(255, target));

        var nv = LoadLibrary("nvapi64.dll");
        if (nv == IntPtr.Zero) { Console.WriteLine("FAIL: can't load nvapi64.dll"); return; }

        var query = GetProcAddress(nv, "nvapi_QueryInterface");
        var q = (QueryFn)Marshal.GetDelegateForFunctionPointer(query, typeof(QueryFn));

        var init = GetFn<StatusFn>(q, 0x0150E828);
        if (init() != 0) { Console.WriteLine("FAIL: Init"); return; }

        var enumDisp = GetFn<EnumDispFn>(q, 0x9ABDD40D);
        var getGPU = GetFn<GetGPUFn>(q, 0x34EF9506);
        var getOutId = GetFn<GetOutIdFn>(q, 0xD995937E);
        var i2cWrite = GetFn<I2CFn>(q, 0xE812EB07);

        uint disp = 0;
        if (enumDisp(0, ref disp) != 0) { Console.WriteLine("FAIL: EnumDisplay"); return; }

        uint[] gpus = new uint[64];
        uint cnt = 0;
        if (getGPU(disp, gpus, ref cnt) != 0 || cnt == 0) { Console.WriteLine("FAIL: GetGPU"); return; }

        uint outId = 0;
        if (getOutId(disp, ref outId) != 0) { Console.WriteLine("FAIL: GetOutputId"); return; }

        uint gpu = gpus[0];
        const int structSize = 48;
        uint structVer = (uint)structSize | (1u << 16);

        byte[] setData = { 0x84, 0x03, 0x10, 0x00, (byte)target, 0x00 };
        byte cs = 0x6E;
        for (int i = 0; i < setData.Length - 1; i++) cs ^= setData[i];
        setData[setData.Length - 1] = cs;

        IntPtr reg = Marshal.AllocHGlobal(1);
        Marshal.WriteByte(reg, 0);
        IntPtr data = Marshal.AllocHGlobal(setData.Length);
        Marshal.Copy(setData, 0, data, setData.Length);
        IntPtr info = Marshal.AllocHGlobal(structSize);
        var zeros = new byte[structSize];
        Marshal.Copy(zeros, 0, info, zeros.Length);
        Marshal.WriteInt32(info, 0, (int)structVer);
        Marshal.WriteInt32(info, 4, (int)outId);
        Marshal.WriteByte(info, 8, 1);
        Marshal.WriteByte(info, 9, 0x6E);
        Marshal.WriteIntPtr(info, 16, reg);
        Marshal.WriteInt32(info, 24, 1);
        Marshal.WriteIntPtr(info, 32, data);
        Marshal.WriteInt32(info, 40, setData.Length);
        Marshal.WriteInt32(info, 44, 27);

        int s = i2cWrite(gpu, info);
        Console.WriteLine($"Set brightness to {target}: {(s == 0 ? "OK" : $"FAIL ({s})")}");

        // Read back
        var i2cRead = GetFn<I2CFn>(q, 0x2FDE12C5);
        if (i2cRead != null)
        {
            byte[] qbytes = { 0x82, 0x01, 0x10, 0x00 };
            cs = 0x6E;
            for (int i = 0; i < qbytes.Length - 1; i++) cs ^= qbytes[i];
            qbytes[qbytes.Length - 1] = cs;

            Marshal.WriteByte(reg, 0);
            Marshal.Copy(qbytes, 0, data, qbytes.Length);

            IntPtr info2 = Marshal.AllocHGlobal(structSize);
            Marshal.Copy(zeros, 0, info2, zeros.Length);
            Marshal.WriteInt32(info2, 0, (int)structVer);
            Marshal.WriteInt32(info2, 4, (int)outId);
            Marshal.WriteByte(info2, 8, 1);
            Marshal.WriteByte(info2, 9, 0x6E);
            Marshal.WriteIntPtr(info2, 16, reg);
            Marshal.WriteInt32(info2, 24, 1);
            Marshal.WriteIntPtr(info2, 32, data);
            Marshal.WriteInt32(info2, 40, qbytes.Length);
            Marshal.WriteInt32(info2, 44, 27);

            s = i2cWrite(gpu, info2);
            if (s == 0)
            {
                byte[] readBuf = new byte[11];
                IntPtr readData = Marshal.AllocHGlobal(11);
                IntPtr info3 = Marshal.AllocHGlobal(structSize);
                Marshal.Copy(zeros, 0, info3, zeros.Length);
                Marshal.WriteInt32(info3, 0, (int)structVer);
                Marshal.WriteInt32(info3, 4, (int)outId);
                Marshal.WriteByte(info3, 8, 1);
                Marshal.WriteByte(info3, 9, 0x6F);
                Marshal.WriteIntPtr(info3, 16, reg);
                Marshal.WriteInt32(info3, 24, 1);
                Marshal.WriteIntPtr(info3, 32, readData);
                Marshal.WriteInt32(info3, 40, 11);
                Marshal.WriteInt32(info3, 44, 27);

                System.Threading.Thread.Sleep(100);
                if (i2cRead(gpu, info3) == 0)
                {
                    Marshal.Copy(readData, readBuf, 0, 11);
                    Console.Write("Read: ");
                    foreach (var b in readBuf) Console.Write($"{b:X2} ");
                    Console.WriteLine();
                    int cur = (readBuf[8] << 8) | readBuf[9];
                    int max = (readBuf[6] << 8) | readBuf[7];
                    Console.WriteLine($"Brightness: {cur}/{max}");
                }
                Marshal.FreeHGlobal(readData);
                Marshal.FreeHGlobal(info3);
            }
            Marshal.FreeHGlobal(info2);
        }

        Marshal.FreeHGlobal(reg);
        Marshal.FreeHGlobal(data);
        Marshal.FreeHGlobal(info);
    }

    static T GetFn<T>(QueryFn q, uint id) where T : class
    {
        var ptr = q(id);
        return ptr != IntPtr.Zero ? Marshal.GetDelegateForFunctionPointer(ptr, typeof(T)) as T : null;
    }

    [DllImport("kernel32", SetLastError = true)]
    static extern IntPtr LoadLibrary(string s);

    [DllImport("kernel32", SetLastError = true)]
    static extern IntPtr GetProcAddress(IntPtr h, string s);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate IntPtr QueryFn(uint id);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate int StatusFn();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate int EnumDispFn(uint n, ref uint h);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate int GetGPUFn(uint h, uint[] g, ref uint c);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate int GetOutIdFn(uint h, ref uint id);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate int I2CFn(uint gpu, IntPtr info);
}
