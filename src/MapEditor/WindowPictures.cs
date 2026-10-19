using System.Drawing.Imaging;
using MapCore;

namespace MapEditor;

/// <summary>
/// <c>MapEditor --window-pictures &lt;archive&gt; &lt;out folder&gt; [WxH]</c>: opens a map in the editor's window without
/// showing it and draws the whole window, once for each tool tab, to window_&lt;tab&gt;.png: to see the editor's look
/// without opening it. Uses a library of its own (made and deleted), as the self-test does.
/// </summary>
static class WindowPictures
{
    public static int Run(string archive, string outDir, string? size)
    {
        Directory.CreateDirectory(outDir);
        var (width, height) = (1600, 1000);
        if (size?.Split('x') is [var w, var h] && int.TryParse(w, out int pw) && int.TryParse(h, out int ph))
            (width, height) = (pw, ph);
        var assetRoot = FileSystemUtil.NewTempDir("window-pictures-assets");
        int result = 1;
        using var form = new MainForm { Quiet = true, AssetRoot = assetRoot };
        _ = form.Handle;
        form.BeginInvoke(new Action(async () =>
        {
            try
            {
                await form.OpenAsync(Path.GetFullPath(archive));
                form.Size = new Size(width, height);
                form.PerformLayout();
                // The browser's thumbnails drawn, not "…": those in view come first; BAR's 2,000-odd entries needn't all be.
                await Task.WhenAny(form.AssetBrowser.ThumbnailsDone, Task.Delay(TimeSpan.FromSeconds(20)));
                form.ShowBeautifyPanel(true, select: false); // its tab too (Quiet: the user's choice isn't changed)
                foreach (var tab in form.ToolTabs.TabPages.Cast<TabPage>().Select(p => p.Text).ToList())
                {
                    form.ShowTab(tab);
                    Picture(form, Path.Combine(outDir, $"window_{tab.ToLowerInvariant()}.png"));
                }
                // fonts.txt: each control's DPI and font, to see that the fonts follow the window's when Windows
                // rescales it (a hidden window can be made at a DPI other than the system's).
                var lines = new List<string>();
                void Note(Control c, int depth)
                {
                    if (depth > 6)
                        return;
                    lines.Add($"{new string(' ', depth * 2)}{c.GetType().Name} '{c.Text}' dpi {c.DeviceDpi} font {c.Font.Name} {c.Font.SizeInPoints}pt {c.Font.Height}px");
                    foreach (Control child in c.Controls)
                        Note(child, depth + 1);
                }
                Note(form, 0);
                var tabs = form.ToolTabs;
                lines.Add($"tool tabs: {tabs.Width} wide, {tabs.TabCount} tabs, item size {tabs.ItemSize}, tabs at " +
                          string.Join(", ", Enumerable.Range(0, tabs.TabCount).Select(i => tabs.GetTabRect(i)).Select(r => $"{r.X}+{r.Width}")));
                File.WriteAllLines(Path.Combine(outDir, "fonts.txt"), lines);
                // Stop the thumbnails still being drawn into the library (BAR's, past the wait above) before it is deleted.
                form.AssetBrowser.Source = AssetSource.Maps;
                await form.AssetBrowser.ThumbnailsDone;
                form.CloseMap();
                result = 0;
            }
            catch (Exception ex)
            {
                File.WriteAllText(Path.Combine(outDir, "window_pictures.error.txt"), ex.ToString());
            }
            finally
            {
                Application.ExitThread();
            }
        }));
        Application.Run();
        // A drawing stopped mid-way may still be writing its file: try again a moment later.
        for (int attempt = 0; attempt < 5 && Directory.Exists(assetRoot); attempt++)
        {
            try
            {
                FileSystemUtil.DeleteTree(assetRoot);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(300);
            }
        }
        return result;
    }

    static void CreateHandles(Control control)
    {
        _ = control.Handle;
        foreach (Control child in control.Controls)
            CreateHandles(child);
    }

    /// <summary>
    /// The whole window, title bar and all, as it would look. Drawn hidden (printing draws only controls with windows),
    /// and put together from its parts: printing a window leaves out what its split containers hold, so each part
    /// outside them is printed on its own and laid where it is.
    /// </summary>
    static void Picture(Form form, string file)
    {
        CreateHandles(form);
        form.PerformLayout();
        using var bitmap = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
        // The client area inside the frame: the borders are even, the title bar takes the rest at the top.
        int border = (form.Width - form.ClientSize.Width) / 2;
        var origin = new Point(border, form.Height - form.ClientSize.Height - border);
        using (var g = Graphics.FromImage(bitmap))
            foreach (Control child in form.Controls)
                Lay(child, g, form, origin);
        bitmap.Save(file, ImageFormat.Png);
    }

    static void Lay(Control control, Graphics g, Form form, Point origin)
    {
        if (control is SplitContainer split) // its panels, unless collapsed (in a hidden window every control reads as hidden)
        {
            if (!split.Panel1Collapsed)
                Lay(split.Panel1, g, form, origin);
            if (!split.Panel2Collapsed)
                Lay(split.Panel2, g, form, origin);
            return;
        }
        if (control is SplitterPanel)
        {
            foreach (Control child in control.Controls)
                Lay(child, g, form, origin);
            return;
        }
        if (control.Width <= 0 || control.Height <= 0)
            return;
        // Where it is in the window's client area: its place in each parent, added up (a hidden window's screen
        // positions can't be trusted).
        var at = control.Location;
        for (var parent = control.Parent; parent != null && parent != form; parent = parent.Parent)
            at.Offset(parent.Location);
        using var part = new Bitmap(control.Width, control.Height);
        control.DrawToBitmap(part, new Rectangle(Point.Empty, control.Size));
        g.DrawImage(part, origin.X + at.X, origin.Y + at.Y);
    }
}
