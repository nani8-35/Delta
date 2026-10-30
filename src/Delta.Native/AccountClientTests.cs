using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
namespace Delta.Native;
internal static class AccountClientTests
{
    private sealed class Handler : HttpMessageHandler {
        internal string Reply="{\"token\":\"abcdefghijklmnopqrstuvwx12345678\",\"user\":{\"username\":\"fixture\",\"active\":true,\"disabled\":false}}";
        internal HttpStatusCode Status=HttpStatusCode.OK;
        internal string Body="",Path="",Authorization="";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct) {
            if(request.RequestUri?.Host!="delta.acranix.com" || request.RequestUri.Scheme!="https")throw new Exception("Unexpected credential destination");
            Body=request.Content==null?"":await request.Content.ReadAsStringAsync(ct);
            Path=request.RequestUri.AbsolutePath;Authorization=request.Headers.Authorization?.ToString()??"";
            return new HttpResponseMessage(Status){Content=new StringContent(Reply,Encoding.UTF8,"application/json")};
        }
    }
    internal static async Task<int> Run(string folder) {
        int count=0;
        void Check(bool condition){if(!condition)throw new Exception("Account protocol regression failed");count++;}
        var handler=new Handler();using var account=new AccountClient(handler);
        await account.Login("fixture","test-only-password",new string('a',64),CancellationToken.None);
        using(var body=JsonDocument.Parse(handler.Body))Check(body.RootElement.GetProperty("client").GetString()=="desktop" && body.RootElement.GetProperty("deviceSecret").GetString()==new string('a',64) && handler.Path=="/api/login");
        await account.Validate(CancellationToken.None);Check(handler.Path=="/api/session"&&handler.Authorization.StartsWith("Bearer "));
        await account.Logout();Check(handler.Path=="/api/logout"&&handler.Authorization.StartsWith("Bearer "));
        try{await account.Validate(CancellationToken.None);throw new Exception("Session was retained");}catch(InvalidOperationException){count++;}
        foreach(var scenario in new[]{
            (HttpStatusCode.Unauthorized,"{\"error\":\"Invalid username or password.\"}"),
            (HttpStatusCode.Forbidden,"{\"code\":\"DEVICE_ACTIVATION_PENDING\",\"error\":\"Activation requested for this computer.\"}"),
            (HttpStatusCode.OK,"{\"token\":\"abcdefghijklmnopqrstuvwx12345678\",\"user\":{\"username\":\"fixture\",\"active\":false}}"),
            (HttpStatusCode.Found,"<html>Redirect</html>"),
            (HttpStatusCode.OK,"{\"token\":\"bad\",\"user\":{\"username\":\"fixture\",\"active\":true}}")
        }) {
            handler.Status=scenario.Item1;handler.Reply=scenario.Item2;
            try{await account.Login("fixture","test-only-password",new string('a',64),CancellationToken.None);throw new Exception("Invalid access accepted");}
            catch(InvalidOperationException){count++;}
            try{await account.Validate(CancellationToken.None);throw new Exception("Failed login retained session");}
            catch(InvalidOperationException){count++;}
        }
        string temp=System.IO.Path.Combine(folder,"installation-test");Directory.CreateDirectory(temp);
        try {
            string first=InstallationIdentity.Load(temp),second=InstallationIdentity.Load(temp);
            Check(first==second&&first.Length==64);
            Check(!File.ReadAllText(System.IO.Path.Combine(temp,"installation.dat")).Contains(first));
        }finally{Directory.Delete(temp,true);}
        return count;
    }
}
