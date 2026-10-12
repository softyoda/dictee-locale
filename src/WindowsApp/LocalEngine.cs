using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Threading;
namespace DicteeLocale;
public sealed class LocalEngine : IDisposable
{
    public Process? Process {get;private set;}
    readonly HttpClient _http = new() {Timeout=TimeSpan.FromSeconds(120)};
    public string? LastError {get;private set;}
    public void Start()
    {
        var listener=new TcpListener(IPAddress.Loopback,0); listener.Start();
        int port=((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        string python=Path.Combine(Paths.Root,".venv","Scripts","python.exe");
        if(!File.Exists(python)) throw new InvalidOperationException("Python local introuvable. Lance setup.ps1.");
        var start=new ProcessStartInfo(python) {UseShellExecute=false,CreateNoWindow=true,
            RedirectStandardError=true,RedirectStandardOutput=true,WorkingDirectory=Paths.Root};
        start.ArgumentList.Add(Path.Combine(Paths.Root,"backend","server.py"));
        start.ArgumentList.Add("--port"); start.ArgumentList.Add(port.ToString());
        string token=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        start.Environment["DICTEE_TOKEN"]=token;
        start.Environment["PYTHONUTF8"]="1";
        Process=System.Diagnostics.Process.Start(start) ?? throw new InvalidOperationException("Le moteur n'a pas démarré.");
        // Drain pipes. Store only latest diagnostic line in memory.
        Process.ErrorDataReceived+=(_,e)=>{if(!string.IsNullOrWhiteSpace(e.Data))LastError=e.Data;};
        Process.OutputDataReceived+=(_,e)=>{if(!string.IsNullOrWhiteSpace(e.Data))LastError=e.Data;};
        Process.BeginErrorReadLine(); Process.BeginOutputReadLine();
        _http.BaseAddress=new Uri("http://127.0.0.1:"+port);
        _http.DefaultRequestHeaders.Add("X-Dictee-Token",token);
    }
    public async Task<JsonElement> Health() =>
        await _http.GetFromJsonAsync<JsonElement>("/health");
    async Task<JsonElement> Post(string path,object value,CancellationToken cancellation=default)
    {
        using var payload=new StringContent(JsonSerializer.Serialize(value),Encoding.UTF8,"application/json");
        using var response=await _http.PostAsync(path,payload,cancellation);
        var result=await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken:cancellation);
        if(!response.IsSuccessStatusCode) throw new InvalidOperationException(
            result.TryGetProperty("error",out var error)?error.GetString():"Erreur du moteur local.");
        return result;
    }
    public async Task<string> Transcribe(byte[] audio,string context,CancellationToken cancellation=default)
    {
        var result=await Post("/transcribe",new {audio=Convert.ToBase64String(audio),context},cancellation);
        return result.GetProperty("text").GetString()??"";
    }
    public async Task<(string text,bool applied)> Cleanup(string text,CancellationToken cancellation=default)
    {
        var result=await Post("/cleanup",new {text},cancellation);
        return (result.GetProperty("text").GetString()??text,result.GetProperty("applied").GetBoolean());
    }
    public async Task<bool> RejectsAnonymous()
    {
        using var client=new HttpClient();
        using var response=await client.GetAsync(new Uri(_http.BaseAddress!,"/health"));
        return response.StatusCode==HttpStatusCode.Forbidden;
    }
    public void Dispose()
    {
        _http.Dispose();
        try { if(Process is {HasExited:false})Process.Kill(entireProcessTree:true); } catch {}
        Process?.Dispose();
    }
}
