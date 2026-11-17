using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

// Compiled with the Windows .NET Framework compiler; the installer needs no
// additional runtime. The installed application contains its own .NET runtime.
internal static class SetupProgram
{
    internal const string Product="Flare Labels";
    internal const string AppExe="FlareLabels.exe";
    internal const string RegistryPath=@"Software\Microsoft\Windows\CurrentVersion\Uninstall\FlareLabels";
    internal static string ReleaseVersion {get{return Assembly.GetExecutingAssembly().GetName().Version.ToString(3);}}
    internal static string InstallPath {get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs",Product);}}
    [STAThread]
    static int Main(string[] args)
    {
#if UNINSTALLER
        if(args.Length==0)args=new[]{"--uninstall"};
#endif
        try
        {
            if(args.Length>0&&args[0]=="--verify-payload")
            {
                using(var s=Assembly.GetExecutingAssembly().GetManifestResourceStream("FlareLabels.Payload"))
                using(var zip=new ZipArchive(s,ZipArchiveMode.Read))
                {
                    if(!zip.Entries.Any(e=>e.FullName==AppExe))return 2;
                    foreach(var entry in zip.Entries){using(var stream=entry.Open()){var buffer=new byte[65536];while(stream.Read(buffer,0,buffer.Length)>0){}}}
                }
                return 0;
            }
            if(args.Length>1&&args[0]=="--extract")
            {
                string destination=Path.GetFullPath(args[1]);
                if(Directory.Exists(destination)&&Directory.EnumerateFileSystemEntries(destination).Any())throw new InvalidOperationException("Extraction destination must be empty.");
                Extract(destination,null);return 0;
            }
            if(args.Length>0&&args[0]=="--uninstall")
            {
                Application.EnableVisualStyles();
                if(MessageBox.Show("Remove Flare Labels from this computer? Your spreadsheets and Word documents will be kept.","Uninstall Flare Labels",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return 0;
                string tempDir=Path.Combine(Path.GetTempPath(),"FlareLabels-Uninstall-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(tempDir);
                string helper=Path.Combine(tempDir,"Remove-FlareLabels.exe");File.Copy(Assembly.GetExecutingAssembly().Location,helper);
                Process.Start(new ProcessStartInfo(helper,"--remove"){UseShellExecute=false,CreateNoWindow=true});return 0;
            }
            if(args.Length>0&&args[0]=="--remove"){Remove();return 0;}
            bool updating=args.Contains("--update");
            if(updating){for(int retry=0;retry<40&&InstalledAppRunning();retry++)Thread.Sleep(250);}
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);Application.Run(new SetupWindow(updating));return 0;
        }
        catch(Exception ex){MessageBox.Show(ex.Message,"Flare Labels setup",MessageBoxButtons.OK,MessageBoxIcon.Warning);return 1;}
    }
    internal static void Extract(string destination,Action<int> progress)
    {
        string root=Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
        Directory.CreateDirectory(root);
        using(var resource=Assembly.GetExecutingAssembly().GetManifestResourceStream("FlareLabels.Payload"))
        using(var zip=new ZipArchive(resource,ZipArchiveMode.Read))
        {
            long total=zip.Entries.Sum(e=>e.Length),done=0;
            foreach(var entry in zip.Entries)
            {
                string path=Path.GetFullPath(Path.Combine(root,entry.FullName));
                if(!path.StartsWith(root,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Invalid setup file path.");
                if(entry.FullName.EndsWith("/")){Directory.CreateDirectory(path);continue;}
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                using(var input=entry.Open())using(var output=new FileStream(path,FileMode.Create,FileAccess.Write,FileShare.None))input.CopyTo(output);
                done+=entry.Length;if(progress!=null)progress((int)(done*100/Math.Max(1,total)));
            }
        }
    }
    internal static void Register(bool desktop)
    {
        string executable=Path.Combine(InstallPath,AppExe);
        string startFolder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs),Product);Directory.CreateDirectory(startFolder);
        Shortcut(Path.Combine(startFolder,Product+".lnk"),executable);
        if(desktop)Shortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),Product+".lnk"),executable);
        using(var key=Registry.CurrentUser.CreateSubKey(RegistryPath))
        {
            key.SetValue("DisplayName",Product);key.SetValue("DisplayVersion",ReleaseVersion);key.SetValue("Publisher","Flare Fireplaces");key.SetValue("DisplayIcon",executable);key.SetValue("InstallLocation",InstallPath);
            key.SetValue("UninstallString","\""+Path.Combine(InstallPath,"FlareLabels-Uninstall.exe")+"\" --uninstall");key.SetValue("NoModify",1);key.SetValue("NoRepair",1);
            key.SetValue("EstimatedSize",(int)(Directory.GetFiles(InstallPath,"*",SearchOption.AllDirectories).Sum(f=>new FileInfo(f).Length)/1024),RegistryValueKind.DWord);
        }
    }
    internal static bool InstalledAppRunning()
    {
        string installed=Path.GetFullPath(Path.Combine(InstallPath,AppExe));
        foreach(var process in Process.GetProcessesByName("FlareLabels"))
        {
            try{if(Path.GetFullPath(process.MainModule.FileName).Equals(installed,StringComparison.OrdinalIgnoreCase))return true;}
            catch(System.ComponentModel.Win32Exception){}finally{process.Dispose();}
        }
        return false;
    }
    static void Shortcut(string path,string target)
    {
        Type shellType=Type.GetTypeFromProgID("WScript.Shell");object shell=Activator.CreateInstance(shellType);
        object link=shellType.InvokeMember("CreateShortcut",BindingFlags.InvokeMethod,null,shell,new object[]{path});Type t=link.GetType();
        t.InvokeMember("TargetPath",BindingFlags.SetProperty,null,link,new object[]{target});t.InvokeMember("WorkingDirectory",BindingFlags.SetProperty,null,link,new object[]{InstallPath});
        t.InvokeMember("IconLocation",BindingFlags.SetProperty,null,link,new object[]{target+",0"});t.InvokeMember("Description",BindingFlags.SetProperty,null,link,new object[]{"Flare Fireplaces mailing labels"});t.InvokeMember("Save",BindingFlags.InvokeMethod,null,link,null);
        System.Runtime.InteropServices.Marshal.FinalReleaseComObject(link);System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
    }
    static void Remove()
    {
        string target=Path.GetFullPath(InstallPath).TrimEnd(Path.DirectorySeparatorChar);
        string allowed=Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs","Flare Labels")).TrimEnd(Path.DirectorySeparatorChar);
        if(!target.Equals(allowed,StringComparison.OrdinalIgnoreCase)||Path.GetFileName(target)!="Flare Labels")throw new InvalidOperationException("Unexpected uninstall location.");
        string executable=Path.Combine(target,AppExe);
        foreach(var process in Process.GetProcessesByName("FlareLabels"))
        {
            try{if(String.Equals(process.MainModule.FileName,executable,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Close Flare Labels and run uninstall again.");}
            finally{process.Dispose();}
        }
        Thread.Sleep(800);
        if(Directory.Exists(target))
        {
            if((File.GetAttributes(target)&FileAttributes.ReparsePoint)!=0)throw new InvalidOperationException("Refusing to remove a redirected installation folder.");
            // The app payload has no subdirectories. Remove only known package files;
            // leave any user-added files or folders in place.
            foreach(string file in new[]{AppExe,"FlareLabels-Uninstall.exe","Quick-Start.txt","DotNet-License.txt","DotNet-ThirdParty-Notices.txt"}){string path=Path.Combine(target,file);if(File.Exists(path))File.Delete(path);}
            if(!Directory.EnumerateFileSystemEntries(target).Any())Directory.Delete(target);
        }
        string start=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs),Product);string startLink=Path.Combine(start,Product+".lnk");
        if(File.Exists(startLink))File.Delete(startLink);if(Directory.Exists(start)&&!Directory.EnumerateFileSystemEntries(start).Any())Directory.Delete(start);
        string desktop=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),Product+".lnk");if(File.Exists(desktop))File.Delete(desktop);
        Registry.CurrentUser.DeleteSubKeyTree(RegistryPath,false);
        MessageBox.Show("Flare Labels was removed. Your spreadsheets and Word documents were kept.","Flare Labels",MessageBoxButtons.OK,MessageBoxIcon.Information);
    }
}

