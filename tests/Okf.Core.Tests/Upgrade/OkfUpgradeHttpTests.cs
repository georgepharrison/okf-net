using System.Net;
using System.Net.Sockets;
using System.Text;
using Okf.Core;

namespace Okf.Core.Tests.Upgrade;

/// <summary>
/// The production <see cref="OkfUpgrade.HttpFetch" />, against a server this test starts
/// itself (work item #23).
/// </summary>
/// <remarks>
/// The rest of the suite injects a fake fetch, which proves everything about the decisions
/// and nothing about the HTTP client that makes them real. This file closes that gap
/// without reaching the artifact host: an in-process <see cref="HttpListener" /> on
/// loopback serves a fixture release, which is exactly the case
/// <see cref="OkfUpgrade.BaseUri" /> permits cleartext for.
/// </remarks>
public sealed class OkfUpgradeHttpTests : IDisposable
{
    private const string AssetBytes = "#!/bin/sh\necho okf 1.1.0-rc.1\n";

    private readonly HttpListener listener = new();
    private readonly string baseUrl;
    private readonly List<string> userAgents = [];
    private readonly Task server;

    public OkfUpgradeHttpTests()
    {
        var port = FreePort();
        this.baseUrl = $"http://127.0.0.1:{port}";
        this.listener.Prefixes.Add($"{this.baseUrl}/");
        this.listener.Start();
        this.server = Task.Run(Serve);
    }

    [Fact]
    public void ResolvesAndInstallsOverRealHttp()
    {
        using var tree = new TempTree();
        var target = Path.Combine(tree.Root, "okf");
        File.WriteAllText(target, "the binary that is running");

        var options = new OkfUpgradeOptions
        {
            BaseUrl = this.baseUrl,
            CurrentVersion = "1.0.0",
            ExecutablePath = target,
            UserAgent = "okf/1.0.0",
        };

        var plan = OkfUpgrade.Resolve(options);

        Assert.Equal("1.1.0-rc.1", plan.AvailableVersion);
        Assert.False(plan.IsUpToDate);
        Assert.Equal($"{this.baseUrl}/latest.json", plan.ManifestUri.ToString());

        var result = OkfUpgrade.Apply(plan, userAgent: options.UserAgent);

        Assert.Equal("1.1.0-rc.1", result.Version);
        Assert.Equal(AssetBytes, File.ReadAllText(target));

        // The host is told which okf is asking, on every request it answered.
        Assert.NotEmpty(this.userAgents);
        Assert.All(this.userAgents, agent => Assert.Equal("okf/1.0.0", agent));
    }

    /// <summary>
    /// Redirects are followed rather than reported: the artifact host is allowed to move.
    /// The scheme they may move to is <see cref="OkfUpgrade.ResolveRedirect" />'s decision
    /// and is asserted there; this proves the loop wires it up and does not simply stop at
    /// the 302.
    /// </summary>
    [Fact]
    public void FollowsARedirectToTheManifest()
    {
        using var tree = new TempTree();
        var target = Path.Combine(tree.Root, "okf");
        File.WriteAllText(target, "old");

        var plan = OkfUpgrade.Resolve(new OkfUpgradeOptions
        {
            BaseUrl = $"{this.baseUrl}/moved",
            CurrentVersion = "1.0.0",
            ExecutablePath = target,
        });

        Assert.Equal("1.1.0-rc.1", plan.AvailableVersion);
    }

    [Fact]
    public void ReportsAReleaseTheHostDoesNotHave()
    {
        using var tree = new TempTree();
        var target = Path.Combine(tree.Root, "okf");
        File.WriteAllText(target, "old");

        var refusal = Assert.Throws<OkfUpgradeException>(() => OkfUpgrade.Resolve(new OkfUpgradeOptions
        {
            BaseUrl = this.baseUrl,
            Version = "9.9.9",
            CurrentVersion = "1.0.0",
            ExecutablePath = target,
        }));

        Assert.Contains("v9.9.9/latest.json", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("HTTP 404", refusal.Message, StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        this.listener.Close();
        this.server.Wait(TimeSpan.FromSeconds(5));
    }

    private void Serve()
    {
        while (this.listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = this.listener.GetContext();
            }
            catch (Exception exception) when (exception is HttpListenerException or ObjectDisposedException)
            {
                return;
            }

            // `Close()` and nothing else. Every branch below closes this response, and
            // disposing it a second time (a `using` here) corrupts the listener's state for
            // whichever request next arrives on the recycled connection: that request is
            // then served through a disposed response, this thread throws, and the client
            // reads an empty body. Measured at 30 failures in 200 redirect round-trips with
            // the second dispose and 0 without it — which is what made
            // `FollowsARedirectToTheManifest` fail about one full suite run in four.
            var response = context.Response;
            this.userAgents.Add(context.Request.UserAgent ?? string.Empty);

            var path = context.Request.Url!.AbsolutePath;
            if (path == "/moved/latest.json")
            {
                response.StatusCode = 302;
                response.Headers["Location"] = "/latest.json";
                response.Close();
                continue;
            }

            var body = path switch
            {
                "/latest.json" => Manifest(),
                "/v1.1.0-rc.1/okf-linux-x64" => AssetBytes,
                "/v1.1.0-rc.1/okf-osx-arm64" => AssetBytes,
                "/v1.1.0-rc.1/okf-win-x64.exe" => AssetBytes,
                _ => null,
            };

            if (body is null)
            {
                response.StatusCode = 404;
                response.Close();
                continue;
            }

            var bytes = Encoding.UTF8.GetBytes(body);
            response.ContentLength64 = bytes.Length;
            response.OutputStream.Write(bytes);
            response.Close();
        }
    }

    private static string Manifest()
    {
        var digest = OkfUpgradeTests.Digest(AssetBytes);
        return $$"""
            {
              "version": "1.1.0-rc.1",
              "tag": "v1.1.0-rc.1",
              "generatedAt": "2026-08-16T04:27:30Z",
              "assets": {
                "okf-linux-x64":   { "path": "v1.1.0-rc.1/okf-linux-x64",   "size": 1, "sha256": "{{digest}}" },
                "okf-osx-arm64":   { "path": "v1.1.0-rc.1/okf-osx-arm64",   "size": 1, "sha256": "{{digest}}" },
                "okf-win-x64.exe": { "path": "v1.1.0-rc.1/okf-win-x64.exe", "size": 1, "sha256": "{{digest}}" }
              }
            }
            """;
    }

    private static int FreePort()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }
}
