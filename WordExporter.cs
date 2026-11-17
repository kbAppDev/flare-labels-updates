using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Xml.Linq;

namespace FlareLabels;

public sealed record LabelLayout(List<string> Lines,double FontSize);
public static class LabelEngine
{
    public const double TextWidth=326.53;
    public const double TextHeight=170;
    static double Measure(string text,double size)=>new FormattedText(text,CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("Arial"),size*96/72,Brushes.Black,1).WidthIncludingTrailingWhitespace;
    public static LabelLayout Layout(Contact contact,double preferredFont)
    {
        for(double size=preferredFont;size>=9.99;size-=0.5)
        {
            var lines=new List<string>();bool tooWide=false;
            foreach(var original in contact.AddressLines())
            {
                string line="";
                foreach(var word in original.Split(' ',StringSplitOptions.RemoveEmptyEntries))
                {
                    if(Measure(word,size)>TextWidth){tooWide=true;break;}
                    string candidate=line.Length>0?line+" "+word:word;
                    if(Measure(candidate,size)>TextWidth){lines.Add(line);line=word;}else line=candidate;
                }
                if(line.Length>0)lines.Add(line);
            }
            if(!tooWide&&lines.Count*size*96/72*1.15<=TextHeight)return new(lines,size);
        }
        throw new InvalidDataException($"The label for {contact.Recipient} is too long. Edit this address or shorten the company name before exporting.");
    }
    public static int PageCount(int labels,int startPosition)=>labels==0?0:(labels+startPosition-1+9)/10;
}

public static class WordExporter
{
    static readonly XNamespace W="http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    static XElement E(string name,params object[] values)=>new(W+name,values);
    static XAttribute A(string name,object value)=>new(W+name,value);
    public static void Export(string path,IReadOnlyList<Contact> contacts,LabelOptions options)
    {
        if(contacts.Count==0)throw new InvalidDataException("Select at least one contact first.");
        if(contacts.Any(c=>!c.IsValid))throw new InvalidDataException("Complete the selected addresses or uncheck them before exporting.");
        var layouts=contacts.Select(c=>LabelEngine.Layout(c,options.FontSize)).ToList();
        using var source=Assembly.GetExecutingAssembly().GetManifestResourceStream("FlareLabels.Assets.Label-Template.docx")??throw new InvalidOperationException("The built-in label template is missing.");
        using var template=new ZipArchive(source,ZipArchiveMode.Read);
        using var documentStream=template.GetEntry("word/document.xml")!.Open();
        var doc=XDocument.Load(documentStream);
        var body=doc.Root!.Element(W+"body")!;
        var table=new XElement(body.Element(W+"tbl")!);
        var section=new XElement(body.Element(W+"sectPr")!);
        var margins=section.Element(W+"pgMar")!;
        int dx=(int)Math.Round(options.OffsetX*1440),dy=(int)Math.Round(options.OffsetY*1440);
        foreach(var (key,delta) in new[]{("left",dx),("right",-dx),("top",dy),("bottom",-dy)})margins.SetAttributeValue(W+key,(int)margins.Attribute(W+key)!+delta);
        body.RemoveNodes();
        int pages=LabelEngine.PageCount(contacts.Count,options.StartPosition);
        int bookmark=0;
        for(int page=0;page<pages;page++)
        {
            var grid=new XElement(table);int slot=0;
            foreach(var row in grid.Elements(W+"tr"))
            {
                var cells=row.Elements(W+"tc").ToList();
                foreach(int col in new[]{0,2})
                {
                    var cell=cells[col];var paragraph=cell.Element(W+"p")!;
                    paragraph.RemoveNodes();
                    paragraph.Add(E("pPr",E("pStyle",A("val","AveryStyle1")),E("spacing",A("before",0),A("after",0),A("line",240),A("lineRule","auto")),E("jc",A("val","center"))));
                    var valign=cell.Element(W+"tcPr")!.Element(W+"vAlign");
                    if(valign==null)cell.Element(W+"tcPr")!.Add(E("vAlign",A("val","center")));else valign.SetAttributeValue(W+"val","center");
                    int index=page*10+slot++-(options.StartPosition-1);
                    if(index<0||index>=contacts.Count)continue;
                    var layout=layouts[index];int halfPoints=(int)Math.Round(layout.FontSize*2);
                    paragraph.Element(W+"pPr")!.Element(W+"spacing")!.ReplaceWith(E("spacing",A("before",0),A("after",0),A("line",(int)Math.Round(layout.FontSize*1.15*20)),A("lineRule","exact")));
                    paragraph.Add(E("bookmarkStart",A("id",bookmark),A("name","FlareLabel"+bookmark)));
                    var run=E("r",E("rPr",E("rFonts",A("ascii","Arial"),A("hAnsi","Arial")),E("color",A("val","000000")),E("sz",A("val",halfPoints)),E("szCs",A("val",halfPoints))));
                    for(int i=0;i<layout.Lines.Count;i++){if(i>0)run.Add(E("br"));run.Add(E("t",layout.Lines[i]));}
                    paragraph.Add(run,E("bookmarkEnd",A("id",bookmark++)));
                }
            }
            body.Add(grid);
            var end=E("p",E("pPr",E("spacing",A("before",0),A("after",0),A("line",20),A("lineRule","exact"))));
            if(page<pages-1){var next=new XElement(section);next.Element(W+"type")?.Remove();next.AddFirst(E("type",A("val","nextPage")));end.Element(W+"pPr")!.Add(next);}
            body.Add(end);
        }
        body.Add(new XElement(section));
        string fullPath=Path.GetFullPath(path);string tempPath=fullPath+"."+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            using(var output=ZipFile.Open(tempPath,ZipArchiveMode.Create))
            {
                foreach(var entry in template.Entries)
                {
                    var item=output.CreateEntry(entry.FullName,CompressionLevel.Optimal);using var stream=item.Open();
                    if(entry.FullName=="word/document.xml")doc.Save(stream);
                    else {using var input=entry.Open();input.CopyTo(stream);}
                }
            }
            File.Move(tempPath,fullPath,true);
        }
        finally {if(File.Exists(tempPath))File.Delete(tempPath);}
    }
}
