using KScript.Testing;

namespace KScript.Tests;

/// <summary>KScript.Auth's Ownable mixin (#208) and guest identity: @guest(), @owner(), adoption on sign-in (#209).</summary>
public sealed class OwnableTests : StorageFixture
{
    private static void AllPass(TempSite site, string file)
    {
        var result = TestRunner.RunFile(Path.Combine(site.Root, file), new TestRunSettings());
        Assert.True(result.Error == null, result.Error?.ToString());
        Assert.NotEmpty(result.Tests);
        Assert.All(result.Tests, test => Assert.True(test.Outcome == TestOutcome.Passed, test.Name + ": " + test.Message));
    }

    private const string Note = """
        import KScript.Data
        import KScript.Auth

        /// A note on someone's board.
        model Note(timestamps, soft_deletes) extends Ownable {
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
          Post(path: /notes) {
            note = Note->create(title: $json.title)
            return { user: $note.user_id, guest: $note.guest_id }
          }
          Get(path: /notes/{id}) { return Note->mine()->find_or_fail($id).title }
          Get(path: /guest) { return { guest: @guest() } }
          Post(path: /signin) {
            @login(@users()->find_email($json.email))
            return { notes: Note->mine()->pluck(title) }
          }
        }
        """;

    private static TempSite Site(string test, bool adopt = false, string users = "\"users\": \"User\", ", params (string, string)[] more) => new TempSite(
        new[]
        {
            ("kscript.json", "{ \"name\": \"owned\", \"packages\": [\"KScript.Data\"], \"http\": { \"auth\": { " + users + "\"adopt_guest\": " + (adopt ? "true" : "false") + " } } }"),
            ("models/note.kscript", Note),
            ("routes/api.kscript", Routes),
            ("owned.test.kscript", test),
        }.Concat(more).ToArray());

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
              expect(@api('/api/notes', post: { title: 'milk' })->json(), equals: { user: null, guest: "{ $first }" })
              @api('/api/notes', post: { title: 'eggs' })
              expect(@api('/api/notes')->json(), equals: ['milk', 'eggs'])
              expect(Guest->count(), equals: 1)
              expect(Note->first().guest.id, equals: $first)

              @acting_as_guest()
              expect(@api('/api/notes')->json(), equals: [])
              expect(@api('/api/notes/1').status, equals: 404)

