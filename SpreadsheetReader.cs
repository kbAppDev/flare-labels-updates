using System.IO;
using System.IO.Compression;
using System.Xml.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace FlareLabels;

public sealed record SheetData(string Name,List<List<string>> Rows);
public sealed record WorkbookData(string FileName,List<SheetData> Sheets);
public sealed record Field(string Key,string Title,bool Required,string[] Aliases);
public sealed class ColumnMap
{
    public int HeaderRow { get; set; }
    public Dictionary<string,int> Columns { get; set; }=new();
}

public static class SpreadsheetReader
{
    public static readonly Field[] Fields = [
        new("Company","Company name",false,["company","companyname","business","businessname","organization","organisation","accountname"]),
        new("First","First name",false,["firstname","first","givenname","contactfirstname"]),
        new("Last","Last name",false,["lastname","last","surname","familyname","contactlastname"]),
        new("FullName","Full name",false,["fullname","contactname","contactfullname"]),
        new("Street","Street address",true,["streetaddress","address","street","address1","addressline1","mailingstreet","mailingaddress","shippingaddress","shippingstreet"]),
        new("Street2","Address line 2",false,["address2","addressline2","street2","streetaddress2","suite","unit","apt","apartment"]),
        new("City","City",true,["city","town","mailingcity","shippingcity"]),
        new("State","State / province",true,["state","province","stateprovince","region","mailingstate","shippingstate"]),
        new("Postal","ZIP / postal code",true,["postalcode","zip","zipcode","postal","zippostalcode","mailingzip","mailingpostalcode","shippingpostalcode"]),
        new("Country","Country",false,["country","countryregion","mailingcountry","shippingcountry"])
    ];
    static readonly XNamespace S="http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    static readonly XNamespace R="http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    public static WorkbookData Read(string path)
    {
        var info=new FileInfo(path);
        if(info.Length>75*1024*1024) throw new InvalidDataException("Please use a spreadsheet smaller than 75 MB.");
        if(Path.GetExtension(path).Equals(".csv",StringComparison.OrdinalIgnoreCase)) return new(Path.GetFileName(path),[new("Contacts",ReadCsv(File.ReadAllText(path)))]);
        using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite);
        using var zip=new ZipArchive(stream,ZipArchiveMode.Read);
        if(zip.Entries.Sum(e=>e.Length)>250L*1024*1024)throw new InvalidDataException("The expanded spreadsheet is too large. Please export only the contact sheet.");
        XDocument Xml(string entry)
        {
            var item=zip.GetEntry(entry)??throw new InvalidDataException("This workbook is missing "+entry+". Save it as a standard .xlsx file and try again.");
            using var input=item.Open();return XDocument.Load(input);
        }
        var strings=zip.GetEntry("xl/sharedStrings.xml") is not null?Xml("xl/sharedStrings.xml").Descendants(S+"si").Select(x=>string.Concat(x.Descendants(S+"t").Select(t=>t.Value))).ToList():[];
        var formats=new Dictionary<int,string>();var styles=new List<int>();
        if(zip.GetEntry("xl/styles.xml") is not null)
        {
            var st=Xml("xl/styles.xml");
            foreach(var f in st.Descendants(S+"numFmt")) formats[(int?)f.Attribute("numFmtId")??0]=(string?)f.Attribute("formatCode")??"";
            styles=st.Root?.Element(S+"cellXfs")?.Elements(S+"xf").Select(x=>(int?)x.Attribute("numFmtId")??0).ToList()??[];
        }
        var relations=Xml("xl/_rels/workbook.xml.rels").Root!.Elements().Where(x=>(string?)x.Attribute("TargetMode")!="External").ToDictionary(x=>(string)x.Attribute("Id")!,x=>(string)x.Attribute("Target")!);
        var sheets=new List<SheetData>();
        foreach(var sheet in Xml("xl/workbook.xml").Descendants(S+"sheet"))
        {
            var id=(string?)sheet.Attribute(R+"id");if(id==null||!relations.TryGetValue(id,out var target))continue;
            var entry=target.StartsWith('/')?target.TrimStart('/'):new Uri(new Uri("https://local/xl/workbook.xml"),target).AbsolutePath.TrimStart('/');
            var content=Xml(entry);var rows=new List<List<string>>();
            foreach(var row in content.Descendants(S+"sheetData").Elements(S+"row"))
            {
                int rowIndex=(int?)row.Attribute("r")??rows.Count+1;
                if(rowIndex>100001)throw new InvalidDataException("Please keep each contact sheet under 100,000 rows.");
                while(rows.Count<rowIndex)rows.Add([]);
                var values=rows[rowIndex-1];int sequential=0;
                foreach(var cell in row.Elements(S+"c"))
                {
                    string reference=(string?)cell.Attribute("r")??"";
                    int col=reference.Length>0?ColumnIndex(reference):sequential;
                    sequential=col+1;if(col>1023)continue;
                    while(values.Count<=col)values.Add("");
                    string type=(string?)cell.Attribute("t")??"";
                    string value=cell.Element(S+"v")?.Value??"";
                    if(type=="s")value=int.TryParse(value,out int index)&&index>=0&&index<strings.Count?strings[index]:"";
                    else if(type=="inlineStr")value=string.Concat(cell.Descendants(S+"t").Select(t=>t.Value));
                    else if(type=="e")value="";
                    else if(type!="str")
                    {
                        int style=(int?)cell.Attribute("s")??0;
                        if(style<styles.Count&&formats.TryGetValue(styles[style],out var fmt)&&Regex.IsMatch(fmt,@"^0{2,10}$")&&long.TryParse(value,out var n)) value=n.ToString(new string('0',fmt.Length));
                    }
                    values[col]=ContactRules.Clean(value);
                }
            }
            while(rows.Count>0 && rows[^1].All(string.IsNullOrWhiteSpace))rows.RemoveAt(rows.Count-1);
            if(rows.Any(r=>r.Any(v=>v.Length>0))) sheets.Add(new((string?)sheet.Attribute("name")??"Sheet",rows));
        }
        if(sheets.Count==0)throw new InvalidDataException("No contact data was found. If this file is password protected, save an unprotected .xlsx copy first.");
        return new(Path.GetFileName(path),sheets);
    }
    static int ColumnIndex(string reference)
    {
        int n=0;foreach(char ch in reference){if(!char.IsLetter(ch))break;n=n*26+(char.ToUpperInvariant(ch)-'A'+1);}return n-1;
    }
    static List<List<string>> ReadCsv(string content)
    {
        var result=new List<List<string>>();var row=new List<string>();var field=new StringBuilder();bool quoted=false;
        char delimiter=content.Split('\n').FirstOrDefault()?.Count(c=>c==';')>content.Split('\n').FirstOrDefault()?.Count(c=>c==',')?';':',';
        for(int i=0;i<content.Length;i++)
        {
            char ch=content[i];
            if(ch=='"'){if(quoted&&i+1<content.Length&&content[i+1]=='"'){field.Append('"');i++;}else quoted=!quoted;}
            else if(ch==delimiter&&!quoted){row.Add(ContactRules.Clean(field.ToString()));field.Clear();}
            else if((ch=='\n'||ch=='\r')&&!quoted){if(ch=='\r'&&i+1<content.Length&&content[i+1]=='\n')i++;row.Add(ContactRules.Clean(field.ToString()));field.Clear();result.Add(row);row=[];}
            else field.Append(ch);
        }
        if(field.Length>0||row.Count>0){row.Add(ContactRules.Clean(field.ToString()));result.Add(row);}
        return result;
    }
    public static string Normalize(string value)=>Regex.Replace(value.ToLowerInvariant(),@"[^a-z0-9]","");
    public static ColumnMap Detect(SheetData sheet)
    {
        var best=new ColumnMap();int bestScore=-1;
        for(int i=0;i<Math.Min(50,sheet.Rows.Count);i++)
        {
            var columns=new Dictionary<string,int>();
            for(int j=0;j<sheet.Rows[i].Count;j++)
            {
                string key=Normalize(sheet.Rows[i][j]);
                var field=Fields.FirstOrDefault(f=>f.Aliases.Contains(key));
                if(field!=null&&!columns.ContainsKey(field.Key))columns[field.Key]=j;
            }
            int score=columns.Count+columns.Keys.Count(k=>Fields.First(f=>f.Key==k).Required)*2;
            if(score>bestScore){bestScore=score;best=new(){HeaderRow=i,Columns=columns};}
        }
        return best;
    }
    public static List<Contact> Contacts(SheetData sheet,ColumnMap map)
    {
        var result=new List<Contact>();
        for(int i=map.HeaderRow+1;i<sheet.Rows.Count;i++)
        {
            var row=sheet.Rows[i];
            string Get(string k)=>map.Columns.TryGetValue(k,out var c)&&c>=0&&c<row.Count?ContactRules.Clean(row[c]):"";
            if(!map.Columns.Values.Any(c=>c>=0&&c<row.Count&&row[c].Length>0))continue;
            var first=Get("First");var last=Get("Last");var full=Get("FullName");
            if(first.Length==0&&last.Length==0&&full.Length>0)
            {
                if(full.Contains(',')){var parts=full.Split(',',2);last=parts[0].Trim();first=parts[1].Trim();}
                else {var parts=full.Split(' ',2,StringSplitOptions.RemoveEmptyEntries);if(parts.Length==2){first=parts[0];last=parts[1];}}
            }
            string postal=Get("Postal");
            if(Regex.IsMatch(postal,@"^\d{1,4}$") && (Get("Country").Length==0||new[]{"US","USA","UNITED STATES"}.Contains(Get("Country").ToUpperInvariant())) && Regex.IsMatch(Get("State"),@"^[A-Za-z]{2}$"))postal=postal.PadLeft(5,'0');
            result.Add(new(){Company=Get("Company"),First=first,Last=last,Street=Get("Street"),Street2=Get("Street2"),City=Get("City"),State=Get("State"),Postal=postal,Country=Get("Country"),SourceRow=i+1});
        }
        ContactRules.MarkDuplicates(result);return result;
    }
}
