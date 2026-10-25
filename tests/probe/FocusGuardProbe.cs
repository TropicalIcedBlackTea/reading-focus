using System;
using System.Drawing;
using System.Windows.Forms;

// Disposable window for the sweep check. The checker minimizes only processes
// with this name, and the probe closes itself if the check never finishes.
internal static class FocusGuardProbe
{
    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        Form form = new Form { Text = "文献专注 · 检查窗口", Width = 360, Height = 150, StartPosition = FormStartPosition.CenterScreen };
        form.Controls.Add(new Label { Text = "专注保护检查使用的临时窗口", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter });
        Timer expiry = new Timer { Interval = 60000 };
        expiry.Tick += delegate { form.Close(); };
        expiry.Start();
        Application.Run(form);
    }
}
