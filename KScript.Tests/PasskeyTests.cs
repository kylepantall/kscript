using System.Buffers.Binary;
using System.Formats.Cbor;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

using KScript.Auth;
using KScript.Projects;
using KScript.Runtime;

namespace KScript.Tests;

public sealed class PasskeyTests : IDisposable
{
    private const string Origin = "https://accounts.example.test", Rp = "example.test";
    private readonly TempSite site = new();
    private readonly ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly byte[] id = RandomNumberGenerator.GetBytes(32);
    private readonly AuthApp app;
    private readonly string user, ownerCookie;

    public PasskeyTests()
    {
        app = AuthApp.For(site.Root, new AuthSettings { Folder = "storage", PasskeyOrigin = Origin, PasskeyRpId = Rp, PasskeySignup = true });
        user = app.Users.Add(new Dictionary<string, object> { ["email"] = "pat@example.test" });
        var login = Request();
        login.Login(user, false);
        ownerCookie = Cookie(login);
    }

    public void Dispose()
    {
        key.Dispose();
        site.Dispose();
    }

    private AuthRequest Request(string cookie = null, string origin = Origin, AuthApp server = null) =>
        AuthRequest.For(new Dictionary<string, object>
        {
            ["method"] = "POST",
            ["headers"] = new Dictionary<string, object> { ["origin"] = origin, ["content-type"] = "application/json" },
            ["cookies"] = new Dictionary<string, object> { [app.Settings.Cookie] = cookie },
        }, server ?? app);

    private static string Cookie(AuthRequest request) => Assert.Single(request.Finish()).Split(';')[0].Split('=')[1];

    private static JsonNode Begin(AuthRequest request, string action) => JsonNode.Parse(Passkeys.Handle(request, action, "options", "{}"));
    private static string B64(byte[] data) => AuthApp.Base64Url(data);

    // A software authenticator for these tests. It returns a COSE EC2 public key at registration,
    // then signs the authenticator data and hash of clientDataJSON using the corresponding private key.
    private byte[] PublicKey()
    {
        ECPoint point = key.ExportParameters(false).Q;
        var cbor = new CborWriter();
        cbor.WriteStartMap(5);
        cbor.WriteInt32(1); cbor.WriteInt32(2);       // kty: EC2
        cbor.WriteInt32(3); cbor.WriteInt32(-7);      // alg: ES256
        cbor.WriteInt32(-1); cbor.WriteInt32(1);      // crv: P-256
        cbor.WriteInt32(-2); cbor.WriteByteString(point.X);
        cbor.WriteInt32(-3); cbor.WriteByteString(point.Y);
        cbor.WriteEndMap();
        return cbor.Encode();
    }

    private static byte[] Client(JsonNode options, string type, string origin = Origin) => Encoding.UTF8.GetBytes(
        new JsonObject { ["type"] = type, ["challenge"] = options["challenge"].GetValue<string>(), ["origin"] = origin }.ToJsonString());

