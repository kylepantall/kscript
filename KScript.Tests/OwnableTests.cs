using KScript.Testing;

namespace KScript.Tests;

/// <summary>KScript.Auth's Ownable mixin (#208) and guest identity: @guest(), @owner(), adoption on sign-in (#209).</summary>
public sealed class OwnableTests : StorageFixture
{
    private static void AllPass(TempSite site, string file)
    {
        var result = TestRunner.RunFile(Path.Combine(site.Root, file), new TestRunSettings());
        Assert.Null(result.Error);
        Assert.NotEmpty(result.Tests);
        Assert.All(result.Tests, test => Assert.True(test.Outcome == TestOutcome.Passed, test.Name + ": " + test.Message));
    }

    private const string Note = """
        import KScript.Data
        import KScript.Auth

        /// A note on someone's board.
        model Note extends [Ownable, Timestamps, SoftDeletes] {
          title: string = ''
        }
        """;

    private const string Routes = """
        import KScript.Auth
        import KScript.Data
        import KScript.Http
        import "../models/note.kscript"

        Routes {
          Get(path: /notes) { return Note->mine()->order_by(id)->pluck(title) }
          Post(path: /notes) { return Note->create(title: $json.title).owner }
          Get(path: /notes/{id}) { return Note->mine()->find_or_fail($id).title }
          Get(path: /guest) { return { guest: @guest(), owner: @owner() } }
          Post(path: /signin) {
            @login(@users()->find_email($json.email))
            return { owner: @owner(), notes: Note->mine()->pluck(title) }
          }
        }
        """;

    private static TempSite Site(string test, bool adopt = false) => new TempSite(
        ("kscript.json", "{ \"name\": \"owned\", \"packages\": [\"KScript.Data\"], \"http\": { \"auth\": { \"adopt_guest\": " + (adopt ? "true" : "false") + " } } }"),
        ("models/note.kscript", Note),
        ("routes/api.kscript", Routes),
        ("owned.test.kscript", test));

    [Fact]
    public void Each_guest_and_user_sees_only_their_own_rows()
    {
        using var site = Site("""
            import KScript.Auth
            import KScript.Data
            import KScript.Net
            import "models/note.kscript"

            test(name: 'keeps rows to their owner') {
              first = @acting_as_guest()
              expect(@api('/api/notes', post: { title: 'milk' }).text, equals: "guest:{ $first }")
              @api('/api/notes', post: { title: 'eggs' })
              expect(@api('/api/notes')->json(), equals: ['milk', 'eggs'])

              @acting_as_guest()
              expect(@api('/api/notes')->json(), equals: [])
              expect(@api('/api/notes/1').status, equals: 404)

              @acting_as({ id: 7 })
              expect(@guest(), equals: null)
              expect(@api('/api/notes')->json(), equals: [])
              expect(@api('/api/notes', post: { title: 'bread' }).text, equals: 'user:7')
              expect(Note->mine(as: { id: 7 })->count(), equals: 1)
              expect(Note->mine(as: "guest:{ $first }")->count(), equals: 2)
            }

            test(name: 'acts as a known guest') {
              id = '3f2a8c1e-9b7d-4c2a-8e1f-0a9b8c7d6e5f'
              expect(@acting_as_guest($id), equals: $id)
              expect(@guest(), equals: $id)
              expect(@owner(), equals: "guest:{ $id }")
              expect(Note->create(title: 'x')->is_mine(), equals: true)
            }

            test(name: 'refuses another owner and a changed owner') {
              @acting_as({ id: 1 })
              expect_error(type: Forbidden) {
                Note->create(title: 'x', owner: 'user:2')
              }
              note = Note->create(title: 'x', owner: 'user:1')
              expect($note.owner, equals: 'user:1')
              expect_error(type: Forbidden) {
                $note->update(owner: 'user:2')
              }
              expect_error(type: Forbidden) {
                Note->where(title: 'x')->update(owner: 'user:2')
              }
              expect($note->update(title: 'y').title, equals: 'y')
            }
            """);
        AllPass(site, "owned.test.kscript");
    }

    [Fact]
    public void Outside_a_request_the_owner_is_named()
    {
        using var site = Site("""
            import KScript.Auth
            import KScript.Data
            import "models/note.kscript"

            test(name: 'needs an owner') {
              expect(@owner(), equals: null)
              expect(@guest(), equals: null)
              expect_error(type: Unauthorized) {
                Note->create(title: 'x')
              }
              expect_error(type: Unauthorized) {
                Note->mine()->count()
              }
              Note->create(title: 'seeded', owner: @owner(as: { id: 3 }))
              expect(Note->mine(as: 'user:3')->pluck(title), equals: ['seeded'])
              expect(@owner(as: 'guest:abc'), equals: 'guest:abc')
              expect(@owner(as: 5), equals: 'user:5')
            }
            """);
        AllPass(site, "owned.test.kscript");
    }

    [Fact]
    public void A_browser_keeps_its_guest_id_in_a_cookie()
    {
        using var site = Site("""
            import KScript.Net

            test(name: 'gives a guest an id once') {
              first = @api('/api/guest')
              guest = $first->json().guest
              expect($guest->is_a(uuid), equals: true)
              expect($first->json().owner, equals: "guest:{ $guest }")
              cookie = $first.headers['Set-Cookie']
              expect($cookie->starts_with("ks_guest={ $guest }; Path=/; HttpOnly; SameSite=Lax; Max-Age=31536000"), equals: true)

              again = @api('/api/guest', options: { headers: { Cookie: "ks_guest={ $guest }" } })
              expect($again->json().guest, equals: $guest)
              expect($again.headers['Set-Cookie'], equals: null)

              made_up = @api('/api/guest', options: { headers: { Cookie: 'ks_guest=not-a-uuid' } })
              expect($made_up->json().guest == 'not-a-uuid', equals: false)
            }
            """);
        AllPass(site, "owned.test.kscript");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Signing_in_moves_the_guests_rows_when_adopt_guest_is_on(bool adopt)
    {
        using var site = Site("""
            import KScript.Auth
            import KScript.Data
            import KScript.Net
            import "models/note.kscript"

            test(name: 'signs in') {
              user = @register('ada@example.test', 'correct-horse-battery-9')
              guest = '3f2a8c1e-9b7d-4c2a-8e1f-0a9b8c7d6e5f'
              Note->create(title: 'milk', owner: "guest:{ $guest }")
              Note->create(title: 'gone', owner: "guest:{ $guest }")->delete()
              Note->create(title: 'theirs', owner: 'guest:0b1c2d3e-4f50-4a6b-8c7d-9e0f1a2b3c4d')

              reply = @api('/api/signin', post: { email: 'ada@example.test' }, options: { headers: { Cookie: "ks_guest={ $guest }" } })
              expect($reply->json().owner, equals: "user:{ $user.id }")
              expect($reply->json().notes, equals: ADOPTED)
              expect(Note->with_trashed()->mine(as: $user)->count(), equals: COUNT)
              expect(Note->mine(as: 'guest:0b1c2d3e-4f50-4a6b-8c7d-9e0f1a2b3c4d')->count(), equals: 1)
              cleared = $reply.headers['Set-Cookie']->to_json()->contains('ks_guest=; Path=/')
              expect($cleared, equals: CLEARED)
            }
            """.Replace("ADOPTED", adopt ? "['milk']" : "[]").Replace("COUNT", adopt ? "2" : "0").Replace("CLEARED", adopt ? "true" : "false"), adopt);
        AllPass(site, "owned.test.kscript");
    }
}
