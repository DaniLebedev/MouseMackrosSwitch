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

    private static readonly object WheelGate = new();

    /// <summary>
    /// Sets the wheel/DPI indicator color. The vendor app sends 06 03 with
    /// eight RGB triples; the wheel uses the color of the active DPI level.
    /// We write the same color to all eight levels and all known profile slots.
    /// </summary>
    public static Result SetWheelColor(string hex)
    {
        var value = (hex ?? "").Trim().TrimStart('#');
        if (value.Length != 6 ||
            !int.TryParse(value, System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out int rgb))
        {
            return new Result(false, $"Некорректный цвет «{hex}». Нужен RRGGBB.");
        }

        byte r = (byte)((rgb >> 16) & 0xFF);
        byte g = (byte)((rgb >> 8) & 0xFF);
        byte b = (byte)(rgb & 0xFF);

        lock (WheelGate)
        {
            var handle = FindDevice(out string info);
            if (handle == IntPtr.Zero)
                return new Result(false, "Мышь не найдена по HID.\n\n" + info);

            try
            {
                // Captured from the vendor app: the DPI color table is written
                // to several profile slots; sending all of them makes the
                // indicator color independent of the active profile.
                byte[] sequences = { 0x03, 0x05, 0x06, 0x07, 0x19, 0x1B };
                int accepted = 0;

                foreach (byte sequence in sequences)
                {
                    var packet = new byte[32];
                    packet[0] = 0x06;
                    packet[1] = 0x03;
                    packet[2] = sequence;
                    packet[3] = 0x01;

                    for (int i = 0; i < 8; i++)
                    {
                        packet[5 + i * 3] = r;
                        packet[6 + i * 3] = g;
                        packet[7 + i * 3] = b;
                    }

                    int sum = 0;
                    for (int i = 0; i < packet.Length; i++)
                        if (i != 4) sum += packet[i];
                    packet[4] = (byte)(sum & 0xFF);

                    if (HidD_SetFeature(handle, packet, packet.Length))
                        accepted++;
                    Thread.Sleep(25);
                }

                return accepted > 0
                    ? new Result(true, $"Цвет колеса: #{r:X2}{g:X2}{b:X2}")
                    : new Result(false, "Устройство не приняло пакет цвета.");
            }
            catch (Exception ex)
            {
                return new Result(false, "Ошибка записи цвета: " + ex.Message);
            }
            finally
            {
                CloseHandle(handle);
            }
        }
    }

    /// <summary>Writes the neutral A..M layout (buttons 1..11,15,16).</summary>
    public static Result WriteNeutral()
    {
        return Write(NeutralValues());
    }

    /// <summary>
    /// Writes neutral keys into every vendor profile slot, so switching profiles
    /// in MBF1000PRO can no longer bring back a different button layout.
    /// </summary>
    public static Result WriteNeutralAllProfiles()
    {
        var handle = FindDevice(out string info);
        if (handle == IntPtr.Zero)
            return new Result(false,
                "Не удалось открыть конфигурационный HID-интерфейс мыши MBF1000PRO.\n\n" +
                "Нажмите любую кнопку мыши, чтобы разбудить её, и повторите. " +
                "Фирменную программу MBF1000PRO лучше закрыть.\n\n" + info);

        try
        {
            var values = NeutralValues();
            if (!BuildKeys(values, out var keys, out var error))
                return new Result(false, error);

            var packets = BuildPackets(keys);

            // Captured vendor "select profile" commands: profile 0, 1, 2.
            var selects = new[]
            {
                new byte[] { 0x06, 0x0A, 0x18, 0x01, 0x29, 0x00 },
                new byte[] { 0x06, 0x0A, 0x0A, 0x01, 0x1C, 0x01 },
                new byte[] { 0x06, 0x0A, 0x1B, 0x01, 0x2E, 0x02 }
            };

            foreach (var select in selects)
            {
                var report = new byte[32];
                Array.Copy(select, report, select.Length);
                HidD_SetFeature(handle, report, report.Length);
                Thread.Sleep(80);

                foreach (var packet in packets)
                {
                    if (!HidD_SetFeature(handle, packet, packet.Length))
                        return new Result(false, "Устройство не приняло пакет раскладки.");
                    Thread.Sleep(30);
                }
            }

            return new Result(true,
                "Нейтральная раскладка записана во все профили мыши.\n\n" + info);
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

    private static string[] NeutralValues()
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

        return values;
    }

    private static bool BuildKeys(string[] values, out byte[][] keys, out string error)
    {
        keys = new byte[16][];
        error = "";

        for (int i = 0; i < 16; i++)
        {
            var raw = (values[i] ?? "").Trim();
            if (raw.Length != 6)
            {
                error = $"Некорректное значение Key{i}: «{raw}».";
                return false;
            }

            try
            {
                keys[i] = Convert.FromHexString(raw);
            }
            catch
            {
                error = $"Некорректное значение Key{i}: «{raw}».";
                return false;
            }
        }

        return true;
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
            return new Result(false,
                "Не удалось открыть конфигурационный HID-интерфейс мыши MBF1000PRO.\n\n" +
                "Нажмите любую кнопку мыши, чтобы разбудить её, и повторите. " +
                "Фирменную программу MBF1000PRO лучше закрыть.\n\n" + info);

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

                    string probe = "—";
                    if (feature >= 32)
                    {
                        var probeBuffer = new byte[Math.Max(32, feature)];
                        probeBuffer[0] = 0x06;
                        probe = HidD_GetFeature(handle, probeBuffer, probeBuffer.Length)
                            ? Convert.ToHexString(probeBuffer, 0, Math.Min(2, probeBuffer.Length))
                            : "fail";
                    }

                    lines.Add($"{attributes.VendorId:X4}:{attributes.ProductId:X4} feature={feature} probe={probe} «{product}»");
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
            seen?.Add($"{attributes.VendorId:X4}:{attributes.ProductId:X4} f={caps.FeatureReportByteLength} «{productName}»");
            CloseHandle(candidate);
            return false;
        }

        // The config collection answers a 0x06 feature read. Do not require a
        // specific second byte: after a reboot the mouse returns 06 00, while
        // the earlier build expected 06 01 and wrongly reported "not found".
        int probeLength = Math.Max(32, (int)caps.FeatureReportByteLength);
        bool probeOk = false;
        for (int attempt = 0; attempt < 3 && !probeOk; attempt++)
        {
            var probe = new byte[probeLength];
            probe[0] = 0x06;
            probeOk = HidD_GetFeature(candidate, probe, probe.Length);
            if (!probeOk) Thread.Sleep(100);
        }

        seen?.Add($"{attributes.VendorId:X4}:{attributes.ProductId:X4} f={caps.FeatureReportByteLength} probe={(probeOk ? "ok" : "fail")} «{productName}»");

        if (!probeOk)
        {
            CloseHandle(candidate);
            return false;
        }

        handle = candidate;
        detail = $"VID={attributes.VendorId:X4} PID={attributes.ProductId:X4} feature={caps.FeatureReportByteLength} probe=ok product={productName}";
        return true;
    }
}
