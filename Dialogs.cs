using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FlareLabels;
public abstract class AppDialog : Window
{
    protected readonly StackPanel Body=new(){Margin=new(26,20,26,22)};
    protected readonly Border Footer=new(){Padding=new(24,16,24,16),Background=Brushes.White,BorderBrush=new SolidColorBrush(Color.FromRgb(225,230,218)),BorderThickness=new(0,1,0,0)};
    protected AppDialog(string title,double width,double height)
    {
        Title=title;Width=width;Height=height;MaxHeight=Math.Max(360,SystemParameters.WorkArea.Height-32);
        WindowStartupLocation=WindowStartupLocation.CenterOwner;ResizeMode=ResizeMode.NoResize;WindowStyle=WindowStyle.None;
        Background=new SolidColorBrush(Color.FromRgb(246,248,241));Foreground=(Brush)FindResource("Ink");FontFamily=new("Segoe UI");ShowInTaskbar=false;
        Icon=System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/Assets/Flare-Labels.ico"));
        System.Windows.Shell.WindowChrome.SetWindowChrome(this,new(){CaptionHeight=46,ResizeBorderThickness=new(0),GlassFrameThickness=new(0),CornerRadius=new(16)});
        WindowEffects.Attach(this);
        var root=new Grid{Background=Background};root.RowDefinitions.Add(new(){Height=new(46)});root.RowDefinitions.Add(new(){Height=new(1,GridUnitType.Star)});root.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var titlebar=new Grid{Background=(Brush)FindResource("Shell")};
        titlebar.Children.Add(new TextBlock{Text="Flare Tools  /  "+title,Foreground=Brushes.White,FontSize=12,FontWeight=FontWeights.SemiBold,Margin=new(22,0,62,0),VerticalAlignment=VerticalAlignment.Center,TextTrimming=TextTrimming.CharacterEllipsis});
        var close=new Button{Content="×",FontSize=18,Style=(Style)FindResource("ChromeButton"),HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Center,Margin=new(0,0,8,0),ToolTip="Close"};
        System.Windows.Shell.WindowChrome.SetIsHitTestVisibleInChrome(close,true);close.Click+=(_,_)=>DialogResult=false;titlebar.Children.Add(close);root.Children.Add(titlebar);
        var scroll=new ScrollViewer{Content=Body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};Grid.SetRow(scroll,1);root.Children.Add(scroll);Grid.SetRow(Footer,2);root.Children.Add(Footer);Content=root;
        PreviewKeyDown+=(_,e)=>{if(e.Key==System.Windows.Input.Key.Escape){DialogResult=false;e.Handled=true;}};
    }
    protected void Heading(string heading,string description)
    {
        Body.Children.Add(new TextBlock{Text=heading,FontSize=25,FontWeight=FontWeights.SemiBold});
        Body.Children.Add(new TextBlock{Text=description,FontSize=13,Foreground=(Brush)FindResource("Muted"),TextWrapping=TextWrapping.Wrap,LineHeight=20,Margin=new(0,8,0,8)});
    }
    protected void Label(string text)=>Body.Children.Add(new TextBlock{Text=text,Style=(Style)FindResource("FieldLabel")});
    protected TextBox Input(string label,string value)
    {
        Label(label);var box=new TextBox{Text=value};System.Windows.Automation.AutomationProperties.SetName(box,label);Body.Children.Add(box);return box;
    }
    protected void Actions(string primary,RoutedEventHandler handler)
    {
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};
        var cancel=new Button{Content="Cancel",Margin=new(0,0,10,0),IsCancel=true};cancel.Click+=(_,_)=>DialogResult=false;
        var save=new Button{Content=primary,Style=(Style)FindResource("Primary"),IsDefault=true};save.Click+=handler;
        buttons.Children.Add(cancel);buttons.Children.Add(save);Footer.Child=buttons;
    }
}
public enum AppChoice { Cancel,Primary,Secondary }
public sealed class ChoiceDialog : AppDialog
{
    public AppChoice Choice {get;private set;}
    internal ChoiceDialog(string title,string text,string primary,string? secondary=null):base(title,560,310)
    {
        Heading(title,text);
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};
        var cancel=new Button{Content="Cancel",Style=(Style)FindResource("Quiet"),IsCancel=true,Margin=new(0,0,8,0)};cancel.Click+=(_,_)=>DialogResult=false;buttons.Children.Add(cancel);
        if(secondary!=null){var other=new Button{Content=secondary,Margin=new(0,0,8,0)};other.Click+=(_,_)=>{Choice=AppChoice.Secondary;DialogResult=true;};buttons.Children.Add(other);}
        var action=new Button{Content=primary,Style=(Style)FindResource("Primary")};action.Click+=(_,_)=>{Choice=AppChoice.Primary;DialogResult=true;};buttons.Children.Add(action);Footer.Child=buttons;
    }
    public static AppChoice Ask(Window owner,string title,string text,string primary,string? secondary=null)
    {
        var dialog=new ChoiceDialog(title,text,primary,secondary){Owner=owner};dialog.ShowDialog();return dialog.Choice;
    }
}
public sealed class MappingDialog : AppDialog
{
    readonly SheetData sheet;readonly ComboBox header;
    readonly Grid fields=new(){Margin=new(0,10,0,0)};
    readonly Dictionary<string,ComboBox> selectors=new();
    ColumnMap proposed;
    public ColumnMap? Result{get;private set;}
    public MappingDialog(SheetData sheet,ColumnMap initial):base("Match spreadsheet columns",590,780)
    {
        this.sheet=sheet;proposed=initial;
        Heading("Match your columns","Choose the spreadsheet fields that belong on your mailing labels. Your original file stays unchanged.");
        Label("Header row");header=new ComboBox();
        for(int i=0;i<Math.Min(50,sheet.Rows.Count);i++)header.Items.Add($"Row {i+1}: "+string.Join(" · ",sheet.Rows[i].Take(4)));
        header.SelectedIndex=initial.HeaderRow;Body.Children.Add(header);
        fields.ColumnDefinitions.Add(new(){Width=new(190)});fields.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});
        Body.Children.Add(fields);BuildFields();
        header.SelectionChanged+=(_,_)=>{proposed=SpreadsheetReader.Detect(new(sheet.Name,sheet.Rows.Skip(header.SelectedIndex).ToList()));proposed.HeaderRow=header.SelectedIndex;BuildFields();};
        Body.Children.Add(new TextBlock{Text="* Required mailing field. Use company name, a full name, or both. A first name without a last name is omitted from company labels.",FontSize=11,TextWrapping=TextWrapping.Wrap,Foreground=new SolidColorBrush(Color.FromRgb(93,100,89)),LineHeight=17,Margin=new(0,14,0,0)});
        Actions("Use these columns",Save);
    }
    void BuildFields()
    {
        fields.Children.Clear();fields.RowDefinitions.Clear();selectors.Clear();
        var headers=sheet.Rows[Math.Max(0,header.SelectedIndex)];
        foreach(var field in SpreadsheetReader.Fields)
        {
            int r=fields.RowDefinitions.Count;fields.RowDefinitions.Add(new(){Height=GridLength.Auto});
            var label=new TextBlock{Text=field.Title+(field.Required?" *":""),VerticalAlignment=VerticalAlignment.Center,FontSize=12,Margin=new(0,6,10,6)};
            var combo=new ComboBox{Margin=new(0,5,0,5)};combo.Items.Add("— Not used —");
            for(int c=0;c<headers.Count;c++)combo.Items.Add($"{c+1}. {(headers[c].Length>0?headers[c]:"Unnamed column")}");
            combo.SelectedIndex=proposed.Columns.TryGetValue(field.Key,out int col)&&col<headers.Count?col+1:0;
            selectors[field.Key]=combo;Grid.SetRow(label,r);Grid.SetRow(combo,r);Grid.SetColumn(combo,1);fields.Children.Add(label);fields.Children.Add(combo);
        }
    }
    void Save(object sender,RoutedEventArgs e)
    {
        var cols=selectors.Where(x=>x.Value.SelectedIndex>0).ToDictionary(x=>x.Key,x=>x.Value.SelectedIndex-1);
        if(SpreadsheetReader.Fields.Any(f=>f.Required&&!cols.ContainsKey(f.Key))||!cols.ContainsKey("Company")&&!cols.ContainsKey("FullName")&&!(cols.ContainsKey("First")&&cols.ContainsKey("Last"))){MessageBox.Show(this,"Match the street, city, state/province and postal code fields, plus a company or full name.","Match required fields",MessageBoxButton.OK,MessageBoxImage.Information);return;}
        if(cols.Values.Distinct().Count()!=cols.Count){MessageBox.Show(this,"A spreadsheet column is matched to more than one field. Choose a separate column for each field, or mark an optional field as Not used.","Check column matches",MessageBoxButton.OK,MessageBoxImage.Information);return;}
        Result=new(){HeaderRow=header.SelectedIndex,Columns=cols};DialogResult=true;
    }
}
public sealed class ContactDialog : AppDialog
{
    readonly Contact target;readonly Dictionary<string,TextBox> boxes=new();readonly CheckBox include;
    public ContactDialog(Contact contact):base("Edit mailing label",620,760)
    {
        target=contact;Heading("Edit mailing label",$"Edit this label from spreadsheet row {contact.SourceRow}. Your source file stays unchanged.");
        boxes["Company"]=Input("Company name",contact.Company);
        Fields(("First","First name",contact.First),("Last","Last name",contact.Last));
        boxes["Street"]=Input("Street address",contact.Street);
        boxes["Street2"]=Input("Address line 2 · suite, unit or apartment",contact.Street2);
        Fields(("City","City",contact.City),("State","State / province",contact.State),("Postal","ZIP / postal code",contact.Postal));
        boxes["Country"]=Input("Country · optional for US addresses",contact.Country);
        Body.Children.Add(new TextBlock{Text="Company first, then first and last name when both are present. The complete address is centered on your label.",FontSize=11,LineHeight=18,Foreground=(Brush)FindResource("Muted"),TextWrapping=TextWrapping.Wrap,Margin=new(0,15,0,0)});
        include=new(){Content="Include this label",IsChecked=contact.Include,Margin=new(0,18,0,0)};Body.Children.Add(include);
        Actions("Save label",Save);
    }
    void Fields(params (string Key,string Label,string Value)[] entries)
    {
        var row=new Grid();
        for(int i=0;i<entries.Length;i++)
        {
            row.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});
            var field=new StackPanel{Margin=new(i==0?0:6,0,i==entries.Length-1?0:6,0)};
            field.Children.Add(new TextBlock{Text=entries[i].Label,Style=(Style)FindResource("FieldLabel")});
            var box=new TextBox{Text=entries[i].Value};System.Windows.Automation.AutomationProperties.SetName(box,entries[i].Label);
            boxes[entries[i].Key]=box;field.Children.Add(box);Grid.SetColumn(field,i);row.Children.Add(field);
        }
        Body.Children.Add(row);
    }
    void Save(object sender,RoutedEventArgs e)
    {
        foreach(var(k,box) in boxes)typeof(Contact).GetProperty(k)!.SetValue(target,ContactRules.Clean(box.Text));
        target.Include=include.IsChecked==true;target.DuplicateReviewed=false;DialogResult=true;
    }
}
public sealed class PrintSettingsDialog : AppDialog
{
    readonly LabelOptions options;readonly TextBox horizontal,vertical;readonly CheckBox autoUpdates;
    public PrintSettingsDialog(LabelOptions options):base("Print & app settings",540,700)
    {
        this.options=options;Heading("Print settings","Your template uses 4 × 2-inch labels, two columns and five rows, on US Letter paper.");
        Body.Children.Add(new TextBlock{Text="In Word, print at 100% scale with one page per sheet and single-sided printing. Try a plain-paper test before loading stickers.",FontSize=13,LineHeight=20,TextWrapping=TextWrapping.Wrap,Margin=new(0,16,0,12)});
        horizontal=Input("Horizontal adjustment (inches)",options.OffsetX.ToString("0.000",CultureInfo.InvariantCulture));
        vertical=Input("Vertical adjustment (inches)",options.OffsetY.ToString("0.000",CultureInfo.InvariantCulture));
        Body.Children.Add(new TextBlock{Text="Use only if your printer shifts the entire sheet. Positive values move right or down. Range: −0.150 to +0.150 inches.",FontSize=11,LineHeight=18,Foreground=new SolidColorBrush(Color.FromRgb(93,100,89)),TextWrapping=TextWrapping.Wrap,Margin=new(0,12,0,0)});
        var reset=new Button{Content="Reset adjustments",HorizontalAlignment=HorizontalAlignment.Left,Margin=new(0,14,0,0),FontSize=11,Padding=new(12,7,12,7)};reset.Click+=(_,_)=>{horizontal.Text="0.000";vertical.Text="0.000";};Body.Children.Add(reset);
        autoUpdates=new CheckBox{Content="Check for updates when the app opens",IsChecked=options.AutoCheckUpdates,Margin=new(0,18,0,0)};Body.Children.Add(autoUpdates);
        Actions("Save settings",Save);
    }
    void Save(object sender,RoutedEventArgs e)
    {
        if(!double.TryParse(horizontal.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out var x)||!double.TryParse(vertical.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out var y)||!double.IsFinite(x)||!double.IsFinite(y)||Math.Abs(x)>.15||Math.Abs(y)>.15){MessageBox.Show(this,"Enter adjustments between −0.150 and +0.150 inches.","Check print adjustment",MessageBoxButton.OK,MessageBoxImage.Information);return;}
        options.OffsetX=x;options.OffsetY=y;options.AutoCheckUpdates=autoUpdates.IsChecked==true;DialogResult=true;
    }
}
