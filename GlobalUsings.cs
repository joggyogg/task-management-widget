// Resolve WPF vs WinForms type ambiguities — we are a WPF app that only
// borrows NotifyIcon from WinForms, so WPF types always win.
global using Application    = System.Windows.Application;
global using Color          = System.Windows.Media.Color;
global using DragDropEffects = System.Windows.DragDropEffects;
global using DragEventArgs  = System.Windows.DragEventArgs;
global using MessageBox     = System.Windows.MessageBox;
global using MouseEventArgs = System.Windows.Input.MouseEventArgs;
global using Point          = System.Windows.Point;
global using UserControl    = System.Windows.Controls.UserControl;
