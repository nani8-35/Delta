using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Delta.Native;
public sealed class AccountClient : IDisposable
{
    private readonly HttpClient http;
    private string? token;
    public string Username { get; private set; } = "";
    public AccountClient(HttpMessageHandler? handler=null) {
        http=new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect=false, UseCookies=false }) {
            BaseAddress=new Uri("https://delta.acranix.com"), Timeout=TimeSpan.FromSeconds(20) };
    }
    public async Task Login(string username,string password,string deviceSecret,CancellationToken ct) {
        token=null; Username="";
        using var request=new HttpRequestMessage(HttpMethod.Post,"/api/login") {
            Content=JsonContent.Create(new {username,password,client="desktop",deviceSecret,version=typeof(App).Assembly.GetName().Version?.ToString(3)}) };
        using var response=await http.SendAsync(request,ct);
        using var data=await Read(response,ct);
        string candidate=data.RootElement.GetProperty("token").GetString() ?? "";
        ValidateUser(data.RootElement);
        if(candidate.Length<20 || candidate.Length>1024 || !System.Text.RegularExpressions.Regex.IsMatch(candidate,"^[A-Za-z0-9_-]+$"))
            throw new InvalidOperationException("The account service returned an invalid session.");
        token=candidate;
    }
    public async Task Validate(CancellationToken ct) {
        if(token==null)throw new InvalidOperationException("Sign in to use Delta.");
        using var request=new HttpRequestMessage(HttpMethod.Get,"/api/session");
        request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);
        using var response=await http.SendAsync(request,ct);
        using var data=await Read(response,ct);
        ValidateUser(data.RootElement);
    }
    private void ValidateUser(JsonElement root) {
        if(!root.TryGetProperty("user",out var user) || !user.TryGetProperty("active",out var active) || active.ValueKind!=JsonValueKind.True ||
            (user.TryGetProperty("disabled",out var disabled) && disabled.ValueKind==JsonValueKind.True))
            throw new InvalidOperationException("Account access is expired or paused. Contact your administrator.");
        Username=user.GetProperty("username").GetString() ?? "";
    }
    private static async Task<JsonDocument> Read(HttpResponseMessage response,CancellationToken ct) {
        string body=await response.Content.ReadAsStringAsync(ct);
        if(body.Length>65536)throw new InvalidOperationException("The account service returned an unexpected response.");
        JsonDocument data;
        try {data=JsonDocument.Parse(body);}catch {throw new InvalidOperationException("The account service is unavailable. Try again shortly.");}
        if(!response.IsSuccessStatusCode) {
            string message=data.RootElement.TryGetProperty("error",out var error) ? error.GetString() ?? "" : "";
            data.Dispose();
            throw new InvalidOperationException(message.Length is >0 and <500 ? message : "Sign-in failed. Check your credentials and account access.");
        }
        return data;
    }
    public async Task Logout() {
        string? old=token;token=null;Username="";
        if(old==null)return;
        try {
            using var request=new HttpRequestMessage(HttpMethod.Post,"/api/logout"){Content=JsonContent.Create(new {})};
            request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",old);
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(4));
            using var response=await http.SendAsync(request,timeout.Token);
        }catch { }
    }
    public void Dispose(){token=null;http.Dispose();}
}
internal static class InstallationIdentity
{
    [StructLayout(LayoutKind.Sequential)] private struct Blob { public int Length; public IntPtr Data; }
    [DllImport("crypt32.dll",SetLastError=true)] private static extern bool CryptProtectData(ref Blob input,IntPtr description,IntPtr entropy,IntPtr reserved,IntPtr prompt,uint flags,out Blob output);
    [DllImport("crypt32.dll",SetLastError=true)] private static extern bool CryptUnprotectData(ref Blob input,IntPtr description,IntPtr entropy,IntPtr reserved,IntPtr prompt,uint flags,out Blob output);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
    private static byte[] Convert(byte[] value,bool protect) {
        var input=new Blob {Length=value.Length,Data=Marshal.AllocHGlobal(value.Length)};Blob output=default;
        try {
            Marshal.Copy(value,0,input.Data,value.Length);
            bool ok=protect?CryptProtectData(ref input,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,1,out output):
                CryptUnprotectData(ref input,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,1,out output);
            if(!ok)throw new InvalidOperationException("Windows could not access this installation identity.");
            var bytes=new byte[output.Length];Marshal.Copy(output.Data,bytes,0,bytes.Length);return bytes;
        }finally {Marshal.FreeHGlobal(input.Data);if(output.Data!=IntPtr.Zero)LocalFree(output.Data);}
    }
    internal static string Load(string folder) {
        Directory.CreateDirectory(folder);string path=Path.Combine(folder,"installation.dat");
        if(File.Exists(path))return System.Convert.ToHexString(Convert(File.ReadAllBytes(path),false)).ToLowerInvariant();
        byte[] identity=RandomNumberGenerator.GetBytes(32),encrypted=Convert(identity,true);
        using(var stream=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None))stream.Write(encrypted);
        return System.Convert.ToHexString(identity).ToLowerInvariant();
    }
}
