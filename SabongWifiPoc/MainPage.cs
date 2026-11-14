using System.Net;
using System.Net.Sockets;
using System.Text;
namespace SabongWifiPoc;
public sealed class MainPage : ContentPage
{
    const int Port=50505;
    readonly Label status=new(){Text="Not connected",HorizontalTextAlignment=TextAlignment.Center};
    readonly Label counter=new(){Text="READY",FontSize=64,FontAttributes=FontAttributes.Bold,HorizontalTextAlignment=TextAlignment.Center,TextColor=Colors.OrangeRed};
    readonly Label address=new(){HorizontalTextAlignment=TextAlignment.Center};
    readonly Entry ip=new(){Placeholder="Host IP e.g. 192.168.1.10"};
    readonly Button host=new(){Text="Create Match (Phone 1)"};
    readonly Button join=new(){Text="Join Match (Phone 2)"};
    readonly Button restart=new(){Text="Restart Match",IsEnabled=false};
    readonly Button leave=new(){Text="Leave Match",IsEnabled=false};
    TcpListener? listener;
    TcpClient? client;
    StreamReader? reader;
    StreamWriter? writer;
    CancellationTokenSource? session;
    CancellationTokenSource? countdown;
    bool isHost;
    int generation;
    public MainPage()
    {
        Title="Sabong Wi-Fi POC";
        host.Clicked+=async (_,_)=>await Host();
        join.Clicked+=async (_,_)=>await Join();
        restart.Clicked+=async (_,_)=>await StartCountdown();
        leave.Clicked+=(_,_)=>Stop("Disconnected");
        Content=new ScrollView{Content=new VerticalStackLayout{Padding=new Thickness(24,48),Spacing=18,Children={
            new Label{Text="SABONG",FontSize=36,FontAttributes=FontAttributes.Bold,HorizontalTextAlignment=TextAlignment.Center},
            new Label{Text="2-Player Local Wi-Fi Match",HorizontalTextAlignment=TextAlignment.Center},
            status,counter,address,host,ip,join,restart,leave,
            new Label{Text="Connect both phones to the same Wi-Fi. Phone 2 enters Phone 1's IP address.",FontSize=12,HorizontalTextAlignment=TextAlignment.Center}
        }}};
    }
    void UI(Action a)=>MainThread.BeginInvokeOnMainThread(a);
    void Stop(string message)
    {
        generation++;
        countdown?.Cancel(); countdown?.Dispose(); countdown=null;
        session?.Cancel(); session?.Dispose(); session=null;
        try{reader?.Dispose();writer?.Dispose();client?.Close();listener?.Stop();}catch{}
        reader=null;writer=null;client=null;listener=null;
        status.Text=message;counter.Text="READY";address.Text="";
        host.IsEnabled=true;join.IsEnabled=true;restart.IsEnabled=false;leave.IsEnabled=false;
    }
    void Connected()
    {
        status.Text="Both players connected";
        host.IsEnabled=false;join.IsEnabled=false;restart.IsEnabled=isHost;leave.IsEnabled=true;
    }
    async Task Host()
    {
        Stop("Starting host...");
        isHost=true;session=new();int gen=generation;
        try
        {
            listener=new TcpListener(IPAddress.Any,Port);listener.Start();
            string local=GetIp();
            address.Text="Host IP: "+local+"  Port: "+Port;
            status.Text="Waiting for Player 2...";
            client=await listener.AcceptTcpClientAsync(session.Token);
            if(gen!=generation)return;
            SetupStreams();Connected();
            _=Receive(gen,session.Token);
            await Task.Delay(1200,session.Token);
            if(gen==generation)await StartCountdown();
        }
        catch(OperationCanceledException){}
        catch(Exception ex){if(gen==generation)UI(()=>Stop("Host error: "+ex.Message));}
    }
    async Task Join()
    {
        string target=ip.Text?.Trim()??"";
        if(!IPAddress.TryParse(target,out var parsed)){status.Text="Enter a valid host IPv4 address";return;}
        Stop("Connecting...");
        isHost=false;session=new();int gen=generation;
        try
        {
            client=new TcpClient();
            await client.ConnectAsync(parsed,Port,session.Token);
            if(gen!=generation)return;
            SetupStreams();UI(Connected);
            await Receive(gen,session.Token);
        }
        catch(OperationCanceledException){}
        catch(Exception ex){if(gen==generation)UI(()=>Stop("Connection error: "+ex.Message));}
    }
    void SetupStreams()
    {
        var stream=client!.GetStream();
        reader=new StreamReader(stream,Encoding.UTF8,leaveOpen:true);
        writer=new StreamWriter(stream,new UTF8Encoding(false),leaveOpen:true){AutoFlush=true};
    }
    async Task Receive(int gen,CancellationToken ct)
    {
        try
        {
            while(!ct.IsCancellationRequested)
            {
                string? line=await reader!.ReadLineAsync(ct);
                if(line==null)break;
                if(line.StartsWith("START:") && long.TryParse(line.AsSpan(6),out long start))
                    UI(()=>RunCountdown(start,gen));
            }
        }
        catch(OperationCanceledException){}
        catch(Exception){}
        if(gen==generation)UI(()=>Stop("Other player disconnected"));
    }
    async Task StartCountdown()
    {
        if(!isHost||writer==null)return;
        long start=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()+1800;
        try
        {
            await writer.WriteLineAsync("START:"+start);
            RunCountdown(start,generation);
        }
        catch(Exception ex){Stop("Send error: "+ex.Message);}
    }
    void RunCountdown(long start,int gen)
    {
        countdown?.Cancel();countdown?.Dispose();countdown=new();
        var ct=countdown.Token;
        _=Task.Run(async ()=>{
            try
            {
                while(!ct.IsCancellationRequested && gen==generation)
                {
                    long remaining=start+10000-DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    string value=remaining<=0?"FIGHT!":Math.Clamp((int)Math.Ceiling(remaining/1000.0),1,10).ToString();
                    UI(()=>{if(gen==generation)counter.Text=value;});
                    if(remaining<=0)break;
                    await Task.Delay(60,ct);
                }
            }
            catch(OperationCanceledException){}
        },ct);
    }
    static string GetIp()
    {
        foreach(var item in Dns.GetHostAddresses(Dns.GetHostName()))
            if(item.AddressFamily==AddressFamily.InterNetwork && !IPAddress.IsLoopback(item))
                return item.ToString();
        return "Check Wi-Fi settings";
    }
}
