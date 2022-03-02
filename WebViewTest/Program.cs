using System;
using System.Windows.Forms;

namespace WebViewTest;

internal static class Program
{
    /// <summary>
    ///  The main entry point for the application.
    /// </summary>
    [STAThread]
    static int Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        var mainForm = new MainForm();
        mainForm.Navigate(new Uri("https://appassets.example/index.html"));

        Application.Run(mainForm);

        return 0;
    }
}