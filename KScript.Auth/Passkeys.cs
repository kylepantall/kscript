using System.Formats.Cbor;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

using Fido2NetLib;
using Fido2NetLib.Objects;
using Fido2NetLib.Serialization;

using KScript.Runtime;

namespace KScript.Auth
{
    [JsonSerializable(typeof(AuthenticatorAttestationRawResponse))]
    internal partial class PasskeyJson : JsonSerializerContext { }

    /// <summary>WebAuthn for KScript's registered users. The browser supplies credential responses; the site supplies the RP, account and challenge.</summary>
    public static class Passkeys
    {
        private const int Lifetime = 300;
        public const int MaxBodyBytes = 128 * 1024;

        private static string RelyingParty(AuthRequest auth)
        {
            string origin = auth.App.Settings.PasskeyOrigin ?? throw new KScriptException("Passkeys are disabled for this site.", "NotFound");
            return auth.App.Settings.PasskeyRpId ?? new Uri(origin).IdnHost;
        }

        private static Dictionary<string, object> Account(AuthRequest auth)
        {
            var sessionUser = auth.SessionUser;
            if (sessionUser != null && auth.Users.Record(UserStore.IdOf(sessionUser, "Passkeys")) != null)
                return sessionUser;
            throw new KScriptException("Sign in to a registered account to manage passkeys.", "Unauthorized");
        }

        public static List<object> List(AuthRequest auth)
        {
            string rp = RelyingParty(auth), user = UserStore.IdOf(Account(auth), "Passkeys");
            return auth.App.PasskeyStore.ForUser(rp, user).Select(key => (object)new Dictionary<string, object>
            {
                ["id"] = key.Id, ["created_at"] = key.Created,
            }).ToList();
        }

        public static bool Revoke(AuthRequest auth, string id) =>
            auth.App.PasskeyStore.Revoke(RelyingParty(auth), UserStore.IdOf(Account(auth), "Passkeys"), id ?? string.Empty);

        public static string Handle(AuthRequest auth, string action, string step, string body)
        {
            string rp = RelyingParty(auth);
            string origin = auth.App.Settings.PasskeyOrigin;
            if (action is not ("register" or "login" or "signup") || step is not ("options" or "verify") ||
                (action == "signup" && !auth.App.Settings.PasskeySignup))
                throw new KScriptException("Unknown passkey operation.", "NotFound");
            if (!string.Equals(auth.Header("origin"), origin, StringComparison.Ordinal))
                throw new KScriptException("This site's origin is required.", "Forbidden");
            if (auth.Request.GetValueOrDefault("method") as string != "POST" ||
                !string.Equals(auth.Header("content-type")?.Split(';')[0].Trim(), "application/json", StringComparison.OrdinalIgnoreCase) ||
                body == null || System.Text.Encoding.UTF8.GetByteCount(body) > MaxBodyBytes)
                throw new KScriptException("Send a JSON passkey request.", "Validation");

            if (action == "signup" && auth.SessionUser != null)
                throw new KScriptException("Sign out before creating another account.", "Forbidden");

            var store = auth.App.PasskeyStore;
            var user = action == "register" ? Account(auth) : null;
            string userId = user == null ? null : UserStore.IdOf(user, "Passkeys");
            string limitKey = "passkey|" + AuthApp.Text(auth.Request.GetValueOrDefault("ip"));
            bool begin = step == "options";
            if (action != "register") auth.App.CheckAttempts(limitKey);
            if (action == "signup" && begin) auth.App.ReserveAttempt(limitKey + "|signup");
            string session = auth.PasskeySessionKey(create: false);
            if (session == null)
            {
                if (!begin) throw Rejected();
                auth.App.ReserveAttempt(limitKey + "|new-session");
                session = auth.PasskeySessionKey(create: true);
            }
            string binding = AuthApp.KeyOf(string.Join("\n", session, origin, rp, action, userId));
            var fido = new Fido2(new Fido2Configuration
            {
                RPID = rp, RPName = auth.App.Name, Origins = new HashSet<string> { origin },
                Timeout = Lifetime * 1000, ChallengeSize = 32,
            });
            if (begin)
            {
                string options = action == "login"
                    ? fido.GetAssertionOptions(new GetAssertionOptionsParams { UserVerification = UserVerificationRequirement.Required }).ToJson()
                    : RegisterOptions(fido, store, rp, userId, user);
                store.Challenge(binding, options, Lifetime);
                return options;
            }

            string pending = store.Consume(binding) ?? throw Rejected();
            try
            {
                if (action == "login") SignIn(fido, auth, rp, pending, body);
                else
                {
                    var result = VerifyRegistration(fido, store, rp, origin, pending, body);
                    string credentialId = AuthApp.Base64Url(result.Id);
                    if (action == "register")
                    {
                        if (!store.Add(rp, credentialId, userId, result.User.Id, result.PublicKey, result.SignCount, result.IsBackupEligible))
                            throw new Fido2VerificationException("Credential could not be added.");
                        return "{\"ok\":true}";
                    }

                    string id = PasskeyStore.NewUserId();
                    var profile = new Dictionary<string, object>
                    {
                        ["id"] = id, ["name"] = result.User.DisplayName, ["created_at"] = AuthApp.Stamp(),
                    };
                    profile["updated_at"] = profile["created_at"];
                    if (auth.Users.Record(id) != null || !store.CreateUser(rp, profile, credentialId, result.User.Id, result.PublicKey, result.SignCount, result.IsBackupEligible))
                        throw new Fido2VerificationException("Account could not be created.");
                    auth.LoginPasskey(id);
                }
                auth.App.Succeeded(limitKey);
                return Values.ToJson(new Dictionary<string, object> { ["ok"] = true, ["redirect"] = auth.Intended(null) });
            }
            catch (Exception ex) when (ex is Fido2VerificationException or JsonException or FormatException or
                CborContentException or CryptographicException or ArgumentException or InvalidOperationException or OverflowException or IndexOutOfRangeException)
            {
                if (action != "register") auth.App.Failed(limitKey);
                throw Rejected();
            }
        }

