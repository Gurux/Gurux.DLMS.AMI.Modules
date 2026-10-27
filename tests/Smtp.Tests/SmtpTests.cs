using System.Net.Mail;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using Gurux.AMI.SMTP;
using Gurux.AMI.SMTP.Server;
using Gurux.DLMS.AMI.Module;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

[UnsupportedOSPlatform("browser")]
public class SmtpTests
{
    static SmtpOptions Options() => new() { Host = "smtp.example.org", Email = "sender@example.org", Password = "encrypted" };

    sealed class Settings : IAmiModuleSettingsSnapshot
    {
        public string? Value { get; set; } = "stored";
        public SmtpOptions Options { get; } = SmtpTests.Options();
        public T Read<T>() where T : new() => (T)(object)Options;
    }
    sealed class Crypto : IAmiCryptoService
    {
        public int Calls;
        public Task<byte[]> DecryptAsync(string value) { Assert.Equal("encrypted", value); Calls++; return Task.FromResult(Encoding.UTF8.GetBytes("secret")); }
        public Task<byte[]> DecryptAsync(byte[] value) => throw new NotImplementedException();
        public Task<byte[]> EncryptAsync(string value) => throw new NotImplementedException();
        public Task<byte[]> EncryptAsync(byte[] value) => throw new NotImplementedException();
    }
    sealed class Transport : ISmtpTransportFactory, ISmtpTransport
    {
        public SmtpOptions? Used;
        public bool Disposed;
        public Exception? Failure;
        public Action<CancellationToken>? OnSend;
        public Func<CancellationToken, Task>? Wait;
        public string? Body;
        public MemoryStream AttachmentStream = new();
        public ISmtpTransport Create(SmtpOptions options) { Used = options; return this; }
        public Task SendAsync(MailMessage message, CancellationToken token)
        {
            Body = message.Body;
            message.Attachments.Add(new Attachment(AttachmentStream, "test.txt"));
            Assert.True(message.IsBodyHtml);
            OnSend?.Invoke(token);
            token.ThrowIfCancellationRequested();
            if (Wait != null) return Wait(token);
            return Failure == null ? Task.CompletedTask : Task.FromException(Failure);
        }
        public void Dispose() => Disposed = true;
    }
    sealed class TestLogger : ILogger<GXSmtpModule>
    {
        public Exception? Exception;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Exception = exception;
    }
    sealed class Fixture
    {
        public Settings Settings = new();
        public Crypto Crypto = new();
        public Transport Transport = new();
        public TestLogger Log = new();
        public CancellationToken Lifetime;
        public IConfiguration Configuration = new ConfigurationBuilder().Build();
        public AmiModuleBuilder Builder()
        {
            var builder = new AmiModuleBuilder("Gurux.AMI.SMTP", Configuration, Settings);
            new GXSmtpModule().Configure(builder);
            return builder;
        }
        public AmiModuleContext Context()
        {
            var host = new ServiceCollection().AddSingleton<IAmiCryptoService>(Crypto).AddSingleton<ILogger<GXSmtpModule>>(Log).AddSingleton(Configuration).BuildServiceProvider();
            var services = new ServiceCollection();
            var context = new AmiModuleContext { ModuleId = "Gurux.AMI.SMTP", Services = services.BuildServiceProvider(), HostServices = host, Settings = Settings, CancellationToken = Lifetime, JsonOptions = new(JsonSerializerDefaults.Web) };
            services.AddSingleton(new GXSmtpSender(context, Transport));
            return new AmiModuleContext { ModuleId = context.ModuleId, Services = services.BuildServiceProvider(), HostServices = host, Settings = Settings, CancellationToken = Lifetime, JsonOptions = context.JsonOptions };
        }
        public async Task<(int Status, string Body, string? Type)> Request(string json, CancellationToken token = default)
        {
            var http = new DefaultHttpContext();
            http.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(json));
            http.Response.Body = new MemoryStream();
            await Builder().Endpoints.Single().Handler(new(http, Context()), token);
            http.Response.Body.Position = 0;
            return (http.Response.StatusCode, await new StreamReader(http.Response.Body).ReadToEndAsync(), http.Response.ContentType);
        }
    }

    [Fact] public void EndpointRequiresEditPolicy()
    {
        var builder = new Fixture().Builder();
        var endpoint = Assert.Single(builder.Endpoints);
        Assert.Equal("test", endpoint.Pattern);
        Assert.Equal(new[] { "POST" }, endpoint.Methods);
        Assert.Equal(new[] { "module.edit" }, endpoint.Policies);
        Assert.Single(builder.Views);
    }
    [Theory]
    [InlineData("bad-json")]
    [InlineData("null")]
    [InlineData("{\"address\":\"private-invalid-address\"}")]
    [InlineData("{\"address\":\"to@example.org\",\"options\":{\"host\":\"private-host\",\"email\":\"private-invalid-address\"}}")]
    public async Task InvalidRequestsUseFixedSafeDetails(string json)
    {
        var f = new Fixture();
        var result = await f.Request(json);
        Assert.Equal(400, result.Status);
        Assert.Equal("application/problem+json", result.Type);
        Assert.Equal("The SMTP test request is invalid.", JsonDocument.Parse(result.Body).RootElement.GetProperty("detail").GetString());
        Assert.Null(f.Transport.Used);
    }
    [Theory] [InlineData(true)] [InlineData(false)]
    public async Task FailuresAreLoggedAndRedacted(bool smtp)
    {
        var f = new Fixture();
        var failure = smtp ? (Exception)new SmtpException("private server", new Exception("private password")) : new InvalidOperationException("private configuration");
        f.Transport.Failure = failure;
        var result = await f.Request("{\"address\":\"to@example.org\"}");
        Assert.Equal(smtp ? 502 : 500, result.Status);
        Assert.DoesNotContain("private", result.Body);
        Assert.Same(failure, f.Log.Exception);
        Assert.True(f.Transport.Disposed);
        Assert.False(f.Transport.AttachmentStream.CanRead);
    }
    [Fact] public async Task StoredPasswordIsDecryptedWithoutChangingSettings()
    {
        var f = new Fixture();
        await new GXSmtpSender(f.Context(), f.Transport).SendEmailAsync("to@example.org", "subject", "<b>body</b>");
        Assert.Equal("secret", f.Transport.Used!.Password);
        Assert.Equal("encrypted", f.Settings.Options.Password);
        Assert.Equal(1, f.Crypto.Calls);
        Assert.True(f.Transport.Disposed);
    }
    [Theory] [InlineData("")] [InlineData("temporary")]
    public async Task TemporarySettingsDoNotMutateInputs(string password)
    {
        var f = new Fixture();
        var temporary = Options(); temporary.Host = "temporary.example.org"; temporary.Password = password; temporary.TimeoutSeconds = 17;
        await new GXSmtpSender(f.Context(), f.Transport).SendTestEmailAsync("to@example.org", "subject", "body", temporary);
        Assert.Equal("temporary.example.org", f.Transport.Used!.Host);
        Assert.Equal(17, f.Transport.Used.TimeoutSeconds);
        Assert.Equal(password == "" ? "secret" : password, f.Transport.Used.Password);
        Assert.Equal(password, temporary.Password);
        Assert.Equal("encrypted", f.Settings.Options.Password);
        Assert.Equal(password == "" ? 1 : 0, f.Crypto.Calls);
    }
    [Fact] public async Task MissingSettingsUseHostConfiguration()
    {
        var f = new Fixture(); f.Settings.Value = null;
        f.Configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Smtp:Host"] = "fallback.example.org", ["Smtp:Email"] = "fallback@example.org", ["Smtp:Password"] = "plain" }).Build();
        await new GXSmtpSender(f.Context(), f.Transport).SendEmailAsync("to@example.org", "s", "body");
        Assert.Equal("fallback.example.org", f.Transport.Used!.Host);
        Assert.Equal("plain", f.Transport.Used.Password);
        Assert.Equal(0, f.Crypto.Calls);
    }
    [Theory] [InlineData(true)] [InlineData(false)]
    public async Task CancellationPropagatesAndDisposesTransport(bool lifetime)
    {
        using var source = new CancellationTokenSource();
        var f = new Fixture(); if (lifetime) f.Lifetime = source.Token;
        f.Transport.OnSend = _ => source.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new GXSmtpSender(f.Context(), f.Transport).SendEmailAsync("to@example.org", "s", "body", lifetime ? default : source.Token));
        Assert.True(f.Transport.Disposed);
    }
    [Fact] public async Task EndpointCancellationPropagates()
    {
        var f = new Fixture(); f.Transport.Failure = new OperationCanceledException();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Request("{\"address\":\"to@example.org\"}"));
        Assert.Null(f.Log.Exception);
    }
    [Theory] [InlineData(true)] [InlineData(false)]
    public async Task AlreadyCancelledOperationDoesNotCreateTransport(bool lifetime)
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        var f = new Fixture();
        if (lifetime) f.Lifetime = source.Token;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new GXSmtpSender(f.Context(), f.Transport).SendEmailAsync("to@example.org", "s", "body", lifetime ? default : source.Token));
        Assert.Null(f.Transport.Used);
        Assert.Equal(0, f.Crypto.Calls);
    }
    [Fact] public async Task HappyHttpRequestSendsHtml()
    {
        var f = new Fixture(); var result = await f.Request("{\"address\":\"to@example.org\"}");
        Assert.Equal(200, result.Status); Assert.Equal("ok", result.Body);
        Assert.False(string.IsNullOrWhiteSpace(f.Transport.Body));
    }
    [Fact] public async Task TimeoutCancelsDeliveryAndDisposesResources()
    {
        var f = new Fixture();
        f.Settings.Options.TimeoutSeconds = 1;
        f.Transport.Wait = token => Task.Delay(Timeout.Infinite, token);
        await Assert.ThrowsAsync<TimeoutException>(() => new GXSmtpSender(f.Context(), f.Transport)
            .SendEmailAsync("to@example.org", "s", "body").WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.True(f.Transport.Disposed);
        Assert.False(f.Transport.AttachmentStream.CanRead);
    }
    [Fact] public async Task EndpointTimeoutReturnsSafeGatewayTimeout()
    {
        var f = new Fixture();
        f.Settings.Options.TimeoutSeconds = 1;
        f.Transport.Wait = token => Task.Delay(Timeout.Infinite, token);
        var result = await f.Request("{\"address\":\"to@example.org\"}").WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(504, result.Status);
        Assert.Equal("The SMTP test email timed out.", JsonDocument.Parse(result.Body).RootElement.GetProperty("detail").GetString());
        Assert.IsType<TimeoutException>(f.Log.Exception);
    }
    [Theory] [InlineData(0)] [InlineData(-1)] [InlineData(301)]
    public void InvalidTimeoutIsRejected(int seconds)
    {
        var options = Options(); options.TimeoutSeconds = seconds;
        Assert.Throws<ArgumentOutOfRangeException>(() => GXSmtpSender.ValidateOptions(options));
    }
    [Theory] [InlineData("", 587, "a@example.org")] [InlineData("host", 0, "a@example.org")] [InlineData("host", 65536, "a@example.org")] [InlineData("host", 587, "")] [InlineData("host", 587, "bad")]
    public void InvalidOptionsAreRejected(string host, int port, string email) => Assert.ThrowsAny<Exception>(() => GXSmtpSender.ValidateOptions(new() { Host = host, Port = port, Email = email }));
}