internal sealed class SetupWindow : Form
{
    readonly Button install=new Button();readonly CheckBox desktop=new CheckBox();readonly ProgressBar progress=new ProgressBar();readonly Label status=new Label();readonly bool updateMode;bool complete,busy;
    public SetupWindow(bool updating)
    {
        updateMode=updating;
        Text="Flare Labels Setup";ClientSize=new Size(540,550);FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;StartPosition=FormStartPosition.CenterScreen;BackColor=Color.FromArgb(20,29,40);ForeColor=Color.White;Font=new Font("Segoe UI",10);Icon=Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location);
        var logo=new PictureBox{Bounds=new Rectangle(34,30,86,86),SizeMode=PictureBoxSizeMode.Zoom};
        using(var image=Assembly.GetExecutingAssembly().GetManifestResourceStream("FlareLabels.Logo")){if(image!=null){using(var bitmap=new Bitmap(image))logo.Image=new Bitmap(bitmap);}}Controls.Add(logo);
        AddLabel("FLARE FIREPLACES",new Rectangle(140,45,350,22),10,Color.FromArgb(180,195,213),FontStyle.Bold);
        AddLabel("Flare Labels",new Rectangle(137,70,360,45),26,Color.White,FontStyle.Bold);
        AddLabel("Excel contacts. Centered Word labels.",new Rectangle(35,143,465,32),15,Color.White,FontStyle.Regular);
        AddLabel("• Import Excel and match your address columns\n• Review missing details and possible duplicates\n• Export centered 4 × 2-inch mailing labels\n• Works offline without a login",new Rectangle(35,193,475,116),11,Color.FromArgb(187,199,214),FontStyle.Regular);
        AddLabel("Installs for your Windows account",new Rectangle(35,326,470,24),10,Color.FromArgb(187,199,214),FontStyle.Regular);
        AddLabel(SetupProgram.InstallPath,new Rectangle(35,352,470,36),9,Color.FromArgb(180,195,213),FontStyle.Regular);
        desktop.Text="Add a desktop shortcut";desktop.Checked=!updating||File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),"Flare Labels.lnk"));desktop.SetBounds(35,401,460,25);desktop.ForeColor=Color.FromArgb(214,223,234);Controls.Add(desktop);
        progress.SetBounds(35,440,470,7);progress.Style=ProgressBarStyle.Continuous;progress.Visible=false;Controls.Add(progress);
        status.SetBounds(35,460,290,52);status.ForeColor=Color.FromArgb(180,195,213);status.Font=new Font("Segoe UI",9);status.Text="Version "+SetupProgram.ReleaseVersion+" · Windows 10 / 11 (64-bit)";Controls.Add(status);
        install.Text="Install Flare Labels";install.SetBounds(336,469,170,43);install.BackColor=Color.FromArgb(196,65,20);install.ForeColor=Color.White;install.FlatStyle=FlatStyle.Flat;install.FlatAppearance.BorderSize=0;install.Cursor=Cursors.Hand;install.Click+=InstallClick;Controls.Add(install);
        FormClosing+=(s,e)=>{if(busy)e.Cancel=true;};
        if(updating)Shown+=(s,e)=>InstallClick(s,e);
    }
    void AddLabel(string text,Rectangle bounds,float size,Color color,FontStyle style){Controls.Add(new Label{Text=text,Bounds=bounds,Font=new Font("Segoe UI",size,style),ForeColor=color,BackColor=Color.Transparent});}
    async void InstallClick(object sender,EventArgs e)
    {
        if(complete){Process.Start(Path.Combine(SetupProgram.InstallPath,SetupProgram.AppExe));Close();return;}
        if(SetupProgram.InstalledAppRunning()){MessageBox.Show(this,"Close Flare Labels before installing or updating it.","Flare Labels setup",MessageBoxButtons.OK,MessageBoxIcon.Information);return;}
        busy=true;install.Enabled=false;desktop.Enabled=false;progress.Visible=true;status.Text="Installing…";
        try
        {
            await Task.Run(()=>SetupProgram.Extract(SetupProgram.InstallPath,value=>BeginInvoke((Action)(()=>progress.Value=value))));
            SetupProgram.Register(desktop.Checked);complete=true;install.Text="Open Flare Labels";status.Text="Installed. You're ready to make labels.";
        }
        catch(Exception ex){MessageBox.Show(this,"Setup couldn't finish.\n\n"+ex.Message,"Flare Labels setup",MessageBoxButtons.OK,MessageBoxIcon.Warning);status.Text="Setup didn't finish. You can try again.";}
        finally{busy=false;install.Enabled=true;desktop.Enabled=true;}
        if(complete&&updateMode){Process.Start(Path.Combine(SetupProgram.InstallPath,SetupProgram.AppExe));Close();}
    }
}