    private static byte[] AuthData(byte flags, uint count)
    {
        var data = new byte[37];
        SHA256.HashData(Encoding.UTF8.GetBytes(Rp)).CopyTo(data, 0);
        data[32] = flags;
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(33), count);
        return data;
    }

    private JsonObject Response(JsonObject response) => new()
    {
        ["id"] = B64(id), ["rawId"] = B64(id), ["type"] = "public-key", ["response"] = response, ["clientExtensionResults"] = new JsonObject(),
    };

    private JsonObject Attest(JsonNode options)
    {
        var attestation = new CborWriter();
        attestation.WriteStartMap(3);
        attestation.WriteTextString("fmt"); attestation.WriteTextString("none");
        attestation.WriteTextString("attStmt"); attestation.WriteStartMap(0); attestation.WriteEndMap();
        attestation.WriteTextString("authData");
        attestation.WriteByteString([.. AuthData(0x45, 0), .. new byte[16], 0, (byte)id.Length, .. id, .. PublicKey()]);
        attestation.WriteEndMap();
        return Response(new JsonObject
        {
            ["clientDataJSON"] = B64(Client(options, "webauthn.create")), ["attestationObject"] = B64(attestation.Encode()),
        });
    }

    private string AssertCredential(JsonNode options, string handle, byte flags = 5, uint count = 1, string origin = Origin)
    {
        byte[] authData = AuthData(flags, count);
        byte[] client = Client(options, "webauthn.get", origin);
        byte[] signature = key.SignData([.. authData, .. SHA256.HashData(client)], HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        return Response(new JsonObject
        {
            ["authenticatorData"] = B64(authData), ["clientDataJSON"] = B64(client), ["signature"] = B64(signature), ["userHandle"] = handle,
        }).ToJsonString();
    }

    private string Register()
    {
        var options = Begin(Request(ownerCookie), "register");
        Assert.Equal("required", (string)options["authenticatorSelection"]["residentKey"]);
        Assert.Equal("required", (string)options["authenticatorSelection"]["userVerification"]);
        Assert.Equal(Rp, (string)options["rp"]["id"]);
        Assert.Equal("{\"ok\":true}", Passkeys.Handle(Request(ownerCookie), "register", "verify", Attest(options).ToJsonString()));
        return (string)options["user"]["id"];
    }

    [Fact]
    public void DiscoverablePasskeySignsInAndRotatesSession()
    {
        string handle = Register();
        var options = Begin(Request(ownerCookie), "register");
        Assert.Equal(B64(id), (string)options["excludeCredentials"][0]["id"]);
        Assert.Equal(handle, (string)options["user"]["id"]);
        var guest = Request();
        guest.RememberIntended("/settings?tab=devices");
        var challenge = Begin(guest, "login");
        Assert.Equal("required", (string)challenge["userVerification"]);
        string guestCookie = Cookie(guest);
        string assertion = AssertCredential(challenge, handle);
        var verified = Request(guestCookie);
        var signedInResponse = JsonNode.Parse(Passkeys.Handle(verified, "login", "verify", assertion));
        Assert.True((bool)signedInResponse["ok"]);
        Assert.Equal("/settings?tab=devices", (string)signedInResponse["redirect"]);
        string signedIn = Cookie(verified);
        Assert.NotEqual(guestCookie, signedIn);
        Assert.Equal(user, Request(signedIn).SessionUser["id"]);
        Assert.Null(Request(guestCookie).SessionUser);
        Assert.Throws<KScriptException>(() => Passkeys.Handle(Request(signedIn), "login", "verify", assertion));

        var next = Begin(Request(signedIn), "login");
        Assert.Throws<KScriptException>(() => Passkeys.Handle(Request(signedIn), "login", "verify", AssertCredential(next, handle, count: 1)));
        Assert.True(Passkeys.Revoke(Request(signedIn), B64(id)));
        Assert.Empty(Passkeys.List(Request(signedIn)));
        var after = Begin(Request(signedIn), "login");
        Assert.Throws<KScriptException>(() => Passkeys.Handle(Request(signedIn), "login", "verify", AssertCredential(after, handle, count: 2)));
    }

    [Fact]
    public void SignupCreatesAStablePasswordlessIdentityOnlyAfterVerification()
    {
        var guest = Request();
        string optionsBody = """{"id":"someone-else","email":"pat@example.test"}""";
        var options = JsonNode.Parse(Passkeys.Handle(guest, "signup", "options", optionsBody));
        Assert.Equal("required", (string)options["authenticatorSelection"]["residentKey"]);
        Assert.NotEqual("pat@example.test", (string)options["user"]["name"]);
        Assert.Null(guest.User);
        Assert.Empty(app.PasskeyStore.Users());
        string guestCookie = Cookie(guest);

        var signup = Request(guestCookie);
        string attestation = Attest(options).ToJsonString();
        Assert.True((bool)JsonNode.Parse(Passkeys.Handle(signup, "signup", "verify", attestation))["ok"]);
        string signedIn = Cookie(signup);
        string account = (string)signup.User["id"];
        Assert.NotEqual(guestCookie, signedIn);
        Assert.NotEqual(B64(id), account);
        Assert.NotEqual(user, account);
        Assert.False(signup.User.ContainsKey("email"));
        Assert.False(signup.User.ContainsKey(UserStore.HashField));
        Assert.False(Passkeys.Revoke(signup, B64(id)));
        Assert.Throws<KScriptException>(() => Passkeys.Handle(Request(guestCookie), "signup", "verify", attestation));
        Assert.Throws<KScriptException>(() => Begin(Request(signedIn), "signup"));

        var users = signup.Users;
        Assert.Single(users.All().OfType<Dictionary<string, object>>(), record => (string)record["id"] == account);
        users.Update(null, account, new Dictionary<string, object> { ["license"] = "test-license" });
        Assert.Throws<KScriptException>(() => users.Update(null, account, new Dictionary<string, object> { ["email"] = "pat@example.test" }));
        Assert.Throws<KScriptException>(() => users.SetPassword(null, account, "a new sign-in secret"));
        Assert.Throws<KScriptException>(() => Request().Login(new Dictionary<string, object> { ["id"] = account }, false));

        var logout = Request(signedIn);
        logout.Logout(false);
        logout.Finish();
        var returning = Request();
        var beforeRestart = Begin(returning, "login");
        string anonymous = Cookie(returning);
        // A fresh server instance starts with the same durable auth folder and no in-flight ceremonies.
        var restarted = AuthApp.For(Path.Combine(site.Root, "second-instance"), new AuthSettings
        {
            Folder = Path.Combine(site.Root, "storage"), PasskeyOrigin = Origin, PasskeyRpId = Rp, PasskeySignup = true,
        });
        string handle = (string)options["user"]["id"];
        var stale = Request(anonymous, server: restarted);
        Assert.Throws<KScriptException>(() => Passkeys.Handle(stale, "login", "verify", AssertCredential(beforeRestart, handle)));
        Assert.Null(stale.User);
        var fresh = Begin(Request(anonymous, server: restarted), "login");
        var verified = Request(anonymous, server: restarted);
        Passkeys.Handle(verified, "login", "verify", AssertCredential(fresh, handle));
        Assert.Equal(account, verified.User["id"]);
        Assert.Equal("test-license", verified.User["license"]);
        string cookie = Cookie(verified);
        Assert.True(verified.Users.Delete(account));
        Assert.Null(Request(cookie, server: restarted).User);
        Assert.Null(restarted.PasskeyStore.Find(Rp, B64(id)));
        Assert.Throws<KScriptException>(() => Request(server: restarted).Login(new Dictionary<string, object> { ["id"] = account }, false));
    }

    [Fact]
    public void ExpiredSignupAndDuplicateCredentialLeaveNoNewAccount()
    {
        var guest = Request();
        var options = Begin(guest, "signup");
        string cookie = Cookie(guest);
        var verify = Request(cookie);
        var clock = AuthApp.Clock;
        try
        {
            AuthApp.Clock = () => clock().AddMinutes(6);
            Assert.Throws<KScriptException>(() => Passkeys.Handle(verify, "signup", "verify", Attest(options).ToJsonString()));
            Assert.Null(verify.User);
            Assert.Empty(app.PasskeyStore.Users());
        }
        finally { AuthApp.Clock = clock; }

        Register();
        var duplicate = Begin(Request(cookie), "signup");
        Assert.Throws<KScriptException>(() => Passkeys.Handle(Request(cookie), "signup", "verify", Attest(duplicate).ToJsonString()));
        // The same conflict at the database write (when two verifications raced past the uniqueness
        // callback) must also roll back the new profile and its handle.
        var profile = new Dictionary<string, object> { ["id"] = PasskeyStore.NewUserId() };
        Assert.False(app.PasskeyStore.CreateUser(Rp, profile, B64(id), RandomNumberGenerator.GetBytes(32), [1], 0, false));
        Assert.Empty(app.PasskeyStore.Users());
        Assert.Equal(user, app.PasskeyStore.Find(Rp, B64(id)).User);
    }

    [Fact]
    public void RegistrationNeedsSameOriginAndStoredSessionOwner()
    {
        Assert.Throws<KScriptException>(() => Begin(Request(), "register"));
        Assert.Throws<KScriptException>(() => Begin(Request(ownerCookie, "https://other.example.test"), "register"));
        var options = Begin(Request(ownerCookie), "register");
        string attestation = Attest(options).ToJsonString();
        var other = Request();
        other.Login(app.Users.Add(new Dictionary<string, object> { ["email"] = "other@example.test" }), false);
        string otherCookie = Cookie(other);
        Assert.Throws<KScriptException>(() => Passkeys.Handle(Request(otherCookie), "register", "verify", attestation));
        Passkeys.Handle(Request(ownerCookie), "register", "verify", attestation);
        Assert.Throws<KScriptException>(() => Passkeys.Handle(Request(ownerCookie), "register", "verify", attestation));
        Assert.False(Passkeys.Revoke(Request(otherCookie), B64(id)));
        Assert.Single(Passkeys.List(Request(ownerCookie)));
        new UserStore(app).Delete(user);
        Assert.Null(app.PasskeyStore.Find(Rp, B64(id)));
    }

    [Fact]
    public void RepeatedAnonymousOptionsCannotCreateUnlimitedSessions()
    {
        string cookie = null;
        for (int i = 0; i < app.Settings.Attempts; i++)
        {
            var guest = Request();
            Begin(guest, "login");
            cookie = Cookie(guest);
        }
        int before = app.Sessions.Count();
        var limited = Request();
        Assert.Equal("TooManyRequests", Assert.Throws<KScriptException>(() => Begin(limited, "login")).Type);
        Assert.Empty(limited.Finish());
        Assert.Equal(before, app.Sessions.Count());
        Assert.NotNull(Begin(Request(cookie), "login")["challenge"]);
    }

    [Theory]
    [InlineData("origin")]
    [InlineData("handle")]
    [InlineData("user-verification")]
    [InlineData("signature")]
    [InlineData("expiry")]
    public void FailedAssertionsDoNotAuthenticate(string reason)
    {
        string handle = Register();
        var guest = Request();
        var options = Begin(guest, "login");
        var verify = Request(Cookie(guest));
        string assertion = AssertCredential(options, reason == "handle" ? B64(new byte[32]) : handle,
            flags: reason == "user-verification" ? (byte)1 : (byte)5, origin: reason == "origin" ? "https://other.example.test" : Origin);
        if (reason == "signature")
        {
            var body = JsonNode.Parse(assertion);
            body["response"]["signature"] = B64(key.SignData([0], HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence));
            assertion = body.ToJsonString();
        }
        var clock = AuthApp.Clock;
        try
        {
            if (reason == "expiry") AuthApp.Clock = () => clock().AddMinutes(6);
            Assert.Throws<KScriptException>(() => Passkeys.Handle(verify, "login", "verify", assertion));
            Assert.Null(verify.SessionUser);
            Assert.Throws<KScriptException>(() => Passkeys.Handle(verify, "login", "verify", AssertCredential(options, handle)));
        }
        finally { AuthApp.Clock = clock; }
    }
}
