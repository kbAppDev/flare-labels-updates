using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace FlareLabels;

public sealed class Contact : INotifyPropertyChanged
{
    string company="", first="", last="", street="", street2="", city="", state="", postal="", country="";
    bool include=true, duplicate, duplicateReviewed;
    public string Company { get=>company; set=>Set(ref company,value); }
    public string First { get=>first; set=>Set(ref first,value); }
    public string Last { get=>last; set=>Set(ref last,value); }
    public string Street { get=>street; set=>Set(ref street,value); }
    public string Street2 { get=>street2; set=>Set(ref street2,value); }
    public string City { get=>city; set=>Set(ref city,value); }
    public string State { get=>state; set=>Set(ref state,value); }
    public string Postal { get=>postal; set=>Set(ref postal,value); }
    public string Country { get=>country; set=>Set(ref country,value); }
    public bool Include { get=>include; set=>Set(ref include,value); }
    public bool IsDuplicate { get=>duplicate; set=>Set(ref duplicate,value); }
    public bool DuplicateReviewed { get=>duplicateReviewed; set=>Set(ref duplicateReviewed,value); }
    public int SourceRow { get; set; }
    public string FullName => string.IsNullOrWhiteSpace(First)||string.IsNullOrWhiteSpace(Last) ? "" : First+" "+Last;
    public string Recipient => Company.Length>0?Company:FullName;
    public string Location => string.Join(", ",new[]{City,State}.Where(s=>s.Length>0));
    public string NameDisplay => FullName.Length>0?FullName:"Company only";
    public string Missing => string.Join(", ",new[]{ Recipient.Length==0?"company or full name":null, Street.Length==0?"street":null, City.Length==0?"city":null, State.Length==0?"state / province":null, Postal.Length==0?"postal code":null }.Where(s=>s!=null));
    public bool IsValid => Missing.Length==0;
    public string Status => !IsValid?"Missing "+Missing:IsDuplicate&&!DuplicateReviewed?"Review duplicate":IsDuplicate?"Duplicate reviewed":"Ready";
    public string DuplicateKey => string.Join("|", new[]{Recipient,Street,Street2,City,State,Postal,Country}.Select(s=>Regex.Replace(s.ToUpperInvariant(),@"[^\p{L}\p{N}]","")));
    public List<string> AddressLines()
    {
        var lines=new List<string>();
        if(Company.Length>0) lines.Add(Company);
        if(FullName.Length>0) lines.Add(FullName);
        foreach(var line in Regex.Split(Street,@",?\s+(?=(?:Suite|Ste)\.?\s)",RegexOptions.IgnoreCase)) if(line.Length>0) lines.Add(line);
        if(Street2.Length>0) lines.Add(Street2);
        lines.Add($"{City}, {State} {Postal}".Trim(' ',','));
        if(Country.Length>0 && !new[]{"US","USA","UNITED STATES","UNITED STATES OF AMERICA"}.Contains(Country.ToUpperInvariant())) lines.Add(Country);
        return lines;
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    void Set<T>(ref T target,T value,[CallerMemberName]string name="")
    {
        if(EqualityComparer<T>.Default.Equals(target,value))return;
        target=value; PropertyChanged?.Invoke(this,new(name));
        foreach(var p in new[]{"FullName","Recipient","NameDisplay","Location","Missing","IsValid","Status","DuplicateKey"}) PropertyChanged?.Invoke(this,new(p));
    }
    public Contact Copy()=>new(){Company=Company,First=First,Last=Last,Street=Street,Street2=Street2,City=City,State=State,Postal=Postal,Country=Country,Include=Include,SourceRow=SourceRow,IsDuplicate=IsDuplicate,DuplicateReviewed=DuplicateReviewed};
}

public sealed class LabelOptions
{
    public double FontSize { get; set; }=16;
    public int StartPosition { get; set; }=1;
    public double OffsetX { get; set; }
    public double OffsetY { get; set; }
    public string LastExportDirectory { get; set; }="";
    public bool AutoCheckUpdates { get; set; }=true;
    public DateTime LastUpdateCheckUtc { get; set; }
}

public static class ContactRules
{
    public static string Clean(string? v)=>Regex.Replace(v??"",@"\s+"," ").Trim();
    public static void MarkDuplicates(IEnumerable<Contact> contacts)
    {
        var all=contacts.ToList();
        var keys=all.Where(c=>c.IsValid).GroupBy(c=>c.DuplicateKey).Where(g=>g.Count()>1).Select(g=>g.Key).ToHashSet();
        foreach(var c in all)c.IsDuplicate=keys.Contains(c.DuplicateKey);
    }
}
