using System.Drawing.Imaging;
using MapCore.Rendering;

namespace MapEditor;

static class Program
{
    /// <summary>
    /// <c>MapEditor [archive]</c> opens the editor, optionally with a map.
    /// <c>MapEditor --render &lt;archive&gt; &lt;out.png&gt; [options]</c> draws the map view, labels included, to a PNG
    /// without showing a window (options as for MapTool render). Errors go to &lt;out.png&gt;.error.txt.
    /// <c>MapEditor --selftest &lt;archive&gt; &lt;out folder&gt;</c> runs <see cref="SelfTest"/>.
    /// <c>MapEditor --window-pictures &lt;archive&gt; &lt;out folder&gt; [WxH]</c> draws the hidden window once for each tool tab (<see cref="WindowPictures"/>).
    /// </summary>
    [STAThread]
    static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        ToolStripManager.Renderer = new BarToolStripRenderer(); // menus, toolbar and status bar in BAR's look (BarTheme)
        try
        {
            MapCore.FileSystemUtil.SweepTempDirs(); // temporary folders a stopped or crashed run (of the editor or MapTool) left
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
        if (args.Length > 0 && args[0] == "--render")
            return RenderToFile(args);
        if (args.Length == 3 && args[0] == "--selftest")
            return SelfTest.Run(args[1], args[2]);
        if (args.Length is 3 or 4 && args[0] == "--window-pictures")
            return WindowPictures.Run(args[1], args[2], args.Length == 4 ? args[3] : null);
        // Anything other than no arguments or one map file is a mistake: never open a window for it.
        if (args.Length > 1 || (args.Length == 1 && args[0].StartsWith('-')))
            return 2;
        Application.Run(new MainForm(args.Length == 1 ? args[0] : null));
        return 0;
    }

    static int RenderToFile(string[] args)
    {
        string? output = args.Length >= 3 ? Path.GetFullPath(args[2]) : null;
        try
        {
            if (output == null)
                throw new FormatException("Usage: MapEditor --render <archive> <out.png> " + RenderRequest.Help);
            var request = RenderRequest.Parse(args[3..]);
            var map = MainForm.LoadMap(args[1], null, keepRecord: false);
            try
            {
                using var view = new MapViewControl();
                view.Attach(map.Session, map.Renderer);
                view.Layer = request.Layer;
                view.Overlays = request.Overlays;
                view.View = request.View(map.Session.MapWidth, map.Session.MapHeight);
                if (request.Layer is MapViewLayer.Splat or MapViewLayer.Normals)
                    map.Renderer.Prepare(request.Layer);
                using var bitmap = new Bitmap(request.Width, request.Height, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(bitmap))
                    view.Draw(g, bitmap.Size);
                bitmap.Save(output, ImageFormat.Png);
                File.WriteAllText(output + ".txt", $"layer {request.Layer}, {view.View.UnitsPerPixel} game units per pixel\nnote: {view.Note ?? "none"}\n");
            }
            finally
            {
                map.Renderer.Dispose();
                Workspaces.Delete(map.Workspace);
            }
            return 0;
        }
        catch (Exception ex)
        {
            if (output != null)
                File.WriteAllText(output + ".error.txt", ex.ToString());
            return 1;
        }
    }
}
