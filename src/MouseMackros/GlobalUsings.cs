// Both WPF and WinForms are referenced (WinForms only for the tray icon),
// so a few type names are ambiguous. Resolve them in favour of WPF.
global using Application = System.Windows.Application;
global using Binding = MouseMackros.Model.Binding;
global using Brush = System.Windows.Media.Brush;
global using Brushes = System.Windows.Media.Brushes;
global using Button = System.Windows.Controls.Button;
global using ContextMenu = System.Windows.Controls.ContextMenu;
global using MenuItem = System.Windows.Controls.MenuItem;
global using KeyEventArgs = System.Windows.Input.KeyEventArgs;
global using MessageBox = System.Windows.MessageBox;
global using FontFamily = System.Windows.Media.FontFamily;
global using SolidColorBrush = System.Windows.Media.SolidColorBrush;
global using Trigger = MouseMackros.Input.Trigger;
global using HorizontalAlignment = System.Windows.HorizontalAlignment;
global using VerticalAlignment = System.Windows.VerticalAlignment;
global using Orientation = System.Windows.Controls.Orientation;
global using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
global using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

global using System.IO;
global using System.Linq;
