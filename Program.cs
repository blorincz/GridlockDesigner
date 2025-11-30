namespace GridlockDesigner;

using System;
using System.Windows;

public class Program
{
    [STAThread]
    public static void Main()
    {
        var app = new Application();
        app.Startup += (s, e) =>
        {
            var mainWindow = new MainWindow();
            mainWindow.Show();
        };
        app.Run();
    }
}
