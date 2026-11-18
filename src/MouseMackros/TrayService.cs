using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using MouseMackros.Model;

namespace MouseMackros;

public sealed class TrayService : IDisposable
{
    private readonly App _app;
    private readonly NotifyIcon _icon;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _pauseItem;
    private readonly ToolStripMenuItem _profilesMenu;
    private readonly Icon _appIcon;

    public TrayService(App app)
    {
        _app = app;
        _appIcon = CreateIcon();

        _menu = new ContextMenuStrip { ShowImageMargin = false, Font = new Font("Segoe UI", 9f) };

        var open = new ToolStripMenuItem("Открыть MouseMackros");
        open.Click += (_, _) => _app.ShowMainWindow();

        _pauseItem = new ToolStripMenuItem("Пауза (Ctrl+Alt+P)");
        _pauseItem.Click += (_, _) => TogglePause();

        _profilesMenu = new ToolStripMenuItem("Профиль");

        var exit = new ToolStripMenuItem("Выход");
        exit.Click += (_, _) => _app.ExitApp();

        _menu.Items.Add(open);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(_profilesMenu);
        _menu.Items.Add(_pauseItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(exit);
        _menu.Opening += (_, _) => RebuildProfiles();

        _icon = new NotifyIcon
        {
            Icon = _appIcon,
            Visible = true,
            Text = "MouseMackros",
            ContextMenuStrip = _menu
        };
        _icon.DoubleClick += (_, _) => _app.ShowMainWindow();
    }

    private void TogglePause()
    {
        _app.Engine.TogglePause();
        UpdateTooltip();
    }

    public void ProfileChanged(Profile profile)
    {
        _icon.Text = Truncate("MouseMackros — " + profile.Name, 63);
        UpdateTooltip();
    }

    private void UpdateTooltip()
    {
        var state = _app.Engine.IsPaused ? "на паузе" : _app.Engine.ActiveProfile.Name;
        _icon.Text = Truncate($"MouseMackros — {state}", 63);
        _pauseItem.Text = _app.Engine.IsPaused ? "Продолжить (Ctrl+Alt+P)" : "Пауза (Ctrl+Alt+P)";
    }

    private void RebuildProfiles()
    {
        _profilesMenu.DropDownItems.Clear();
        foreach (var profile in _app.Config.Profiles)
        {
            var item = new ToolStripMenuItem(profile.Name)
            {
                Checked = ReferenceEquals(profile, _app.Engine.ActiveProfile)
            };
            item.Click += (_, _) => _app.Engine.SwitchProfile(profile.Id);
            _profilesMenu.DropDownItems.Add(item);
        }
        UpdateTooltip();
    }

    public void ShowInfo(string title, string text)
    {
        _icon.BalloonTipTitle = title;
        _icon.BalloonTipText = text;
        _icon.BalloonTipIcon = ToolTipIcon.None;
        _icon.ShowBalloonTip(2500);
    }

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..(max - 1)] + "…";

    private static Icon CreateIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            using var bg = new SolidBrush(Color.FromArgb(76, 154, 255));
            using var dark = new SolidBrush(Color.FromArgb(18, 20, 26));
            using var white = new SolidBrush(Color.White);

            // mouse body
            g.FillRoundedRectangle(bg, new RectangleF(9, 3, 14, 26), 7);
            // left/right split
            g.FillRoundedRectangle(dark, new RectangleF(15.4f, 3, 1.2f, 11), 0.6f);
            // scroll wheel
            g.FillRoundedRectangle(white, new RectangleF(14.2f, 6, 3.6f, 6), 1.8f);
            // side buttons
            g.FillRoundedRectangle(dark, new RectangleF(21f, 8f, 3.6f, 4f), 1.4f);
            g.FillRoundedRectangle(dark, new RectangleF(21f, 13f, 3.6f, 4f), 1.4f);
            // cable
            using var pen = new Pen(Color.FromArgb(200, 220, 245), 1.6f);
            g.DrawLine(pen, 16, 3, 16, 1);
        }

        var handle = bmp.GetHicon();
        var icon = (Icon)Icon.FromHandle(handle).Clone();
        NativeIcon.DestroyIcon(handle);
        return icon;
    }

    private static class NativeIcon
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern bool DestroyIcon(IntPtr handle);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _menu.Dispose();
        _appIcon.Dispose();
    }
}

internal static class DrawingExtensions
{
    public static void FillRoundedRectangle(this Graphics g, System.Drawing.Brush brush, RectangleF rect, float radius)
    {
        using var path = new GraphicsPath();
        float d = radius * 2;
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        g.FillPath(brush, path);
    }
}
