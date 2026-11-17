using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace FlareLabels;
public partial class MainWindow : Window
{
    readonly ObservableCollection<Contact> contacts=[];
    readonly DispatcherTimer refreshTimer=new(){Interval=TimeSpan.FromMilliseconds(160)};
    readonly string settingsPath=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Flare Labels","settings.json");
    LabelOptions options=new();
    WorkbookData? workbook;
    ColumnMap? map;
    bool initialized,loading;
    int currentPage;
    ICollectionView? view;
    UpdateInfo? pendingUpdate;
    bool checkingUpdates;

    public MainWindow()
    {
        InitializeComponent();
        try{if(File.Exists(settingsPath))options=JsonSerializer.Deserialize<LabelOptions>(File.ReadAllText(settingsPath))??new();}catch{options=new();}
        options.StartPosition=1;
        options.FontSize=new[]{10d,12,14,16,18}.Contains(options.FontSize)?options.FontSize:16;
        options.OffsetX=Math.Clamp(options.OffsetX,-0.15,0.15);options.OffsetY=Math.Clamp(options.OffsetY,-0.15,0.15);
        FontSelect.SelectedIndex=Array.IndexOf(new[]{10d,12,14,16,18},options.FontSize);
        StartSelect.SelectedIndex=0;
        view=CollectionViewSource.GetDefaultView(contacts);view.Filter=FilterContact;ContactsGrid.ItemsSource=view;
        refreshTimer.Tick+=(_,_)=>{refreshTimer.Stop();Refresh();};
        initialized=true;
        Closed+=(_,_)=>SaveSettings();
        Refresh();
        ContentRendered+=async(_,_)=>
        {
            if(options.AutoCheckUpdates&&DateTime.UtcNow-options.LastUpdateCheckUtc>TimeSpan.FromHours(24))
            {await Task.Delay(1200);await CheckForUpdatesAsync(false);}
        };
    }
    void SaveSettings()
    {
        try{Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);File.WriteAllText(settingsPath,JsonSerializer.Serialize(options));}catch{}
    }
    void Refresh()
    {
        if(!initialized)return;
        ContactRules.MarkDuplicates(contacts);
        view?.Refresh();
        var selected=contacts.Where(c=>c.Include).ToList();
        int pages=LabelEngine.PageCount(selected.Count,options.StartPosition);
        CountImported.Text=contacts.Count.ToString("N0");CountSelected.Text=selected.Count.ToString("N0");CountPages.Text=pages.ToString("N0");
        currentPage=Math.Clamp(currentPage,0,Math.Max(0,pages-1));
        ExportButton.IsEnabled=selected.Count>0&&!loading;
        MapButton.IsEnabled=workbook!=null;
        EmptyState.Visibility=contacts.Count==0?Visibility.Visible:Visibility.Collapsed;
        ReviewDuplicates.Visibility=contacts.Any(c=>c.IsDuplicate&&!c.DuplicateReviewed)?Visibility.Visible:Visibility.Collapsed;
        int missing=selected.Count(c=>!c.IsValid),duplicates=selected.Count(c=>c.IsDuplicate&&!c.DuplicateReviewed);
        StatusText.Text=contacts.Count==0?"Company first · Full name when available · Centered addresses":missing>0?$"{missing} selected address{(missing==1?"":"es")} need details. Double-click to edit.":duplicates>0?$"{duplicates} selected labels share a company and address. Review before exporting.":$"{selected.Count:N0} labels ready · Centered horizontally and vertically";
        RenderPreview();
    }
    bool FilterContact(object item)
    {
        var c=(Contact)item;string search=SearchBox.Text.Trim();
        if(search.Length>0&&!string.Join(" ",c.Recipient,c.FullName,c.Street,c.Street2,c.City,c.State,c.Postal).Contains(search,StringComparison.OrdinalIgnoreCase))return false;
        return FilterSelect.SelectedIndex switch {1=>!c.IsValid,2=>c.IsDuplicate,3=>c.Include,_=>true};
    }
    void RenderPreview()
    {
        SheetCanvas.Children.Clear();
        var selected=contacts.Where(c=>c.Include).ToList();int pages=LabelEngine.PageCount(selected.Count,options.StartPosition);
        PageText.Text=$"Page {(pages==0?1:currentPage+1)} of {Math.Max(1,pages)}";
        for(int slot=0;slot<10;slot++)
        {
            int index=currentPage*10+slot-(options.StartPosition-1);
            Contact? c=index>=0&&index<selected.Count?selected[index]:null;
            var border=new Border{Width=384,Height=192,BorderBrush=new SolidColorBrush(Color.FromRgb(159,174,192)),BorderThickness=new(1.5),CornerRadius=new(12),Background=Brushes.White};
            if(c!=null&&c==ContactsGrid.SelectedItem){border.BorderBrush=new SolidColorBrush(Color.FromRgb(234,83,31));border.BorderThickness=new(2);}
            Canvas.SetLeft(border,15.6+(slot%2)*402+options.OffsetX*96);Canvas.SetTop(border,48+(slot/2)*192+options.OffsetY*96);
            if(c!=null)
            {
                try
                {
                    var layout=LabelEngine.Layout(c,options.FontSize);
                    var text=new TextBlock{Text=string.Join("\n",layout.Lines),FontFamily=new("Arial"),FontSize=layout.FontSize*96/72,LineHeight=layout.FontSize*96/72*1.15,LineStackingStrategy=LineStackingStrategy.BlockLineHeight,TextAlignment=TextAlignment.Center,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,Foreground=c.IsValid?Brushes.Black:new SolidColorBrush(Color.FromRgb(177,65,44)),Width=LabelEngine.TextWidth};
                    border.Child=text;
                }
                catch{border.Child=new TextBlock{Text="Address is too long\nDouble-click contact to edit",FontSize=18,TextAlignment=TextAlignment.Center,VerticalAlignment=VerticalAlignment.Center,Foreground=Brushes.IndianRed};}
                border.ToolTip=c.Recipient;
            }
            else if(contacts.Count==0&&slot==0)border.Child=new TextBlock{Text="Your labels appear here",FontSize=22,Foreground=new SolidColorBrush(Color.FromRgb(82,98,119)),TextAlignment=TextAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
            SheetCanvas.Children.Add(border);
        }
    }
    void OnContactChanged(object? sender,PropertyChangedEventArgs e)
    {
        if(e.PropertyName is "IsDuplicate" or "DuplicateReviewed" or "Status" or "FullName" or "Recipient" or "NameDisplay" or "Location" or "Missing" or "IsValid" or "DuplicateKey")return;
        if(sender is Contact c && e.PropertyName!="Include")c.DuplicateReviewed=false;
        refreshTimer.Stop();refreshTimer.Start();
    }
    void ApplySheet(bool forceMap=false)
    {
        if(workbook==null||SheetSelect.SelectedIndex<0)return;
        var sheet=workbook.Sheets[SheetSelect.SelectedIndex];
        var detected=SpreadsheetReader.Detect(sheet);
        if(forceMap||SpreadsheetReader.Fields.Where(f=>f.Required).Any(f=>!detected.Columns.ContainsKey(f.Key))||!detected.Columns.ContainsKey("Company")&&!(detected.Columns.ContainsKey("First")&&detected.Columns.ContainsKey("Last"))&&!detected.Columns.ContainsKey("FullName"))
        {
            var dialog=new MappingDialog(sheet,forceMap&&map!=null?map:detected){Owner=this};
            if(dialog.ShowDialog()!=true)return;
            detected=dialog.Result!;
        }
        map=detected;SetContacts(SpreadsheetReader.Contacts(sheet,map));
        FileDetail.Text=$"Header row {map.HeaderRow+1} · {map.Columns.Count} columns matched";
        currentPage=0;Refresh();
    }
    void SetContacts(IEnumerable<Contact> incoming)
    {
        foreach(var c in contacts)c.PropertyChanged-=OnContactChanged;
        contacts.Clear();foreach(var c in incoming){c.PropertyChanged+=OnContactChanged;contacts.Add(c);}
        currentPage=0;
    }
    public async void ImportPath(string path)
    {
        if(loading)return;
        if(!new[]{".xlsx",".xlsm",".csv"}.Contains(Path.GetExtension(path).ToLowerInvariant())){MessageBox.Show(this,"Choose an .xlsx, .xlsm or .csv file. For an older .xls file, use Excel's Save As to create an .xlsx copy.","Choose an Excel workbook",MessageBoxButton.OK,MessageBoxImage.Information);return;}
        if(contacts.Count>0&&MessageBox.Show(this,"Importing a new spreadsheet replaces the contact list in this window. Any Word documents already saved are kept.\n\nImport the new spreadsheet?","Import spreadsheet",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
        loading=true;Cursor=Cursors.Wait;ExportButton.IsEnabled=false;StatusText.Text="Reading spreadsheet…";
        try
        {
            var loaded=await Task.Run(()=>SpreadsheetReader.Read(path));
            workbook=loaded;map=null;FileTitle.Text=loaded.FileName;
            SheetSelect.ItemsSource=loaded.Sheets.Select(s=>s.Name).ToList();SheetSelect.Visibility=Visibility.Visible;
            SheetSelect.SelectedIndex=0;
            // Sheet change is suppressed while loading; map only once here.
            ApplySheet();
        }
        catch(Exception ex){MessageBox.Show(this,"Couldn't import this file.\n\n"+ex.Message,"Import spreadsheet",MessageBoxButton.OK,MessageBoxImage.Warning);}
        finally{loading=false;Cursor=null;Refresh();}
    }
    void Import_Click(object sender,RoutedEventArgs e)
    {
        var dialog=new OpenFileDialog{Title="Import contacts",Filter="Excel workbooks and CSV|*.xlsx;*.xlsm;*.csv|Excel workbook|*.xlsx;*.xlsm|CSV file|*.csv",CheckFileExists=true};if(dialog.ShowDialog(this)==true)ImportPath(dialog.FileName);
    }
    void Sheet_Changed(object sender,SelectionChangedEventArgs e){if(initialized&&!loading)ApplySheet();}
    void Map_Click(object sender,RoutedEventArgs e)
    {
        if(workbook==null)return;
        var dialog=new MappingDialog(workbook.Sheets[SheetSelect.SelectedIndex],map??SpreadsheetReader.Detect(workbook.Sheets[SheetSelect.SelectedIndex])){Owner=this};
        if(dialog.ShowDialog()==true){map=dialog.Result;SetContacts(SpreadsheetReader.Contacts(workbook.Sheets[SheetSelect.SelectedIndex],map!));FileDetail.Text=$"Header row {map!.HeaderRow+1} · {map.Columns.Count} columns matched";Refresh();}
    }
    void Options_Changed(object sender,SelectionChangedEventArgs e)
    {
        if(!initialized)return;options.FontSize=new[]{10d,12,14,16,18}[Math.Max(0,FontSelect.SelectedIndex)];options.StartPosition=StartSelect.SelectedIndex+1;currentPage=0;Refresh();
    }
    void Search_Changed(object sender,TextChangedEventArgs e){if(SearchPlaceholder!=null)SearchPlaceholder.Visibility=SearchBox.Text.Length==0?Visibility.Visible:Visibility.Collapsed;if(initialized)view?.Refresh();}
    void Filter_Changed(object sender,SelectionChangedEventArgs e){if(initialized)view?.Refresh();}
    void SelectAll_Click(object sender,RoutedEventArgs e){foreach(var c in contacts)c.Include=true;Refresh();}
    void SelectNone_Click(object sender,RoutedEventArgs e){foreach(var c in contacts)c.Include=false;Refresh();}
    void KeepDuplicates_Click(object sender,RoutedEventArgs e){foreach(var c in contacts.Where(c=>c.IsDuplicate))c.DuplicateReviewed=true;Refresh();}
    void Edit_Click(object sender,RoutedEventArgs e)=>EditSelected();
    void Contact_DoubleClick(object sender,MouseButtonEventArgs e){if(ContactsGrid.SelectedItem is Contact)EditSelected();}
    void Contacts_KeyDown(object sender,KeyEventArgs e){if(e.Key==Key.Enter){EditSelected();e.Handled=true;}}
    void EditSelected()
    {
        if(ContactsGrid.SelectedItem is not Contact c){StatusText.Text="Choose a contact, then select Edit. You can also double-click a row.";return;}
        var dialog=new ContactDialog(c){Owner=this};if(dialog.ShowDialog()==true)Refresh();
    }
    void Contact_Selected(object sender,SelectionChangedEventArgs e)
    {
        if(!initialized)return;
        if(ContactsGrid.SelectedItem is Contact c&&c.Include){int i=contacts.Where(x=>x.Include).ToList().IndexOf(c);currentPage=(i+options.StartPosition-1)/10;}
        RenderPreview();
    }
    void Previous_Click(object sender,RoutedEventArgs e){currentPage=Math.Max(0,currentPage-1);RenderPreview();}
    void Next_Click(object sender,RoutedEventArgs e){int pages=LabelEngine.PageCount(contacts.Count(c=>c.Include),options.StartPosition);currentPage=Math.Min(Math.Max(0,pages-1),currentPage+1);RenderPreview();}
    async void Export_Click(object sender,RoutedEventArgs e)
    {
        var selected=contacts.Where(c=>c.Include).ToList();if(selected.Count==0)return;
        if(selected.Any(c=>!c.IsValid)){FilterSelect.SelectedIndex=1;MessageBox.Show(this,"Some selected labels are missing address details. Double-click those contacts to edit them, or uncheck them before exporting.","Complete the addresses",MessageBoxButton.OK,MessageBoxImage.Information);return;}
        if(selected.Any(c=>c.IsDuplicate&&!c.DuplicateReviewed))
        {
            var result=MessageBox.Show(this,"Some selected labels share the same company and mailing address.\n\nYes: Keep all selected duplicates and export.\nNo: Show duplicates for review.\nCancel: Return to the contact list.","Review possible duplicates",MessageBoxButton.YesNoCancel,MessageBoxImage.Question);
            if(result==MessageBoxResult.No){FilterSelect.SelectedIndex=2;return;}
            if(result!=MessageBoxResult.Yes)return;
            foreach(var c in selected.Where(c=>c.IsDuplicate))c.DuplicateReviewed=true;
        }
        var dialog=new SaveFileDialog{Title="Save Word labels",Filter="Word document|*.docx",FileName=$"Flare-Labels-{DateTime.Today:yyyy-MM-dd}.docx",DefaultExt=".docx",AddExtension=true,InitialDirectory=Directory.Exists(options.LastExportDirectory)?options.LastExportDirectory:Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)};
        if(dialog.ShowDialog(this)!=true)return;
        ExportButton.IsEnabled=false;Cursor=Cursors.Wait;
        try
        {
            var exportOptions=new LabelOptions{FontSize=options.FontSize,StartPosition=options.StartPosition,OffsetX=options.OffsetX,OffsetY=options.OffsetY};
            await Task.Run(()=>WordExporter.Export(dialog.FileName,selected,exportOptions));
            options.LastExportDirectory=Path.GetDirectoryName(dialog.FileName)!;SaveSettings();
            StatusText.Text=$"Saved {selected.Count} labels to {Path.GetFileName(dialog.FileName)}";
            if(MessageBox.Show(this,$"Saved {selected.Count} labels across {LabelEngine.PageCount(selected.Count,options.StartPosition)} sheet(s).\n\nPrint on Letter paper, one page per sheet, at 100% scale. Test on plain paper first.\n\nOpen the document in Word now?","Word labels saved",MessageBoxButton.YesNo,MessageBoxImage.Information)==MessageBoxResult.Yes)Process.Start(new ProcessStartInfo(dialog.FileName){UseShellExecute=true});
        }
        catch(Exception ex){MessageBox.Show(this,"Couldn't save the Word document.\n\n"+ex.Message+"\n\nIf this document is already open in Word, close it or choose a different filename.","Export labels",MessageBoxButton.OK,MessageBoxImage.Warning);}
        finally{Cursor=null;ExportButton.IsEnabled=selected.Count>0;}
    }
    void Settings_Click(object sender,RoutedEventArgs e){var dialog=new PrintSettingsDialog(options){Owner=this};if(dialog.ShowDialog()==true){SaveSettings();RenderPreview();}}
    async void CheckUpdates_Click(object sender,RoutedEventArgs e)=>await CheckForUpdatesAsync(true);
    async Task CheckForUpdatesAsync(bool interactive)
    {
        if(checkingUpdates)return;checkingUpdates=true;
        try
        {
            if(interactive){UpdateButton.IsEnabled=false;UpdateButton.Content="Checking updates…";}
            var update=pendingUpdate??await UpdateService.CheckAsync();
            options.LastUpdateCheckUtc=DateTime.UtcNow;SaveSettings();
            if(update==null)
            {
                if(interactive)MessageBox.Show(this,$"Flare Labels {UpdateService.CurrentVersion} is up to date.","Flare Labels updates",MessageBoxButton.OK,MessageBoxImage.Information);
                return;
            }
            pendingUpdate=update;UpdateButton.Content="Update available";UpdateButton.ToolTip="Install Flare Labels "+update.Version;
            if(!interactive)return;
            string work=contacts.Count>0?"\n\nInstalling closes this contact list. Export any labels you need first.":"";
            if(MessageBox.Show(this,$"Flare Labels {update.Version} is available.\n\nDownload and install the update now?"+work,"Update Flare Labels",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
            UpdateButton.Content="Downloading…";
            string file=await UpdateService.DownloadAsync(update,new Progress<int>(n=>UpdateButton.Content=$"Downloading {n}%"));
            SaveSettings();UpdateService.LaunchInstaller(file);Application.Current.Shutdown();
        }
        catch(Exception ex)
        {
            if(interactive)MessageBox.Show(this,"Couldn't complete the update check. Label creation is still available.\n\n"+ex.Message,"Flare Labels updates",MessageBoxButton.OK,MessageBoxImage.Information);
        }
        finally{checkingUpdates=false;UpdateButton.IsEnabled=true;UpdateButton.Content=pendingUpdate==null?"Check for updates":"Update available";}
    }
    void FileDrop(object sender,DragEventArgs e){if(e.Data.GetData(DataFormats.FileDrop) is string[] files&&files.Length>0)ImportPath(files[0]);}
    void FileDragOver(object sender,DragEventArgs e){e.Effects=e.Data.GetDataPresent(DataFormats.FileDrop)?DragDropEffects.Copy:DragDropEffects.None;e.Handled=true;}
    void Demo_Click(object sender,RoutedEventArgs e)=>LoadDemo();
    public void LoadDemo()
    {
        var list=new List<Contact>();
        var names=new[]{("Stonebridge Architecture","Avery","Morgan","Portland","OR","97205"),("Northline Studio","Jordan","Lee","Austin","TX","78701"),("Fieldwork Design","","","Denver","CO","80202"),("Juniper Interiors","Casey","Reed","Reno","NV","89501"),("West & Co. Architects","Taylor","Brooks","San Diego","CA","92101"),("Oak House Design","","","Dallas","TX","75201"),("Summit Architecture","Alex","Parker","Seattle","WA","98101"),("Form Studio","Riley","Chen","Chicago","IL","60601"),("Hearthside Interiors","","","Boise","ID","83702"),("Arc & Line","Sam","Quinn","Boston","MA","02108")};
        foreach(var (company,first,last,city,state,postal) in names)list.Add(new(){Company=company,First=first,Last=last,Street=$"{100+list.Count*20} Example Avenue",City=city,State=state,Postal=postal,SourceRow=list.Count+2});
        SetContacts(list);workbook=null;map=null;SheetSelect.Visibility=Visibility.Collapsed;FileTitle.Text="Sample contacts";FileDetail.Text="Fictional sample · Import Excel to use your contacts";Refresh();
    }
    void Minimize_Click(object sender,RoutedEventArgs e)=>WindowState=WindowState.Minimized;
    void Maximize_Click(object sender,RoutedEventArgs e)=>WindowState=WindowState==WindowState.Maximized?WindowState.Normal:WindowState.Maximized;
    void Close_Click(object sender,RoutedEventArgs e)=>Close();
}