              ada = @register('ada@example.test', 'correct-horse-battery-9', name: 'Ada')
              @acting_as($ada)
              expect(@guest(), equals: null)
              expect(@api('/api/notes')->json(), equals: [])
              expect(@api('/api/notes', post: { title: 'bread' })->json(), equals: { user: $ada.id, guest: null })
              expect(Note->mine(as: $ada)->count(), equals: 1)
              expect(Note->mine(as: $ada)->first().user.email, equals: 'ada@example.test')
              expect(Note->where(guest: $first)->count(), equals: 2)
              expect(Note->mine()->first()->is_mine(), equals: true)
            }

            test(name: 'acts as a known guest') {
              id = '3f2a8c1e-9b7d-4c2a-8e1f-0a9b8c7d6e5f'
              expect(@acting_as_guest($id), equals: $id)
              expect(@guest(), equals: $id)
              note = Note->create(title: 'x')
              expect($note->is_mine(), equals: true)
              expect($note.guest->is($id), equals: true)
              @acting_as_guest()
              expect($note->is_mine(), equals: false)
            }

            test(name: 'refuses another owner and a changed owner') {
              ada = @register('ada@example.test', 'correct-horse-battery-9', name: 'Ada')
              ben = @register('ben@example.test', 'correct-horse-battery-9', name: 'Ben')
              @acting_as($ada)
              expect_error(type: Forbidden) {
                Note->create(title: 'x', user: $ben)
              }
              expect_error(type: Forbidden) {
                Note->create(title: 'x', guest: '3f2a8c1e-9b7d-4c2a-8e1f-0a9b8c7d6e5f')
              }
              note = Note->create(title: 'x', user: $ada)
              expect($note.user->is($ada), equals: true)
              expect_error(type: Forbidden) {
                $note->update(user: $ben)
              }
              expect_error(type: Forbidden) {
                $note->update(user_id: $ben.id)
              }
              expect_error(type: Forbidden) {
                Note->where(title: 'x')->update(user: $ben)
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
              expect(@guest(), equals: null)
              expect_error(type: Unauthorized) {
                Note->create(title: 'x')
              }
              expect_error(type: Unauthorized) {
                Note->mine()->count()
              }
              expect_error(type: Argument) {
                Note->create(title: 'x', user: 1, guest: '3f2a8c1e-9b7d-4c2a-8e1f-0a9b8c7d6e5f')
              }
              ada = User->create(name: 'Ada', email: 'ada@example.test', password: 'correct-horse-battery-9')
              Note->create(title: 'seeded', user: $ada)
              Note->create(title: 'kept', guest: '3f2a8c1e-9b7d-4c2a-8e1f-0a9b8c7d6e5f')
              expect(Note->mine(as: $ada)->pluck(title), equals: ['seeded'])
              expect(Guest->count(), equals: 1)
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

    [Fact]
    public void A_project_copy_of_Guest_changes_the_guest_ids()
    {
        // Guests numbered by the database: a new guest is stored at once, to get its number.
        using var site = Site("""
            import KScript.Auth
            import KScript.Data
            import KScript.Net
            import "models/note.kscript"

            test(name: 'numbers guests') {
              first = @acting_as_guest()
              expect($first, equals: 1)
              expect(Guest->count(), equals: 1)
              expect(Note->create(title: 'x').guest_id, equals: 1)
              expect(Note->create(title: 'y').guest.label, equals: 'new')
              expect_error(type: Argument) {
                @acting_as_guest('not-a-number')
              }
            }
            """, more: ("models/guest.kscript", """
            import KScript.Data

            model Guest {
              label: string = 'new'
            }
            """));
        AllPass(site, "owned.test.kscript");
    }

    [Fact]
    public void Ownable_needs_the_users_in_a_model()
    {
        using var site = Site("""
            import KScript.Auth
            import KScript.Data
            import "models/note.kscript"

            test(name: 'says where the users go') {
              @acting_as({ id: 1 })
              expect_error(type: Config, contains: 'Add "users": "User" to http.auth') {
                Note->create(title: 'x')
              }
            }
            """, users: "");
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
              user = @register('ada@example.test', 'correct-horse-battery-9', name: 'Ada')
              guest = '3f2a8c1e-9b7d-4c2a-8e1f-0a9b8c7d6e5f'
              Note->create(title: 'milk', guest: $guest)
              Note->create(title: 'gone', guest: $guest)->delete()
              Note->create(title: 'theirs', guest: '0b1c2d3e-4f50-4a6b-8c7d-9e0f1a2b3c4d')

              reply = @api('/api/signin', post: { email: 'ada@example.test' }, options: { headers: { Cookie: "ks_guest={ $guest }" } })
              expect($reply->json().notes, equals: ADOPTED)
              expect(Note->with_trashed()->mine(as: $user)->count(), equals: COUNT)
              expect(Note->with_trashed()->where(guest: $guest)->count(), equals: LEFT)
              expect(Note->where(guest: '0b1c2d3e-4f50-4a6b-8c7d-9e0f1a2b3c4d')->count(), equals: 1)
              cleared = $reply.headers['Set-Cookie']->to_json()->contains('ks_guest=; Path=/')
              expect($cleared, equals: CLEARED)
            }
            """.Replace("ADOPTED", adopt ? "['milk']" : "[]").Replace("COUNT", adopt ? "2" : "0").Replace("LEFT", adopt ? "0" : "2").Replace("CLEARED", adopt ? "true" : "false"), adopt);
        AllPass(site, "owned.test.kscript");
    }
}
