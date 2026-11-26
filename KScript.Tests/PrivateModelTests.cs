namespace KScript.Tests;

/// <summary>private on a model: the column is stored and loaded, and only the model's own methods read it.</summary>
public class PrivateModelTests : DataFixture
{
    [Fact]
    public void A_private_model_field_is_stored_but_not_reachable_from_outside()
    {
        string output = Run("""
            model Account {
              name: string
              private token: string = 'none'

              private fn masked() => $token->upper()
              fn hint() => $this->masked()
            }

            $db->create_table(Account)
            a = Account->create(name: 'Ada')
            found = Account->find($a.id)
            echo $found->hint()
            echo $found->to_json()
            try {
              echo $found.token
            } catch e {
              echo $e.message
            }
            try {
              Account->create(name: 'Bob', token: 'secret')
            } catch e {
              echo $e.message
            }
            """);
        Assert.Equal("NONE\n{\"id\":1,\"name\":\"Ada\"}\n'token' is private to Account. Only Account's own methods can use it.\n"
            + "The Account doesn't fit Account (1 problem):\n  $.token: is private to Account and can't be passed in.\n", output);
    }
}
