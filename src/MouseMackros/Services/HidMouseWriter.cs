using System.Runtime.InteropServices;
using MouseMackros.Model;

namespace MouseMackros.Services;

/// <summary>
/// Writes the neutral Ctrl+Alt+Shift+A..M layout directly to the MBF1000PRO
/// config interface over HID. Tested on VID_04F3 / PID_026E: the vendor utility
/// sends two 32-byte feature reports with the 16 KeyN triples.
/// </summary>
public static class HidMouseWriter
{
    private const ushort MouseVendorId = 0x04F3;
    private const ushort MouseProductId = 0x026E;
    private const uint GENERIC_READ = 0x80000000;
    private const uint GENERIC_WRITE = 0x40000000;
    private const uint FILE_SHARE_READ = 0x01;
    private const uint FILE_SHARE_WRITE = 0x02;
    private const uint OPEN_EXISTING = 3;
    private const uint DIGCF_PRESENT = 0x02;
    private const uint DIGCF_DEVICEINTERFACE = 0x10;

    public sealed record Result(bool Success, string Message);

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVICE_INTERFACE_DATA
    {
        public int Size;
        public Guid InterfaceClassGuid;
        public int Flags;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HIDD_ATTRIBUTES
    {
        public int Size;
        public ushort VendorId;
        public ushort ProductId;
        public ushort VersionNumber;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HIDP_CAPS
    {
        public ushort Usage;
        public ushort UsagePage;
        public ushort InputReportByteLength;
        public ushort OutputReportByteLength;
        public ushort FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
    }

    [DllImport("hid.dll")] private static extern void HidD_GetHidGuid(out Guid guid);
    [DllImport("hid.dll")] private static extern bool HidD_GetAttributes(IntPtr handle, ref HIDD_ATTRIBUTES attributes);
    [DllImport("hid.dll")] private static extern bool HidD_GetPreparsedData(IntPtr handle, out IntPtr data);
    [DllImport("hid.dll")] private static extern bool HidD_FreePreparsedData(IntPtr data);
    [DllImport("hid.dll")] private static extern int HidP_GetCaps(IntPtr data, ref HIDP_CAPS caps);
    [DllImport("hid.dll")] private static extern bool HidD_GetFeature(IntPtr handle, byte[] buffer, int length);
    [DllImport("hid.dll")] private static extern bool HidD_SetFeature(IntPtr handle, byte[] buffer, int length);
    [DllImport("hid.dll", CharSet = CharSet.Unicode)]
    private static extern bool HidD_GetProductString(IntPtr handle, System.Text.StringBuilder buffer, int length);
    [DllImport("hid.dll", CharSet = CharSet.Unicode)]
    private static extern bool HidD_GetManufacturerString(IntPtr handle, System.Text.StringBuilder buffer, int length);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, IntPtr enumerator, IntPtr parent, uint flags);
    [DllImport("setupapi.dll")]
    private static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr info, ref Guid classGuid,
        uint index, ref SP_DEVICE_INTERFACE_DATA data);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SP_DEVICE_INTERFACE_DATA data,
        IntPtr detail, int detailSize, out int required, IntPtr info);
    [DllImport("setupapi.dll")]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Get_Device_Interface_List_SizeW(out int length, ref Guid interfaceClassGuid,
        string? deviceId, uint flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Get_Device_Interface_ListW(ref Guid interfaceClassGuid, string? deviceId,
        [Out] char[] buffer, int length, uint flags);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateFile(string name, uint access, uint share, IntPtr security,
        uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    /// <summary>Writes the neutral A..M layout (buttons 1..11,15,16).</summary>
    public static Result WriteNeutral()
    {
        var values = new string[16];
        values[0] = "100100";
        values[1] = "100200";
        values[2] = "100400";

        foreach (var button in MouseButtonMap.Editable)
        {
            if (button.Letter is char letter)
                values[button.KeyIndex] = PsfGenerator.EncodeCombo(0x07, PsfGenerator.LetterCode(letter));
        }

        return Write(values);
    }

    /// <summary>Writes the KeyN values from a .psf file (buttons only for now).</summary>
    public static Result WritePsf(string path)
    {
        var psf = PsfReader.Read(path);
        if (psf is null) return new Result(false, "Не удалось прочитать .psf.");

        var values = new string[16];
        for (int i = 0; i < 16; i++)
        {
            var key = psf.ForKeyIndex(i);
            values[i] = string.IsNullOrWhiteSpace(key?.RawValue) ? "000000" : key!.RawValue;
        }

        var macro = psf.Keys.FirstOrDefault(k => k.Action?.Kind == ActionKind.Macro);
        var result = Write(values);
        if (result.Success && macro is not null)
            return new Result(true, result.Message + "\n\nМакросы пока не записываются — только клавиши.");
        return result;
    }

    private static Result Write(string[] values)
    {
        var handle = FindDevice(out string info);
        if (handle == IntPtr.Zero)
            return new Result(false, "Мышь MBF1000PRO не найдена по HID.\n\n" + info);

        try
        {
            var key = new byte[16][];
            for (int i = 0; i < 16; i++)
            {
                var raw = (values[i] ?? "").Trim();
                if (raw.Length != 6)
                    return new Result(false, $"Некорректное значение Key{i}: «{raw}».");

                try { key[i] = Convert.FromHexString(raw); }
                catch { return new Result(false, $"Некорректное значение Key{i}: «{raw}»."); }
            }

            var packets = BuildPackets(key);
            foreach (var packet in packets)
            {
                if (!HidD_SetFeature(handle, packet, packet.Length))
                    return new Result(false, "HID-отчёт отправлен, но устройство его не приняло.");
            }

            return new Result(true, "Нейтральная раскладка записана в мышь.\n\n" + info);
        }
        catch (Exception ex)
        {
            return new Result(false, "Ошибка записи в HID: " + ex.Message);
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    /// <summary>Two vendor-shaped reports with Key0..8 and Key9..15.</summary>
    internal static byte[][] BuildPackets(byte[][] keys)
    {
        static byte[] Packet(byte sequence, byte marker, byte check, byte[][] entries, int from, int to, int total)
        {
            var packet = new byte[total];
            packet[0] = 0x06;
            packet[1] = 0x01;
            packet[2] = sequence;
            packet[3] = marker;
            packet[4] = check;

            int offset = 5;
            for (int i = from; i <= to; i++)
            {
                packet[offset++] = entries[i][0];
                packet[offset++] = entries[i][1];
                packet[offset++] = entries[i][2];
            }
            return packet;
        }

        return new[]
        {
            Packet(0x03, 0x02, 0x2D, keys, 0, 8, 32),
            Packet(0x04, 0x12, 0x19, keys, 9, 15, 32)
        };
    }

    /// <summary>Lists HID collections that look like the mouse config channel.</summary>
    public static string ListDevices()
    {
        var lines = new List<string>();
        HidD_GetHidGuid(out var guid);

        try
        {
            int status = CM_Get_Device_Interface_List_SizeW(out int length, ref guid, null, 0);
            if (status != 0 || length <= 1) return "CfgMgr32 не вернул список HID.";

            var buffer = new char[length];
            status = CM_Get_Device_Interface_ListW(ref guid, null, buffer, buffer.Length, 0);
            if (status != 0) return "CfgMgr32: status=" + status;

            foreach (var path in new string(buffer).Split('\0', StringSplitOptions.RemoveEmptyEntries))
            {
                var handle = CreateFile(path, GENERIC_READ | GENERIC_WRITE,
                    FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
                if (handle == new IntPtr(-1))
                {
                    lines.Add("нет доступа: " + Path.GetFileName(path));
                    continue;
                }

                try
                {
                    var attributes = new HIDD_ATTRIBUTES { Size = Marshal.SizeOf<HIDD_ATTRIBUTES>() };
                    if (!HidD_GetAttributes(handle, ref attributes)) continue;

                    var product = new System.Text.StringBuilder(256);
                    HidD_GetProductString(handle, product, product.Capacity);

                    int feature = 0;
                    if (HidD_GetPreparsedData(handle, out var preparsed))
                    {
                        var caps = new HIDP_CAPS { Reserved = new ushort[17] };
                        HidP_GetCaps(preparsed, ref caps);
                        feature = caps.FeatureReportByteLength;
                        HidD_FreePreparsedData(preparsed);
                    }

                    bool ack = false;
                    if (feature >= 32)
                    {
                        var probe = new byte[32];
                        probe[0] = 0x06;
                        ack = HidD_GetFeature(handle, probe, probe.Length) && probe[1] == 0x01;
                    }

                    lines.Add($"{attributes.VendorId:X4}:{attributes.ProductId:X4} feature={feature} ack={ack} «{product}»");
                }
                finally
                {
                    CloseHandle(handle);
                }
            }
        }
        catch (Exception ex)
        {
            lines.Add("ошибка: " + ex.Message);
        }

        return lines.Count == 0 ? "HID-устройства не найдены." : string.Join("\n", lines);
    }

    private static IntPtr FindDevice(out string info)
    {
        info = "Проверьте подключение по USB или ресивер.";
        var seen = new List<string>();
        HidD_GetHidGuid(out var guid);

        // CfgMgr32 lists every HID top-level collection, including the vendor
        // config collection that SetupAPI sometimes skips.
        try
        {
            int status = CM_Get_Device_Interface_List_SizeW(out int length, ref guid, null, 0);
            if (status == 0 && length > 1)
            {
                var buffer = new char[length];
                status = CM_Get_Device_Interface_ListW(ref guid, null, buffer, buffer.Length, 0);
                if (status == 0)
                {
                    foreach (var path in new string(buffer).Split('\0', StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (TryOpenCandidate(path, out var handle, out var detail, seen))
                        {
                            info = path + "\n" + detail;
                            return handle;
                        }
                    }
                }
            }
        }
        catch
        {
            // fall back to SetupAPI
        }

        var set = SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
        if (set == IntPtr.Zero)
        {
            info = "SetupDiGetClassDevs не сработал.";
            return IntPtr.Zero;
        }

        try
        {
            for (uint index = 0; ; index++)
            {
                var data = new SP_DEVICE_INTERFACE_DATA { Size = Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>() };
                if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, index, ref data)) break;

                if (!SetupDiGetDeviceInterfaceDetail(set, ref data, IntPtr.Zero, 0, out int required, IntPtr.Zero))
                    continue;

                var detail = Marshal.AllocHGlobal(required);
                try
                {
                    Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                    if (!SetupDiGetDeviceInterfaceDetail(set, ref data, detail, required, out _, IntPtr.Zero))
                        continue;

                    var path = Marshal.PtrToStringUni(detail + 4);
                    if (string.IsNullOrWhiteSpace(path)) continue;

                    if (TryOpenCandidate(path, out var handle, out _, seen))
                    {
                        info = path;
                        return handle;
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(detail);
                }
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(set);
        }

        if (seen.Count > 0)
            info = string.Join("\n", seen.Take(12));

        return IntPtr.Zero;
    }

    private static bool TryOpenCandidate(string path, out IntPtr handle, out string detail,
        List<string>? seen = null)
    {
        handle = IntPtr.Zero;
        detail = "";

        var candidate = CreateFile(path, GENERIC_READ | GENERIC_WRITE,
            FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        if (candidate == new IntPtr(-1))
        {
            seen?.Add("нет доступа: " + Path.GetFileName(path));
            return false;
        }

        var attributes = new HIDD_ATTRIBUTES { Size = Marshal.SizeOf<HIDD_ATTRIBUTES>() };
        if (!HidD_GetAttributes(candidate, ref attributes))
        {
            CloseHandle(candidate);
            return false;
        }

        bool known = (attributes.VendorId == 0x04F3 && attributes.ProductId == 0x026E) ||
                     (attributes.VendorId == 0x04F3 && attributes.ProductId == 0x026F) ||
                     (attributes.VendorId == 0x1A2C && attributes.ProductId == 0x4C5E);

        var product = new System.Text.StringBuilder(256);
        HidD_GetProductString(candidate, product, product.Capacity);
        var productName = product.ToString();

        if (!HidD_GetPreparsedData(candidate, out var preparsed))
        {
            CloseHandle(candidate);
            return false;
        }

        var caps = new HIDP_CAPS { Reserved = new ushort[17] };
        HidP_GetCaps(preparsed, ref caps);
        HidD_FreePreparsedData(preparsed);

        seen?.Add($"{attributes.VendorId:X4}:{attributes.ProductId:X4} f={caps.FeatureReportByteLength} «{productName}»");

        bool named = productName.Contains("MBF", StringComparison.OrdinalIgnoreCase) ||
                     productName.Contains("Gaming", StringComparison.OrdinalIgnoreCase) ||
                     productName.Contains("Mouse", StringComparison.OrdinalIgnoreCase) ||
                     productName.Contains("MouseMackros", StringComparison.OrdinalIgnoreCase);

        if (!known && !named)
        {
            CloseHandle(candidate);
            return false;
        }

        if (caps.FeatureReportByteLength < 32)
        {
            CloseHandle(candidate);
            return false;
        }

        var probe = new byte[32];
        probe[0] = 0x06;
        if (!HidD_GetFeature(candidate, probe, probe.Length) || probe[1] != 0x01)
        {
            CloseHandle(candidate);
            return false;
        }

        handle = candidate;
        detail = $"VID={attributes.VendorId:X4} PID={attributes.ProductId:X4} feature={caps.FeatureReportByteLength} product={productName}";
        return true;
    }
}
