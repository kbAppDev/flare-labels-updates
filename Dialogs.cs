using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FlareLabels;
public abstract class AppDialog : Window
{
    protected readonly StackPanel Body=new(){Margin=new(26,23,26,23)};
    protected AppDialog(string title,double width,double height)
    {
        Title=title;Width=width;Height=height;WindowStartupLocation=WindowStartupLocation.CenterOwner;ResizeMode=ResizeMode.NoResize;Background=new SolidColorBrush(Color.FromRgb(247,248,250));FontFamily=new("Segoe UI");ShowInTaskbar=false;
        Content=new ScrollViewer{Content=Body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
    }
    protected void Heading(string heading,string description)
    {
        Body.Children.Add(new TextBlock{Text=heading,FontSize=22,FontWeight=FontWeights.SemiBold});
        Body.Children.Add(new TextBlock{Text=description,FontSize=12,Foreground=new SolidColorBrush(Color.FromRgb(79,96,117)),TextWrapping=TextWrapping.Wrap,LineHeight=18,Margin=new(0,8,0,8)});
    }
    protected void Label(string text)=>Body.Children.Add(new TextBlock{Text=text,Style=(Style)FindResource("FieldLabel")});
    protected TextBox Input(string label,string value)
    {
        Label(label);var box=new TextBox{Text=value};Body.Children.Add(box);return box;
    }
    protected void Actions(string primary,RoutedEventHandler handler)
    {
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new(0,24,0,0)};
        var cancel=new Button{Content="Cancel",Margin=new(0,0,8,0),IsCancel=true};cancel.Click+=(_,_)=>DialogResult=false;
        var save=new Button{Content=primary,Style=(Style)FindResource("Primary"),IsDefault=true};save.Click+=handler;
        buttons.Children.Add(cancel);buttons.Children.Add(save);Body.Children.Add(buttons);
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
        Body.Children.Add(new TextBlock{Text="* Required mailing field. Use company name, a full name, or both. A first name without a last name is omitted from company labels.",FontSize=11,TextWrapping=TextWrapping.Wrap,Foreground=new SolidColorBrush(Color.FromRgb(79,96,117)),LineHeight=17,Margin=new(0,14,0,0)});
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
    public ContactDialog(Contact contact):base("Edit mailing label",530,790)
    {
        target=contact;Heading("Edit mailing label",$"Source row {contact.SourceRow}. Changes apply to this label session.");
        foreach(var(key,label,value) in new[]{("Company","Company name",contact.Company),("First","First name",contact.First),("Last","Last name",contact.Last),("Street","Street address",contact.Street),("Street2","Address line 2",contact.Street2),("City","City",contact.City),("State","State / province",contact.State),("Postal","ZIP / postal code",contact.Postal),("Country","Country (optional)",contact.Country)})boxes[key]=Input(label,value);
        include=new(){Content="Include this label",IsChecked=contact.Include,Margin=new(0,20,0,0)};Body.Children.Add(include);
        Actions("Save label",Save);
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
    public PrintSettingsDialog(LabelOptions options):base("Print settings",480,570)
    {
        this.options=options;Heading("Print settings","Your template uses 4 × 2-inch labels, two columns and five rows, on US Letter paper.");
        Body.Children.Add(new TextBlock{Text="In Word, print at 100% scale with one page per sheet and single-sided printing. Try a plain-paper test before loading stickers.",FontSize=13,LineHeight=20,TextWrapping=TextWrapping.Wrap,Margin=new(0,16,0,12)});
        horizontal=Input("Horizontal adjustment (inches)",options.OffsetX.ToString("0.000",CultureInfo.InvariantCulture));
        vertical=Input("Vertical adjustment (inches)",options.OffsetY.ToString("0.000",CultureInfo.InvariantCulture));
        Body.Children.Add(new TextBlock{Text="Use only if your printer shifts the entire sheet. Positive values move right or down. Range: −0.150 to +0.150 inches.",FontSize=11,LineHeight=18,Foreground=new SolidColorBrush(Color.FromRgb(79,96,117)),TextWrapping=TextWrapping.Wrap,Margin=new(0,12,0,0)});
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
