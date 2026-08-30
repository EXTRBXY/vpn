namespace NothingVpn.Desktop.Wpf;
internal sealed record StartupOptions(bool Start, string? Mode, string? ProfileId)
{
    public static StartupOptions Parse(string[] args)
    {
        bool start=false; string? mode=null, profile=null;
        for(int i=0;i<args.Length;i++)
        {
            var a=args[i].Trim(); if(a.Equals("--start",StringComparison.OrdinalIgnoreCase)){start=true;continue;}
            if(Read(args,ref i,"--mode",out var v)) mode=v;
            else if(Read(args,ref i,"--profile",out v)) profile=v;
        }
        return new(start,mode,profile);
    }
    private static bool Read(string[] args,ref int i,string key,out string? value)
    {
        value=null;var a=args[i];if(a.StartsWith(key+"=",StringComparison.OrdinalIgnoreCase)){value=a[(key.Length+1)..].Trim('"');return true;}
        if(!a.Equals(key,StringComparison.OrdinalIgnoreCase))return false;if(i+1<args.Length)value=args[++i].Trim('"');return true;
    }
}
