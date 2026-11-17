using System.Windows;

namespace FlareLabels;
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, ex) => { MessageBox.Show("Flare Labels couldn't complete that step.\n\n" + (ex.Exception.InnerException?.Message??ex.Exception.Message), "Flare Labels", MessageBoxButton.OK, MessageBoxImage.Warning); ex.Handled = true; };
        if(e.Args.FirstOrDefault() is "--sign-update" or "--verify-update")
        {
            try{ReleaseTools.Run(e.Args);Shutdown(0);}
            catch(Exception ex){System.IO.File.WriteAllText("release-tool-error.txt",ex.ToString());Shutdown(1);}
            return;
        }
        if (e.Args.Contains("--self-test"))
        {
            try { SelfTests.Run(e.Args); Shutdown(0); }
            catch (Exception ex) { System.IO.File.WriteAllText("self-test-error.txt", ex.ToString()); Shutdown(1); }
            return;
        }
        try
        {
            var window = new MainWindow();
            MainWindow = window;
            window.Show();
            if (e.Args.Contains("--demo")) window.LoadDemo();
            else if (e.Args.FirstOrDefault() is string file && System.IO.File.Exists(file)) window.ImportPath(file);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Flare Labels couldn't start.\n\n" + (ex.InnerException?.Message??ex.Message),"Flare Labels",MessageBoxButton.OK,MessageBoxImage.Warning);
            Shutdown(1);
        }
    }
}