        private static KScriptException Rejected() => new KScriptException("Passkey verification failed. Start again and try your passkey.", "Unauthorized");

        private static string RegisterOptions(Fido2 fido, PasskeyStore store, string rp, string userId, Dictionary<string, object> user)
        {
            // Signup has no account yet. Its temporary handle and label live only in the server's
            // challenge until verification. No client-supplied identifier or email selects an account.
            byte[] handle = user == null ? RandomNumberGenerator.GetBytes(32) : store.Handle(rp, userId);
            string label = "Account " + Convert.ToHexString(handle.AsSpan(0, 5));
            string name = user == null ? label : AuthApp.Text(user.GetValueOrDefault("email")) ?? userId;
            var account = new Fido2User { Id = handle, Name = name, DisplayName = AuthApp.Text(user?.GetValueOrDefault("name")) ?? name };
            var exclude = user == null ? [] : store.ForUser(rp, userId).Select(key => new PublicKeyCredentialDescriptor(System.Buffers.Text.Base64Url.DecodeFromChars(key.Id))).ToList();
            return fido.RequestNewCredential(new RequestNewCredentialParams
            {
                User = account, ExcludeCredentials = exclude,
                AuthenticatorSelection = new AuthenticatorSelection { ResidentKey = ResidentKeyRequirement.Required, UserVerification = UserVerificationRequirement.Required },
            }).ToJson();
        }

        // Fido2 also verifies the challenge and cross-origin flag. Require the exact serialized origin here,
        // since its origin comparison normalizes URLs (including dropping any path).
        private static void CheckClientOrigin(byte[] clientData, string origin)
        {
            using var json = JsonDocument.Parse(clientData);
            var data = json.RootElement;
            if (!data.TryGetProperty("origin", out var clientOrigin) || clientOrigin.GetString() != origin ||
                (data.TryGetProperty("topOrigin", out var top) && top.GetString() != origin))
                throw new Fido2VerificationException("Unexpected client origin.");
        }

        private static RegisteredPublicKeyCredential VerifyRegistration(Fido2 fido, PasskeyStore store, string rp, string origin, string pending, string body)
        {
            var response = JsonSerializer.Deserialize(body, PasskeyJson.Default.AuthenticatorAttestationRawResponse);
            if (response?.Response?.ClientDataJson == null || response.Response.AttestationObject == null)
                throw new Fido2VerificationException("Incomplete registration.");
            CheckClientOrigin(response.Response.ClientDataJson, origin);
            return fido.MakeNewCredentialAsync(new MakeNewCredentialParams
            {
                OriginalOptions = CredentialCreateOptions.FromJson(pending), AttestationResponse = response,
                IsCredentialIdUniqueToUserCallback = (args, _) => Task.FromResult(store.Find(rp, AuthApp.Base64Url(args.CredentialId)) == null),
            }).GetAwaiter().GetResult();
        }

        private static void SignIn(Fido2 fido, AuthRequest auth, string rp, string pending, string body)
        {
            var response = JsonSerializer.Deserialize(body, FidoModelSerializerContext.Default.AuthenticatorAssertionRawResponse);
            if (response?.RawId == null || response.Response?.UserHandle is not { Length: > 0 } ||
                response.Response.ClientDataJson == null || response.Response.Signature == null || response.Response.AuthenticatorData is not { Length: >= 37 } ||
                response.ClientExtensionResults?.AppID == true)
                throw new Fido2VerificationException("Incomplete assertion.");
            CheckClientOrigin(response.Response.ClientDataJson, auth.App.Settings.PasskeyOrigin);
            string id = AuthApp.Base64Url(response.RawId);
            var store = auth.App.PasskeyStore;
            var credential = store.Find(rp, id) ?? throw new Fido2VerificationException("Unknown credential.");
            var result = fido.MakeAssertionAsync(new MakeAssertionParams
            {
                OriginalOptions = AssertionOptions.FromJson(pending), AssertionResponse = response, StoredPublicKey = credential.Key,
                StoredSignatureCounter = credential.Counter,
                IsUserHandleOwnerOfCredentialIdCallback = (args, _) => Task.FromResult(args.UserHandle.AsSpan().SequenceEqual(credential.Handle)),
            }).GetAwaiter().GetResult();
            // BE is immutable over the life of a credential. The flags byte follows the 32-byte RP ID hash.
            bool backupEligible = (response.Response.AuthenticatorData[32] & 0x08) != 0;
            if (credential.BackupEligible != backupEligible || !store.Advance(rp, id, credential, result.SignCount))
                throw new Fido2VerificationException("Credential has changed.");
            if (auth.Users.Record(credential.User) == null) throw new Fido2VerificationException("Account has ended.");
            auth.LoginPasskey(credential.User);
        }
    }
}
