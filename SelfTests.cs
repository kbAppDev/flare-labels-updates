using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;
using System.Security.Cryptography;
using System.Text;

namespace FlareLabels;
public static class SelfTests
{
    public static void Run(string[] args)
    {
        int at=Array.IndexOf(args,"--self-test");string input=args[at+1];string output=args[at+2];Directory.CreateDirectory(output);
        var checks=new List<string>();
        void Check(bool condition,string description){if(!condition)throw new InvalidOperationException(description);checks.Add(description);}
        System.Windows.Application.Current.ShutdownMode=System.Windows.ShutdownMode.OnExplicitShutdown;
        var window=new MainWindow();
        Check(window.Title=="Flare Labels","Loads the complete desktop interface and packaged icon");
        window.LoadDemo();
        var surface=(System.Windows.FrameworkElement)window.Content;
        surface.Measure(new System.Windows.Size(1400,900));
        surface.Arrange(new System.Windows.Rect(0,0,1400,900));surface.UpdateLayout();
        var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap(1400,900,96,96,System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(surface);
        var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using(var image=File.Create(Path.Combine(output,"interface-review.png")))encoder.Save(image);
        var export=(System.Windows.Controls.Button)window.FindName("ExportButton");
        Check(export.IsEnabled&&export.Foreground is System.Windows.Media.SolidColorBrush b&&b.Color==System.Windows.Media.Colors.White,"Export action remains readable with white text on the active button");
        var book=SpreadsheetReader.Read(input);var sheet=book.Sheets[0];var map=SpreadsheetReader.Detect(sheet);var contacts=SpreadsheetReader.Contacts(sheet,map);
        Check(map.HeaderRow==1,"Detects title row before spreadsheet headers");
        Check(contacts.Count==20,"Imports all 20 source contacts without blank rows");
        Check(contacts.All(c=>c.IsValid),"All 20 source addresses are complete");
        Check(contacts.Count(c=>c.FullName.Length>0)==11,"Includes 11 complete first and last names");
        var partialName=new Contact{Company="Example Studio",First="Single",Last="",Street="100 Example St",City="Austin",State="TX",Postal="78701"};
        Check(!partialName.AddressLines().Contains("Single"),"Falls back to company when only a first name exists");
        Check(contacts.All(c=>!c.AddressLines().Any(s=>s.Contains('@'))),"Excludes email and phone metadata from labels");
        var original=Path.Combine(output,"Centered-Source-Labels.docx");WordExporter.Export(original,contacts,new());
        var ns=(XNamespace)"http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        XDocument Doc(string path){using var z=ZipFile.OpenRead(path);using var stream=z.GetEntry("word/document.xml")!.Open();return XDocument.Load(stream);}
        var xml=Doc(original);var tables=xml.Descendants(ns+"tbl").ToList();
        Check(tables.Count==2,"Creates exactly two label tables for 20 contacts");
        Check(xml.Descendants(ns+"bookmarkStart").Count()==20,"Exports every selected contact exactly once");
        Check(xml.Descendants(ns+"p").Where(p=>p.Descendants(ns+"t").Any()).All(p=>(string?)p.Element(ns+"pPr")?.Element(ns+"jc")?.Attribute(ns+"val")=="center"),"Centers every printed label horizontally");
        Check(xml.Descendants(ns+"tc").Where(c=>c.Descendants(ns+"t").Any()).All(c=>(string?)c.Element(ns+"tcPr")?.Element(ns+"vAlign")?.Attribute(ns+"val")=="center"),"Centers every printed label vertically");
        Check(tables.All(t=>t.Elements(ns+"tr").Count()==5&&t.Descendants(ns+"trHeight").All(h=>(int)h.Attribute(ns+"val")! ==2880)),"Preserves exact two-inch row height and five-row grid");
        Check(!xml.Descendants(ns+"drawing").Any(),"Removes printable template guide outlines");
        WordExporter.Export(Path.Combine(output,"Partial-Sheet-Labels.docx"),contacts,new(){StartPosition=7});
        var partial=Doc(Path.Combine(output,"Partial-Sheet-Labels.docx"));
        Check(partial.Descendants(ns+"tbl").Count()==3,"Starts at label seven and correctly adds a third sheet");
        Check(partial.Descendants(ns+"tbl").First().Elements(ns+"tr").Take(3).All(r=>!r.Descendants(ns+"t").Any()),"Leaves the first six used labels empty");
        var first=contacts[0].Copy();var duplicate=first.Copy();duplicate.First="Another";ContactRules.MarkDuplicates([first,duplicate]);
        Check(first.IsDuplicate&&duplicate.IsDuplicate,"Flags same company/address even with different contact names");
        var missing=first.Copy();missing.Postal="";bool refused=false;
        try{WordExporter.Export(Path.Combine(output,"Invalid.docx"),[missing],new());}catch(InvalidDataException){refused=true;}
        Check(refused,"Blocks incomplete selected addresses instead of silently omitting them");
        var leadingSheet=new SheetData("ZIP",[["Company","Address","City","State","Zip"],["Example","10 Example St","Boston","MA","2108"]]);
        Check(SpreadsheetReader.Contacts(leadingSheet,SpreadsheetReader.Detect(leadingSheet))[0].Postal=="02108","Restores leading zeros for US ZIP codes");
        var formattedFile=Path.Combine(output,"Formatted-Zip.xlsx");
        using(var z=ZipFile.Open(formattedFile,ZipArchiveMode.Create))
        {
            void Add(string name,string value){using var w=new StreamWriter(z.CreateEntry(name).Open());w.Write(value);}
            Add("xl/workbook.xml","<workbook xmlns='http://schemas.openxmlformats.org/spreadsheetml/2006/main' xmlns:r='http://schemas.openxmlformats.org/officeDocument/2006/relationships'><sheets><sheet name='ZIP' sheetId='1' r:id='rId1'/></sheets></workbook>");
            Add("xl/_rels/workbook.xml.rels","<Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'><Relationship Id='rId1' Target='worksheets/sheet1.xml'/></Relationships>");
            Add("xl/styles.xml","<styleSheet xmlns='http://schemas.openxmlformats.org/spreadsheetml/2006/main'><numFmts><numFmt numFmtId='164' formatCode='00000'/></numFmts><cellXfs><xf numFmtId='0'/><xf numFmtId='164'/></cellXfs></styleSheet>");
            Add("xl/worksheets/sheet1.xml","<worksheet xmlns='http://schemas.openxmlformats.org/spreadsheetml/2006/main'><sheetData><row r='1'><c r='A1' s='1'><v>2108</v></c></row></sheetData></worksheet>");
        }
        Check(SpreadsheetReader.Read(formattedFile).Sheets[0].Rows[0][0]=="02108","Reads numeric ZIP codes with Excel zero-padding formats");
        var longContact=first.Copy();longContact.Company="A long architectural studio company name with several divisions and locations";
        longContact.Street="12345 A Very Long Street Name That Needs To Wrap Across Several Lines Suite 12345";
        var fitted=LabelEngine.Layout(longContact,18);
        Check(fitted.FontSize>=10&&fitted.FontSize<=18&&fitted.Lines.Count*fitted.FontSize*96/72*1.15<=LabelEngine.TextHeight,"Fits long labels without clipping or dropping text");
        Check(contacts.All(c=>LabelEngine.Layout(c,16).FontSize==16),"Original contact labels fit at the template's 16-point size");
        var timer=Stopwatch.StartNew();
        var many=Enumerable.Range(0,250).Select(i=>contacts[i%20].Copy()).ToList();
        WordExporter.Export(Path.Combine(output,"Large-Batch-Labels.docx"),many,new());timer.Stop();
        Check(Doc(Path.Combine(output,"Large-Batch-Labels.docx")).Descendants(ns+"tbl").Count()==25,"Paginates a 250-contact batch into 25 sheets");
        using var productionKey=RSA.Create();productionKey.ImportFromPem(UpdateService.PublicKey());
        Check(productionKey.KeySize>=3072,"Packages the dedicated Flare Labels update verification key");
        using var signingKey=RSA.Create(2048);using var differentKey=RSA.Create(2048);
        var payload=Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new{schema=1,application="FlareLabels",version="9.9.9",installerUrl=$"https://github.com/{UpdateService.Repository}/releases/download/v9.9.9/Flare-Labels-Setup-9.9.9.exe",installerSize=1024,sha256=new string('a',64)}));
        string signed=JsonSerializer.Serialize(new{algorithm="RS256",payload=Convert.ToBase64String(payload),signature=Convert.ToBase64String(signingKey.SignData(payload,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1))});
        var verified=UpdateService.VerifyManifest(signed,"9.9.9",signingKey.ExportSubjectPublicKeyInfoPem());
        Check(verified.Version=="9.9.9","Accepts an authentic version-matched update manifest");
        bool badKey=false;try{UpdateService.VerifyManifest(signed,"9.9.9",differentKey.ExportSubjectPublicKeyInfoPem());}catch(InvalidDataException){badKey=true;}
        Check(badKey,"Rejects update manifests signed by another key");
        bool badVersion=false;try{UpdateService.VerifyManifest(signed,"9.9.8",signingKey.ExportSubjectPublicKeyInfoPem());}catch(InvalidDataException){badVersion=true;}
        Check(badVersion,"Rejects a signed manifest that does not match the release version");
        bool badSize=false;try{UpdateService.VerifyDownloadedContent(1023,new string('a',64),verified);}catch(InvalidDataException){badSize=true;}
        Check(badSize,"Rejects incomplete installer downloads");
        bool badHash=false;try{UpdateService.VerifyDownloadedContent(1024,new string('b',64),verified);}catch(InvalidDataException){badHash=true;}
        Check(badHash,"Rejects installer checksum mismatches before launch");
        File.WriteAllText(Path.Combine(output,"test-results.json"),JsonSerializer.Serialize(new{passed=checks.Count,checks,batch250milliseconds=timer.ElapsedMilliseconds},new JsonSerializerOptions{WriteIndented=true}));
        window.Close();
    }
}
