using Microsoft.Web.WebView2.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Newtonsoft.Json.Linq;

namespace WebViewTest;

public partial class MainForm : Form
{
    private static readonly Dictionary<string, string> s_mimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        { ".html", "text/html; charset=utf-8" },
        { ".txt", "text/plain; charset=utf-8" },
        { ".js", "text/javascript; charset=utf-8" },
        { ".json", "application/json; charset=utf-8" },
        { ".css", "text/css; charset=utf-8" },
    };

    public MainForm()
    {
        this.InitializeComponent();

        unsafe
        {
            var hwnd = (HWND)this.Handle;
            uint attribute = 2; // DWMWCP_ROUND
            PInvoke.DwmSetWindowAttribute(hwnd, DWMWINDOWATTRIBUTE.DWMWA_WINDOW_CORNER_PREFERENCE, &attribute, sizeof(uint));
        }

#if !DEBUG
        this.mainMenuStrip.Hide();
#endif
    }

    public void Navigate(Uri uri)
    {
        this.webView.Source = uri;
    }

    private void webView_CoreWebView2InitializationCompleted(object sender, CoreWebView2InitializationCompletedEventArgs e)
    {
        if (!e.IsSuccess)
        {
            MessageBox.Show("アプリを起動できませんでした。 WebView2 Runtime がインストールされていない可能性があります。\n\n" + e.InitializationException, "起動失敗", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Environment.Exit(1);
            return;
        }

        var core = this.webView.CoreWebView2;
        var settings = core.Settings;
        settings.AreBrowserAcceleratorKeysEnabled = false;
        settings.AreDevToolsEnabled = false;
        settings.AreHostObjectsAllowed = false;
        settings.IsPinchZoomEnabled = false;
        settings.IsStatusBarEnabled = false;
        settings.IsZoomControlEnabled = false;

        core.AddWebResourceRequestedFilter("*://appassets.example/*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += this.CoreOnWebResourceRequested;

        core.ContextMenuRequested += this.CoreOnContextMenuRequested;
        core.DocumentTitleChanged += this.CoreOnDocumentTitleChanged;
        core.NewWindowRequested += this.CoreOnNewWindowRequested;
        core.WebMessageReceived += this.CoreOnWebMessageReceived;
        core.WindowCloseRequested += this.CoreOnWindowCloseRequested;
    }

    private void CoreOnContextMenuRequested(object sender, CoreWebView2ContextMenuRequestedEventArgs e)
    {
        if (e.ContextMenuTarget.Kind == CoreWebView2ContextMenuTargetKind.Page)
        {
            if (e.ContextMenuTarget.HasLinkUri)
            {
                // URLコピー以外を削除
                RemoveIf(item => item.Name != "copyLinkLocation");
            }
            else if (e.ContextMenuTarget.IsEditable)
            {
                RemoveShareCommand();
            }
            else
            {
                e.Handled = true;
                return;
            }
        }
        else if (e.ContextMenuTarget.Kind == CoreWebView2ContextMenuTargetKind.SelectedText)
        {
            // コピー以外を削除
            RemoveIf(item => item.Name is not ("copy" or "copyLinkLocation"));
        }
        else
        {
            RemoveShareCommand();
        }

        // 連続したセパレータを削除
        for (var i = 1; i < e.MenuItems.Count;)
        {
            if (e.MenuItems[i].Kind == CoreWebView2ContextMenuItemKind.Separator &&
                e.MenuItems[i - 1].Kind == CoreWebView2ContextMenuItemKind.Separator)
            {
                e.MenuItems.RemoveAt(i);
            }
            else
            {
                i++;
            }
        }

        // 最後のセパレータを削除
        if (e.MenuItems.LastOrDefault()?.Kind == CoreWebView2ContextMenuItemKind.Separator)
            e.MenuItems.RemoveAt(e.MenuItems.Count - 1);

        void RemoveIf(Predicate<CoreWebView2ContextMenuItem> predicate)
        {
            for (var i = 0; i < e.MenuItems.Count;)
            {
                if (predicate(e.MenuItems[i]))
                {
                    e.MenuItems.RemoveAt(i);
                }
                else
                {
                    i++;
                }
            }
        }

        void RemoveShareCommand()
        {
            RemoveIf(item => item.Kind == CoreWebView2ContextMenuItemKind.Command &&
                             item.Name == "other");
        }
    }

    private void CoreOnDocumentTitleChanged(object? sender, object? e)
    {
        this.Text = this.webView.CoreWebView2.DocumentTitle;
    }

    private void CoreOnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        Process.Start(new ProcessStartInfo(e.Uri) { UseShellExecute = true });
    }

    private void CoreOnWebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        var message = JObject.Parse(e.WebMessageAsJson);

        switch ((string?)message["type"])
        {
            case "alert":
                MessageBox.Show((string)message["content"]!, "Received Message");
                break;
            case "subwindow":
                var newWindow = new MainForm();
                newWindow.Navigate(new Uri("https://appassets.example/subwindow.html"));
                newWindow.Show();
                break;
        }
    }

    private void CoreOnWebResourceRequested(object sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        if (!string.Equals(e.Request.Method, "GET", StringComparison.OrdinalIgnoreCase))
        {
            e.Response = this.webView.CoreWebView2.Environment.CreateWebResourceResponse(Stream.Null, 405, "Method Not Allowed", "Content-Length: 0\r\n");
            return;
        }

        var path = new Uri(e.Request.Uri).AbsolutePath.TrimStart('/');
        if (path == "") path = "index.html";

        var assetsDirectory = Path.Combine(typeof(MainForm).Assembly.Location, "../assets");
        path = Path.Combine(assetsDirectory, path);

        if (!File.Exists(path))
        {
            if (e.ResourceContext != CoreWebView2WebResourceContext.Document)
            {
                e.Response = this.webView.CoreWebView2.Environment.CreateWebResourceResponse(Stream.Null, 404, "Not Found", "Content-Length: 0\r\n");
                return;
            }

            path = Path.Combine(assetsDirectory, "index.html");
        }

        if (!s_mimeTypes.TryGetValue(Path.GetExtension(path), out var mimeType))
            mimeType = "application/octet-stream";

        var stream = File.OpenRead(path);

        var header = new StringBuilder()
            .AppendFormat(CultureInfo.InvariantCulture, "Content-Length: {0}\r\n", stream.Length)
            .AppendFormat(CultureInfo.InvariantCulture, "Content-Type: {0}\r\n", mimeType);

        if (string.Equals(path, "index.html", StringComparison.OrdinalIgnoreCase))
            header.Append("Content-Location: https://appassets.example/index.html\r\n");

        e.Response = this.webView.CoreWebView2.Environment.CreateWebResourceResponse(stream, 200, "OK", header.ToString());
    }

    private void CoreOnWindowCloseRequested(object sender, object e)
    {
        this.Close();
    }

    private void reloadToolStripMenuItem_Click(object sender, EventArgs e)
    {
        this.webView.Reload();
    }
    
    private void devToolsToolStripMenuItem_Click(object sender, EventArgs e)
    {
        this.webView.CoreWebView2?.OpenDevToolsWindow();
    }

    private void taskManagerToolStripMenuItem_Click(object sender, EventArgs e)
    {
        this.webView.CoreWebView2?.OpenTaskManagerWindow();
    }
}
